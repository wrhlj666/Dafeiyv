using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyv
{
    public partial class MainWindow : Window
    {
        // ========== 记忆相关 ==========
        private List<(string role, string content)> conversationHistory = new List<(string, string)>();
        private int _maxHistoryRounds = 10;   // 最大记忆轮数（10/5/0，0=无记忆），可在设置中调整

        // ========== 字段 ==========
        private DispatcherTimer bubbleTimer;
        private DeepSeekService _aiService;
        private SudokuWindow _sudokuWindow;   // 非模态数独窗口（可反复打开，无需重建）
        private bool isInputVisible = false;
        private bool _isSending;   // 防止并发发送
        private bool _enableMaxReplyLength;   // 是否限制最大回复字数
        private int _maxReplyLength = 100;    // 自定义最大回复字数
        private bool _enableThinking = true;  // 主聊天是否开启深度思考（默认开）
        private bool _enableLongTermMemory = false;   // 是否启用长期记忆（默认关闭）
        private int _memoryMaxChars = 40;     // 每条长期记忆最大字数
        private MemoryStore? _memoryStore;    // 长期记忆文档（Memory.txt）
        private string? _pendingInteraction;   // 互动状态槽位：选中互动后下一条消息会带上，发送后清空

        // ===== DSH 工作状态（来自 dsh-pet-status 插件的状态文件）=====
        private DshCompanion? _dshCompanion;
        private bool _statusBubbleActive;      // 工作状态气泡锁生效中
        private string _lastStatusText = "";   // 最近一次工作状态文案（临时气泡到期后恢复用）

        // ===== 底部状态小字：余额 · 峰谷期 =====
        private bool _balanceBusy;             // 余额查询进行中（防连点）
        private string _balanceShort = "";     // 最近一次余额（短格式，如 ¥110.00）
        private DispatcherTimer? _statusLineTimer;   // 小字刷新（保证峰谷切换及时）
        private DispatcherTimer? _balanceTimer;      // 余额定时刷新

        // 敲（bonk.gif 一个循环约 0.58s）：锤子约在播到一半时落下，此刻才把鱼打飞
        private const int BonkHitDelayMs = 300;

        // 漫游蹦跳力度（px/s）：
        //   横向速度 = ±(RoamVxBase ~ RoamVxBase + RoamVxRandom)
        //   向上速度 = (RoamVyBase ~ RoamVyBase + RoamVyRandom)，越大跳得越高越远。
        // 参考：重力 1800，1 秒后相对起跳点的高度 ≈ 向上速度 − 900，
        // 想让 1 秒后定格在空中，RoamVyBase 应 ≥ 1000。
        private const double RoamVxBase = 300;
        private const double RoamVxRandom = 700;
        private const double RoamVyBase = 1000;
        private const double RoamVyRandom = 1500;

        // 睡熟时的提示语
        private const string SleepHint = "💤 她睡着了……用「敲」把她叫醒吧！";

        // 宠物窗口基础尺寸（XAML 中 260×400），随 PetScale 整体缩放
        private const double BaseWindowWidth = 260.0;
        private const double BaseWindowHeight = 400.0;

        public double PetScale { get; private set; } = 1.0;

        public PetBehavior Pet { get; private set; }
        public PhysicsHelper Physics { get; private set; }
        public bool IsSending => _isSending;   // 供 PetBehavior 判断是否正在对话/思考
        public bool IsMouseGrabbed => _mouseDown;   // 供 PetBehavior 判断是否正在被拖拽/抓取
        public DeepSeekService AiService => _aiService;   // 供数独等窗口调用 AI

        /// <summary>当前用户自拟提示词（数独复用同一套性格/说话风格）</summary>
        public string CurrentUserPrompt => _aiService?.CurrentSystemPrompt ?? "";

        // ========== 鼠标拖拽（Pro 大神方案） ==========
        private bool _mouseDown;            // 左键是否按下
        private bool _isDragging;           // 是否已判定为拖拽（移动超过阈值）
        private bool _stoppedFlyingOnGrab;  // 抓取时是否暂停了飞行中的物理
        private double _dragDistance;        // 累计移动量，用于区分“点击”和“拖拽”
        private Point _grabOffset;
        private double _lastMoveTime;
        private readonly Queue<(double vx, double vy)> _velSamples = new();
        private const int MaxVelSamples = 5;
        private const double DragStartThreshold = 3.0;  // 超过此距离才视为拖拽
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        // ========== 依赖属性 ==========
        public string CurrentGifPath
        {
            get { return (string)GetValue(CurrentGifPathProperty); }
            set { SetValue(CurrentGifPathProperty, value); }
        }

        public static readonly DependencyProperty CurrentGifPathProperty =
            DependencyProperty.Register("CurrentGifPath", typeof(string), typeof(MainWindow),
                new PropertyMetadata("Images/Standby_fan.gif"));

        // ========== 构造函数 ==========
        public MainWindow()
        {
            InitializeComponent();
            CreateTrayIcon();   // 任务栏隐藏后，靠托盘图标显示/隐藏/退出

            bubbleTimer = new DispatcherTimer();
            bubbleTimer.Interval = TimeSpan.FromSeconds(3);
            bubbleTimer.Tick += BubbleTimer_Tick;

            Pet = new PetBehavior(this);
            InitDshCompanion();
            Pet.ProactiveSpeakRequested += OnProactiveSpeakRequested;
            Pet.RoamRequested += OnRoamRequested;
            Physics = new PhysicsHelper(this);

            string apiKey = null;
            string systemPrompt = null;
            double petScale = 1.0;
            int idleInterval = 10;
            bool proactiveSpeakEnabled = true;     // 主动说话开关
            int proactiveIntervalSeconds = 300;    // 主动说话间隔（秒，最低 10）
            bool roamEnabled = true;               // 漫游开关
            int roamIntervalSeconds = 30;          // 漫游间隔（秒，最低 1）

            // 启动欢迎语（可在设置中自定义）
            string welcome1 = "大肥鱼来啦！";
            string welcome2 = "今天想聊什么？";
            string welcome3 = "点我聊天～";
            string welcome4 = "我在这里哦！";

            string configPath = "Settings.json";
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        if (!string.IsNullOrEmpty(config.ApiKey))
                            apiKey = config.ApiKey;
                        if (!string.IsNullOrEmpty(config.SystemPrompt))
                            systemPrompt = config.SystemPrompt;
                        if (config.PetScale >= 0.5 && config.PetScale <= 2.0)
                            petScale = config.PetScale;
                        if (config.IdleActionIntervalSeconds >= 10)
                            idleInterval = config.IdleActionIntervalSeconds;
                        if (!string.IsNullOrWhiteSpace(config.Welcome1)) welcome1 = config.Welcome1;
                        if (!string.IsNullOrWhiteSpace(config.Welcome2)) welcome2 = config.Welcome2;
                        if (!string.IsNullOrWhiteSpace(config.Welcome3)) welcome3 = config.Welcome3;
                        if (!string.IsNullOrWhiteSpace(config.Welcome4)) welcome4 = config.Welcome4;
                        if (config.MaxHistoryRounds is 0 or 5 or 10)
                            _maxHistoryRounds = config.MaxHistoryRounds;
                        if (config.MaxReplyLength >= 1)
                            _maxReplyLength = config.MaxReplyLength;
                        _enableMaxReplyLength = config.EnableMaxReplyLength;
                        _enableThinking = config.EnableThinking;
                        _enableLongTermMemory = config.EnableLongTermMemory;
                        if (config.MemoryEntryMaxChars >= 10)
                            _memoryMaxChars = config.MemoryEntryMaxChars;
                        proactiveSpeakEnabled = config.EnableProactiveSpeak;
                        if (config.ProactiveSpeakIntervalSeconds >= 10)
                            proactiveIntervalSeconds = config.ProactiveSpeakIntervalSeconds;
                        roamEnabled = config.EnableRoam;
                        if (config.RoamIntervalSeconds >= 1)
                            roamIntervalSeconds = config.RoamIntervalSeconds;
                    }
                }
                catch { }
            }

            _aiService = new DeepSeekService(apiKey, systemPrompt);
            _aiService.SetMaxReplyLength(_enableMaxReplyLength, _maxReplyLength);
            _aiService.SetThinking(_enableThinking);

            // 长期记忆文档（与 Settings.json 同在 exe 目录）
            _memoryStore = new MemoryStore(System.IO.Path.Combine(AppContext.BaseDirectory, "Memory.txt"));
            RefreshMemoryPrompt();

            // 应用配置里的宠物缩放与随机动作间隔
            ApplyPetScale(petScale);
            SetIdleInterval(idleInterval);
            ApplyProactiveSpeak(proactiveSpeakEnabled, proactiveIntervalSeconds);
            ApplyRoam(roamEnabled, roamIntervalSeconds);

            this.Closing += MainWindow_Closing;

            this.Loaded += (s, e) =>
            {
                // 启动先播放打招呼动画（hello1.gif 播一遍，约 2.3s）
                Pet.PlayActionSequence(("Interact/hello1.gif", 2.5, false));

                string[] welcomeMessages = { welcome1, welcome2, welcome3, welcome4 };
                var random = new Random();
                ShowBubble(welcomeMessages[random.Next(welcomeMessages.Length)], 4);

                InitStatusLine();   // 底部小字：余额 · 峰谷期
            };
        }

        // ========== 气泡相关 ==========
        private void BubbleTimer_Tick(object sender, EventArgs e)
        {
            bubbleTimer.Stop();
            // 工作状态气泡锁：临时气泡（如聊天回复）到期后，把工作状态气泡恢复回来
            if (_statusBubbleActive && Pet.IsWorkMode && !string.IsNullOrEmpty(_lastStatusText))
            {
                BubbleText.Text = _lastStatusText;
                BubbleBorder.Visibility = Visibility.Visible;
                return;
            }
            BubbleBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>普通气泡。force=false 时在 DSH 工作状态气泡锁定期间会被忽略。</summary>
        public void ShowBubble(string text, double durationSeconds = 3, bool force = false)
        {
            if (_statusBubbleActive && !force) return;   // 锁气泡：工作状态气泡不被顶掉

            BubbleText.Text = text;
            BubbleBorder.Visibility = Visibility.Visible;
            bubbleTimer.Stop();
            bubbleTimer.Interval = TimeSpan.FromSeconds(durationSeconds);
            bubbleTimer.Start();
        }

        /// <summary>显示被锁定的 DSH 工作状态气泡（常驻，直到状态结束）</summary>
        private void ShowStatusBubble(string text)
        {
            _lastStatusText = text;
            _statusBubbleActive = true;
            BubbleText.Text = text;
            BubbleBorder.Visibility = Visibility.Visible;
            bubbleTimer.Stop();   // 不自动消失
        }

        private void HideStatusBubble()
        {
            _statusBubbleActive = false;
            HideBubble();
        }

        public void HideBubble()
        {
            BubbleBorder.Visibility = Visibility.Collapsed;
            bubbleTimer.Stop();
        }

        // ========== DSH 工作状态 ==========

        /// <summary>启动 DSH 状态监听（读 dsh-dafeiyu 插件输出的协议事件流）</summary>
        private void InitDshCompanion()
        {
            try
            {
                _dshCompanion = new DshCompanion("", Dispatcher);
                _dshCompanion.StatusChanged += OnDshStatusChanged;
                _dshCompanion.Start();
            }
            catch
            {
                _dshCompanion = null;   // 监听不可用不影响桌宠其他功能
            }
        }

        /// <summary>
        /// DSH 状态变化：工作类状态进入工作模式（锁气泡/锁小动作/锁睡觉 + 循环动画），
        /// 完成/出错播一下对应情绪，空闲则恢复常态。
        /// </summary>
        private void OnDshStatusChanged(DshStatus status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnDshStatusChanged(status)));
                return;
            }

            switch (status.State)
            {
                case DshState.Thinking:
                    Pet.EnterWorkMode(PetBehavior.WorkMode.Thinking);
                    ShowStatusBubble(status.BuildBubbleText());
                    break;

                case DshState.Working:
                    Pet.EnterWorkMode(PetBehavior.WorkMode.Working);
                    ShowStatusBubble(status.BuildBubbleText());
                    break;

                case DshState.Waiting:
                    Pet.EnterWorkMode(PetBehavior.WorkMode.Waiting);
                    ShowStatusBubble(status.BuildBubbleText());
                    break;

                case DshState.Success:
                    Pet.ExitWorkMode();
                    HideStatusBubble();
                    Pet.SetEmotion("开心");
                    ShowBubble("🎉 DSH 完成任务啦！", 4, force: true);
                    break;

                case DshState.Error:
                    Pet.ExitWorkMode();
                    HideStatusBubble();
                    Pet.SetEmotion("大哭");
                    ShowBubble("😢 DSH 那边出错了…", 4, force: true);
                    break;

                default:   // Idle / Disconnected / Unknown：恢复常态
                    if (Pet.IsWorkMode) Pet.ExitWorkMode();
                    // 只有当前还挂着工作状态气泡时才收起，避免把刚弹出的完成/出错气泡一起吞掉
                    if (_statusBubbleActive) HideStatusBubble();
                    break;
            }
        }

        // ========== 鼠标事件（Pro 大神方案） ==========

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ctrl + 左键 清空记忆
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                conversationHistory.Clear();
                ShowBubble("🧹 记忆已清空", 2);
                return;
            }

            // 点击输入框 / 发送按钮时不要拖动窗口，让它们正常工作
            if (InputPanel.Visibility == Visibility.Visible && InputPanel.IsMouseOver)
            {
                return;
            }

            // 点击/抓取都算互动：打断犯困计时（睡熟 sleep3 不会醒，见 NotifyInteraction）
            Pet.NotifyInteraction();

            // 点击/抓取只打断随机动画，不打断互动动画（摸头/砸/敲/打招呼/蹲大牢）
            if (Pet.IsPlayingAction && !Pet.IsActionSequenceActive)
            {
                Pet.InterruptAction();
            }

            // 抓取飞行中的桌宠时，先暂停它的物理，让它停住
            _stoppedFlyingOnGrab = false;
            if (Physics != null && Physics.IsFlying)
            {
                Physics.Stop();
                _stoppedFlyingOnGrab = true;
            }

            _mouseDown = true;
            _isDragging = false;
            _dragDistance = 0.0;
            _velSamples.Clear();
            _grabOffset = e.GetPosition(this);
            _lastMoveTime = _clock.Elapsed.TotalSeconds;

            // 关键：必须在处理鼠标事件的同一元素（Grid）上捕获鼠标，
            // 否则 MouseMove / MouseLeftButtonUp 不会派发到这里，导致“松不开”。
            (sender as UIElement)?.CaptureMouse();
            e.Handled = true;
        }

        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_mouseDown) return;

            Point rel = e.GetPosition(this);
            double now = _clock.Elapsed.TotalSeconds;
            double dt = now - _lastMoveTime;
            _lastMoveTime = now;
            if (dt <= 0) dt = 0.001;

            double dx = rel.X - _grabOffset.X;
            double dy = rel.Y - _grabOffset.Y;
            _dragDistance += Math.Abs(dx) + Math.Abs(dy);

            // 移动距离超过阈值才判定为“拖拽”
            if (!_isDragging && _dragDistance > DragStartThreshold)
            {
                _isDragging = true;
                _velSamples.Clear();
            }

            if (_isDragging)
            {
                _velSamples.Enqueue((dx / dt, dy / dt));
                while (_velSamples.Count > MaxVelSamples) _velSamples.Dequeue();

                Left += dx;
                Top += dy;
            }
        }

        private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_mouseDown) return;

            _mouseDown = false;
            bool wasDragging = _isDragging;
            _isDragging = false;
            (sender as UIElement)?.ReleaseMouseCapture();

            if (!wasDragging)
            {
                // 睡熟时不能聊天：提示，不打开输入框（顺带关掉已开着的输入框）
                if (Pet.IsAsleep)
                {
                    if (isInputVisible)
                    {
                        isInputVisible = false;
                        InputPanel.Visibility = Visibility.Collapsed;
                    }
                    ShowBubble(SleepHint, 2.5);
                    e.Handled = true;
                    return;
                }

                // 没有移动：视为“点击”，切换输入框（原功能不变）
                if (_stoppedFlyingOnGrab && Physics != null && Physics.EnablePhysics)
                {
                    // 点击时若暂停了飞行，让它继续轻轻下落，避免悬空
                    Physics.StartFalling();
                }
                ToggleInputPanel();
                e.Handled = true;
                return;
            }

            // 有移动：按拖拽速度抛出（飞出去）
            if (_velSamples.Count > 0 && Physics != null && Physics.EnablePhysics)
            {
                double sx = 0, sy = 0;
                foreach (var s in _velSamples) { sx += s.vx; sy += s.vy; }
                Physics.Throw(sx / _velSamples.Count, sy / _velSamples.Count);
            }
            e.Handled = true;
        }

        private void Grid_LostMouseCapture(object sender, MouseEventArgs e)
        {
            // 鼠标捕获被系统夺走时也要复位，避免卡在拖拽状态
            _mouseDown = false;
            _isDragging = false;
        }

        private void ToggleInputPanel()
        {
            // 切换输入框（原功能不变）
            isInputVisible = !isInputVisible;
            InputPanel.Visibility = isInputVisible ? Visibility.Visible : Visibility.Collapsed;

            if (isInputVisible)
            {
                InputTextBox.Focus();
                HideBubble();
            }
        }

        // ========== 发送消息 ==========
        private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SendMessage();
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendMessage();
        }

        private async Task SendMessage()
        {
            // 睡熟时不能对话
            if (Pet.IsAsleep)
            {
                ShowBubble(SleepHint, 2.5);
                return;
            }

            if (_isSending) return;   // 防止连按 Enter / 连点发送导致并发请求

            string userInput = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(userInput))
                return;

            Pet.NotifyInteraction();   // 发消息也算互动，重置 120s 无互动计时
            Pet.ResetProactiveTimer();   // 对话后重新开始主动说话倒计时

            _isSending = true;
            InputTextBox.IsEnabled = false;
            SendButton.IsEnabled = false;
            try
            {
                await SendMessageCore(userInput);
            }
            finally
            {
                _isSending = false;
                InputTextBox.IsEnabled = true;
                SendButton.IsEnabled = true;
            }
        }

        private async Task SendMessageCore(string userInput)
        {
            // 取走互动槽位并清空：这条消息带上互动信息，下一条不再带
            string interactionPrefix = _pendingInteraction ?? "";
            _pendingInteraction = null;

            string fullInput = interactionPrefix + userInput;   // 例如 (用户敲了你一下)+内容

            InputTextBox.Text = "";
            InputPanel.Visibility = Visibility.Collapsed;
            isInputVisible = false;

            ShowBubble($"你说：{fullInput}", 1.5);
            await Task.Delay(1500);

            if (_maxHistoryRounds > 0)
            {
                conversationHistory.Add(("user", fullInput));
                TrimHistory();
            }

            ShowBubble("思考中...", 10);
            Pet.SetEmotion("思考中");

            // 发送前刷新长期记忆（用户手动编辑 Memory.txt 后，下一条消息立即生效）
            if (_enableLongTermMemory && _memoryStore != null)
                _aiService.SetLongTermMemory(_memoryStore.BuildPromptBlock(_memoryMaxChars));

            string reply = await _aiService.SendMessageAsync(fullInput, conversationHistory);

            ProcessAiReply(reply, 5, force: true);   // 用户主动聊天：允许临时顶开工作状态气泡
        }

        /// <summary>统一处理 AI 回复：提取长期记忆、去掉【记忆】行、写入历史、解析情绪并展示</summary>
        private void ProcessAiReply(string reply, double bubbleSeconds, bool force = false)
        {
            // 提取 AI 提议的新记忆并写入文档
            if (_enableLongTermMemory && _memoryStore != null)
            {
                if (_memoryStore.AppendFromReply(reply, _memoryMaxChars) > 0)
                    _aiService.SetLongTermMemory(_memoryStore.BuildPromptBlock(_memoryMaxChars));
            }
            // 把「【记忆】」行从展示内容里去掉
            reply = MemoryStore.StripMemoryLines(reply);

            if (_maxHistoryRounds > 0)
            {
                conversationHistory.Add(("assistant", reply));
                TrimHistory();
            }

            var (emotion, message) = _aiService.ParseEmotion(reply);

            Pet.SetEmotion(emotion);

            // 用户主动聊天的回复可以临时顶开工作状态气泡（到期后自动恢复）
            ShowBubble($" {message}", bubbleSeconds, force);
        }

        /// <summary>桌宠主动说话：定时器触发，用户长时间没回复时自己开启话题</summary>
        public async void SpeakOnOwn()
        {
            if (_isSending) return;   // 正在对话/思考中就不插话

            _isSending = true;
            try
            {
                // 只把“她自己说过的话”作为上下文，不带上用户的话，避免重复或接错茬
                var selfHistory = conversationHistory
                    .Where(entry => entry.role == "assistant")
                    .ToList();

                const string triggerPrompt = "（用户长时间没有回复你，你打算主动发话，自然地开启一个新话题，说一句就好）";

                ShowBubble("思考中...", 10);
                Pet.SetEmotion("思考中");

                if (_enableLongTermMemory && _memoryStore != null)
                    _aiService.SetLongTermMemory(_memoryStore.BuildPromptBlock(_memoryMaxChars));

                string reply = await _aiService.SendMessageAsync(triggerPrompt, selfHistory);

                ProcessAiReply(reply, 6);
            }
            finally
            {
                _isSending = false;
            }
        }

        private void OnProactiveSpeakRequested() => SpeakOnOwn();

        /// <summary>漫游：随机力度抛飞，让她随便跳，等物理自然停下（落地静止/被抓住）后恢复原物理开关状态</summary>
        private async void OnRoamRequested()
        {
            if (Physics == null) return;

            bool wasEnabled = Physics.EnablePhysics;
            Physics.EnablePhysics = true;

            var rand = new Random();
            double dir = rand.NextDouble() < 0.5 ? -1 : 1;
            double vx = dir * (RoamVxBase + rand.NextDouble() * RoamVxRandom);
            double vy = -(RoamVyBase + rand.NextDouble() * RoamVyRandom);
            Physics.Throw(vx, vy);

            // 不设定格：让她自由弹跳直到自然停下，然后恢复原来的物理开关状态
            if (!wasEnabled)
            {
                while (Physics.IsFlying)
                {
                    await Task.Delay(200);
                }
                Physics.EnablePhysics = false;
            }
        }

        /// <summary>应用漫游设置（开关 + 间隔秒数，最低 1）</summary>
        public void ApplyRoam(bool enabled, int intervalSeconds)
        {
            Pet?.SetRoamEnabled(enabled);
            Pet?.SetRoamInterval(intervalSeconds);
        }

        /// <summary>应用主动说话设置（开关 + 间隔秒数，最低 10）</summary>
        public void ApplyProactiveSpeak(bool enabled, int intervalSeconds)
        {
            Pet?.SetProactiveEnabled(enabled);
            Pet?.SetProactiveInterval(intervalSeconds);
        }

        /// <summary>按当前最大记忆轮数裁剪历史（0 轮 = 无记忆）</summary>
        private void TrimHistory()
        {
            int maxEntries = _maxHistoryRounds * 2;
            if (maxEntries == 0)
            {
                conversationHistory.Clear();
                return;
            }
            while (conversationHistory.Count > maxEntries)
            {
                conversationHistory.RemoveAt(0);
            }
        }

        /// <summary>应用最大记忆轮数设置（10/5/0），立即按新上限裁剪</summary>
        public void ApplyMemorySettings(int maxHistoryRounds)
        {
            _maxHistoryRounds = maxHistoryRounds;
            TrimHistory();
        }

        /// <summary>应用最大回复字数设置（无限制/自定义）</summary>
        public void ApplyReplyLengthSetting(bool enabled, int maxLength)
        {
            _enableMaxReplyLength = enabled;
            _maxReplyLength = Math.Max(1, maxLength);
            _aiService?.SetMaxReplyLength(enabled, _maxReplyLength);
        }

        /// <summary>应用主聊天深度思考开关（关 = 回复快、省 token）</summary>
        public void ApplyThinkingSetting(bool enabled)
        {
            _enableThinking = enabled;
            _aiService?.SetThinking(enabled);
        }

        /// <summary>应用长期记忆设置（启用开关 + 每条字数），立即刷新随提示词发出的内容</summary>
        public void ApplyLongTermMemory(bool enabled, int maxChars)
        {
            _enableLongTermMemory = enabled;
            if (maxChars >= 10)
                _memoryMaxChars = maxChars;
            RefreshMemoryPrompt();
        }

        /// <summary>用当前设置重建长期记忆提示词片段并交给 AI 服务</summary>
        private void RefreshMemoryPrompt()
        {
            if (_aiService == null || _memoryStore == null) return;
            _aiService.SetLongTermMemory(
                _enableLongTermMemory ? _memoryStore.BuildPromptBlock(_memoryMaxChars) : "");
        }

        /// <summary>打开长期记忆文档（Memory.txt）供用户手动增删</summary>
        public void OpenMemoryFile()
        {
            _memoryStore?.OpenInEditor();
        }

        // ========== 右键菜单事件 ==========

        private void Menu_ShowHistory_Click(object sender, RoutedEventArgs e)
        {
            if (conversationHistory.Count == 0)
            {
                ShowBubble("📭 还没有对话记录哦", 2);
                return;
            }

            string historyText = "";
            int startIndex = Math.Max(0, conversationHistory.Count - 20);

            for (int i = startIndex; i < conversationHistory.Count; i++)
            {
                var entry = conversationHistory[i];
                string role = entry.role == "user" ? "👤 你" : "🐟 大肥鱼";
                historyText += $"{role}: {entry.content}\n";
            }

            if (conversationHistory.Count > 20)
            {
                historyText = "--- 最近20条 ---\n" + historyText;
            }

            var historyWindow = new HistoryWindow(historyText, this);
            historyWindow.Owner = this;
            historyWindow.ShowDialog();
        }

        private void Menu_ActionPet_Click(object sender, RoutedEventArgs e)
        {
            if (Pet.IsAsleep) { ShowBubble(SleepHint, 2.5); return; }   // 睡熟时只有敲能叫醒
            Pet.NotifyInteraction();
            Pet.ResetProactiveTimer();   // 互动后重新开始主动说话倒计时

            // 摸头：pettin 循环播放 3 秒
            Pet.PlayActionSequence(("Interact/pettin.gif", 3, true));

            // 记录互动状态：下一条消息会带上 (用户摸了摸你的头)
            _pendingInteraction = "(用户摸了摸你的头)";
        }

        private void Menu_ActionHit_Click(object sender, RoutedEventArgs e)
        {
            if (Pet.IsAsleep) { ShowBubble(SleepHint, 2.5); return; }   // 睡熟时只有敲能叫醒
            Pet.NotifyInteraction();
            Pet.ResetProactiveTimer();   // 互动后重新开始主动说话倒计时

            // 砸：hit1~hit4 随机挑一个播一遍（每个约 0.82s），无物理效果
            string[] hitGifs = { "Interact/hit1.gif", "Interact/hit2.gif", "Interact/hit3.gif", "Interact/hit4.gif" };
            var rand = new Random();
            Pet.PlayActionSequence((hitGifs[rand.Next(hitGifs.Length)], 1.0, false));

            // 记录互动状态：下一条消息会带上 (用户砸了你一下)
            _pendingInteraction = "(用户砸了你一下)";
        }

        private async void Menu_ActionBonk_Click(object sender, RoutedEventArgs e)
        {
            // 敲能把睡着的她叫醒（任何睡眠阶段都生效）
            Pet.WakeUp();
            Pet.ResetProactiveTimer();   // 互动后重新开始主动说话倒计时

            // 敲：bonk 播一遍（约 0.58s）
            Pet.PlayActionSequence(("Interact/bonk.gif", 0.8, false));

            // 物理开启时，等锤子落到一半（约 0.3s）再把鱼打飞，
            // 避免“锤子还没碰到鱼就飞起来”的不合理观感
            if (Physics != null && Physics.EnablePhysics)
            {
                await Task.Delay(BonkHitDelayMs);
                if (Pet.IsPlayingAction)   // 动画还在播（没被中断）才打飞
                {
                    var rand = new Random();
                    double dir = rand.NextDouble() < 0.5 ? -1 : 1;
                    double vx = dir * (500 + rand.NextDouble() * 400);
                    double vy = -(700 + rand.NextDouble() * 500);
                    Physics.Throw(vx, vy);
                }
            }

            // 记录互动状态：下一条消息会带上 (用户敲了你一下)
            _pendingInteraction = "(用户敲了你一下)";
        }

        private void Menu_ActionGreet_Click(object sender, RoutedEventArgs e)
        {
            if (Pet.IsAsleep) { ShowBubble(SleepHint, 2.5); return; }   // 睡熟时只有敲能叫醒
            Pet.NotifyInteraction();
            Pet.ResetProactiveTimer();   // 互动后重新开始主动说话倒计时

            // 打招呼：hello2 循环播放 4 秒，并弹出 hello 气泡
            Pet.PlayActionSequence(("Interact/hello2.gif", 4.0, true));
            ShowBubble("hello", 4);
        }

        private void Menu_ActionJail_Click(object sender, RoutedEventArgs e)
        {
            if (Pet.IsAsleep) { ShowBubble(SleepHint, 2.5); return; }   // 睡熟时只有敲能叫醒
            Pet.NotifyInteraction();
            Pet.ResetProactiveTimer();   // 互动后重新开始主动说话倒计时

            // 蹲大牢：jail1 播 3 秒 → jail2 播 3 秒，连起来
            Pet.PlayActionSequence(
                ("Interact/jail1.gif", 3.0, true),
                ("Interact/jail2.gif", 3.0, true));

            // 记录互动状态：下一条消息会带上 (用户把你关进牢房了一会)
            _pendingInteraction = "(用户把你关进牢房了一会)";
        }

        // ===== 底部状态小字：余额 · 峰谷期 =====

        /// <summary>
        /// 当前是否谷期（空闲时段）。DeepSeek 峰谷计费规则：
        /// 工作日高峰 9:00-12:00、14:00-18:00，其余为空闲时段（闲时价为峰时价的一半）；
        /// 周末（周六/周日）全天不再区分峰谷，统一按低谷价计费。
        /// </summary>
        private static bool IsOffPeakNow()
        {
            var now = DateTime.Now;
            if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return true;

            int hour = now.Hour;
            bool peak = (hour >= 9 && hour < 12) || (hour >= 14 && hour < 18);
            return !peak;
        }

        /// <summary>刷新底部小字：💰 余额 · 峰谷</summary>
        private void UpdateStatusLine()
        {
            if (StatusLineText == null) return;

            bool offPeak = IsOffPeakNow();
            string weekend = DateTime.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "周末" : "";
            string peakText = offPeak ? $"⚡ 谷期{weekend}" : "🔥 峰期";
            string money = string.IsNullOrEmpty(_balanceShort) ? "--" : _balanceShort;
            StatusLineText.Text = $"💰 {money} · {peakText}";
            StatusLineText.ToolTip = offPeak
                ? "当前为闲时（谷期），API 价格为高峰时段的一半"
                : "当前为高峰时段，API 价格较高";
        }

        /// <summary>初始化底部小字：立即刷新一次，随后定时刷新峰谷与余额</summary>
        private void InitStatusLine()
        {
            UpdateStatusLine();

            _statusLineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _statusLineTimer.Tick += (s, e) => UpdateStatusLine();
            _statusLineTimer.Start();

            _balanceTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _balanceTimer.Tick += async (s, e) => await RefreshBalanceAsync(false);
            _balanceTimer.Start();

            // 启动 3 秒后再查一次余额，避开启动动画/欢迎语
            var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            delay.Tick += async (s, e) =>
            {
                delay.Stop();
                await RefreshBalanceAsync(false);
            };
            delay.Start();
        }

        /// <summary>拉取余额：更新底部小字；showBubble=true 时同时在气泡里显示详情</summary>
        private async Task RefreshBalanceAsync(bool showBubble)
        {
            if (_aiService == null) return;

            var result = await _aiService.GetBalanceAsync();
            _balanceShort = result.Short;
            UpdateStatusLine();

            if (showBubble)
                ShowBubble(result.Full, result.Ok ? 8 : 6, force: true);
        }

        /// <summary>右键菜单：查询 DeepSeek 账号余额，结果显示在气泡里（工作状态下临时顶开，随后自动恢复）</summary>
        private async void Menu_Balance_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null || _balanceBusy) return;

            _balanceBusy = true;
            try
            {
                ShowBubble("💰 正在查询余额…", 15, force: true);
                await RefreshBalanceAsync(true);
            }
            finally
            {
                _balanceBusy = false;
            }
        }

        private void Menu_Sudoku_Click(object sender, RoutedEventArgs e)
        {
            // 数独期间只暂停「自发对话」和「睡觉」，避免打断游戏；
            // 发呆动作、漫游、情绪表情照常运行，桌宠仍可交互、不被锁住。
            if (_sudokuWindow == null)
            {
                _sudokuWindow = new SudokuWindow(this);
                _sudokuWindow.Closed += (s, ev) => { _sudokuWindow = null!; Pet.SetPlayerBusy(false); };
            }
            Pet.SetPlayerBusy(true);
            _sudokuWindow.Show();
            _sudokuWindow.Activate();
        }

        private void Menu_Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(this);
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void Menu_PhysicsToggle_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem != null && Physics != null)
            {
                Physics.EnablePhysics = menuItem.IsChecked;
                if (Physics.EnablePhysics)
                {
                    ShowBubble("🎯 物理效果已开启", 2);
                    Physics.StartFalling();
                }
                else
                {
                    ShowBubble("🎯 物理效果已关闭", 2);
                    Physics.Reset();
                }
            }
        }

        public void ReloadConfig(string apiKey, string systemPrompt)
        {
            try
            {
                _aiService?.Dispose();   // 释放旧的 HttpClient，避免泄漏
                _aiService = new DeepSeekService(apiKey, systemPrompt);
                _aiService.SetMaxReplyLength(_enableMaxReplyLength, _maxReplyLength);
                _aiService.SetThinking(_enableThinking);
                ShowBubble("✅ 设置已生效！", 2);
            }
            catch (Exception ex)
            {
                ShowBubble($"❌ 配置生效失败：{ex.Message}", 3);
            }
        }

        /// <summary>应用宠物缩放：窗口尺寸和整个内容一起按 scale 缩放</summary>
        public void ApplyPetScale(double scale)
        {
            scale = Math.Clamp(scale, 0.5, 2.0);
            PetScale = scale;

            Width = BaseWindowWidth * scale;
            Height = BaseWindowHeight * scale;

            // 用 LayoutTransform 而不是 RenderTransform：
            // RenderTransform 不改变布局尺寸，窗口缩小到内容(260×400)以下时，
            // 内容会在自身布局空间里先被窗口裁剪（GIF 被裁掉），LayoutTransform
            // 会让内容的有效布局尺寸同步变成 260×scale，窗口永远不会小于内容，不裁剪。
            if (RootGrid != null)
                RootGrid.LayoutTransform = new ScaleTransform(scale, scale);

            Physics?.RefreshBounds();   // 更新物理边界（含天花板偏移）
        }

        /// <summary>设置随机动作触发间隔（秒，最低 10）</summary>
        public void SetIdleInterval(int seconds)
        {
            Pet?.SetIdleInterval(seconds);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_exiting)
            {
                // 关闭窗口（Alt+F4 等）→ 收纳到托盘，不退出程序
                e.Cancel = true;
                HideToTray();
                return;
            }

            // 真正退出：释放物理订阅、宠物定时器、HTTP 客户端、托盘图标
            Physics?.Unsubscribe();
            Pet?.Stop();
            _dshCompanion?.Dispose();
            _aiService?.Dispose();
            _trayIcon?.Dispose();
        }

        private void Menu_Exit_Click(object sender, RoutedEventArgs e)
        {
            ExitApp();
        }

        // ========== 系统托盘（任务栏隐藏后的入口） ==========

        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private bool _exiting;   // 真正退出标志：托盘“退出”时置 true，避免被收纳逻辑拦截

        // 托盘单击防抖：双击会触发两次 MouseClick，用延时判断避免“显示+立即隐藏”
        private readonly DispatcherTimer _trayClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        /// <summary>创建托盘图标：左键单击显示/隐藏，右键菜单可显示/退出</summary>
        private void CreateTrayIcon()
        {
            // 用自定义鱼图标（从 ico 加载），加载失败回退系统图标
            string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "ico", "dafeiyv.ico");
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = IconLoader.LoadFromIco(icoPath, 32),
                Text = "大肥鱼桌宠",
                Visible = true
            };

            // 单击/双击都切换显示，但用防抖：双击不会变成“显示+立即隐藏”
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button != System.Windows.Forms.MouseButtons.Left) return;
                _trayClickTimer.Stop();
                _trayClickTimer.Start();   // 250ms 内没有第二次点击才切换
            };
            _trayIcon.MouseDoubleClick += (s, e) =>
            {
                _trayClickTimer.Stop();    // 双击：取消待定的单击，只切换一次
                ToggleWindow();
            };
            _trayClickTimer.Tick += (s, e) =>
            {
                _trayClickTimer.Stop();
                ToggleWindow();
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("🐟 显示/隐藏桌宠", null, (s, e) => ToggleWindow());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("❌ 退出", null, (s, e) => ExitApp());
            _trayIcon.ContextMenuStrip = menu;
        }

        private void ToggleWindow()
        {
            if (IsVisible)
                HideToTray();
            else
                ShowFromTray();
        }

        private void HideToTray()
        {
            Pet.SetActive(false);          // 暂停全部行为计时器
            Pet.StopAnimation();           // 停止 GIF 动画并释放解码帧（内存大头）
            Physics?.Stop();               // 停止物理（若正在飞）
            Hide();

            // 主动回收：把刚释放的解码内存真正归还系统（仅隐藏时做一次，平时不调用）
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void ShowFromTray()
        {
            Show();
            Pet.SetActive(true);           // 恢复全部行为计时器
            Pet.RestoreAnimation();        // 重新加载动画（待机）
        }

        private void ExitApp()
        {
            _exiting = true;
            Application.Current.Shutdown();
        }

        public void ClearHistory()
        {
            conversationHistory.Clear();
            ShowBubble("🧹 记忆已清空", 2);
        }
    }
}
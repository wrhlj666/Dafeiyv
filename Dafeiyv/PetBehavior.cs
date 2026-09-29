using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyv
{
    public class PetBehavior
    {
        private readonly MainWindow _window;
        private readonly Dispatcher _dispatcher;

        private readonly (string path, bool isLooping, double duration)[] idleActions = {
            ("Idle/Popcat.gif", true, 5.0),
            ("Idle/watermelon.gif", true, 4.0),
            ("Idle/raid.gif", true, 4.0),
            ("Idle/lick.gif", true, 4.0),
            ("Idle/sing.gif", true, 4.0),
            ("Idle/hypnosis.gif", true, 4.0),
            ("Idle/play_games.gif", true, 4.0),
            ("Idle/fan.gif", true, 4.0),
            ("Idle/drink.gif", true, 4.0),
            ("Idle/red_envelope.gif", true, 4.0),
            ("Idle/drawing.gif", true, 4.0),
            ("Idle/guitar.gif", true, 4.0),
            ("Idle/cheer_up.gif", true, 4.0),
            ("Idle/driving.gif", true, 4.0),
            ("Idle/megaphone.gif", true, 4.0),
            ("Idle/trash_can.gif", true, 4.0),
            ("Idle/gift_2.gif", true, 4.0),
            ("Idle/magic.gif", true, 4.0),
            ("Idle/spray.gif", true, 4.0),
            ("Idle/money.gif", true, 4.0),
            ("Idle/gun.gif", true, 4.0),
            ("Idle/burn.gif", true, 4.0),
            ("Idle/swipe_card.gif", true, 4.0),
            ("Idle/licking.gif", true, 4.0),
            ("Idle/dance1.gif", true, 4.0),
            ("Idle/dance2.gif", true, 4.0),
            ("Idle/dance3.gif", true, 4.0),
            ("Idle/dance4.gif", true, 4.0),
            ("Idle/dance5.gif", true, 4.0),
            ("Idle/shake_cola.gif", true, 4.0),
            ("Idle/shake_bell.gif", true, 4.0),
            ("Idle/all_good.gif", true, 4.0),
            ("Idle/glow_stick.gif", true, 4.0),
            ("Idle/love_letter.gif", false, 0),
            ("Idle/donut.gif", false, 0),
            ("Idle/idle_2.gif", false, 0),
            ("Idle/idle_3.gif", false, 0),
            ("Idle/tape.gif", false, 0),
            ("Idle/gift_1.gif", false, 0),
            ("Idle/bubbles.gif", false, 0),
            ("Idle/snapshot.gif", false, 0),
            ("Idle/wink.gif", false, 0),
            ("Idle/beg_rice.gif", false, 0),
            ("Idle/joker.gif", false, 0),
            ("Idle/slipper.gif", false, 0),
            ("Idle/stop_work.gif", false, 0),
            ("Idle/rose.gif", false, 0),
            // 厕所组合动作（toilet1 播完会自动接 toilet2，各 4s，见 IdleActionTimer_Tick）
            ("Interact/toilet1.gif", true, 4.0),
        };

        // ---- 睡眠系统 ----
        public enum SleepStage { Awake, Sleep1, Sleep2, Sleep3 }
        private const int AwakeToSleep1Seconds = 120;      // 多久没互动开始犯困
        private const int SleepStageIntervalSeconds = 30;  // sleep1→sleep2、sleep2→sleep3 间隔
        private const int Sleep3AutoWakeSeconds = 60;      // 睡多久自动醒

        private SleepStage _sleepStage = SleepStage.Awake;
        private readonly DispatcherTimer sleepTimer;
        private readonly DispatcherTimer proactiveTimer;   // 主动说话计时器
        private readonly DispatcherTimer roamTimer;        // 漫游计时器

        /// <summary>主动说话计时器到点（由 MainWindow 订阅并触发 AI 发言）</summary>
        public event Action? ProactiveSpeakRequested;
        /// <summary>漫游计时器到点（由 MainWindow 订阅并触发一次限时物理蹦跳）</summary>
        public event Action? RoamRequested;

        private const double EmotionTimeoutSeconds = 4.5;   // 情绪展示时长（秒）
        public bool IsPlayingAction { get; set; } = false;
        /// <summary>是否正在播放互动动画序列（摸头/砸/敲/打招呼/蹲大牢）</summary>
        public bool IsActionSequenceActive => _actionSequenceActive;
        private volatile bool _stopped;   // 窗口关闭后不再触碰 UI
        private bool _actionSequenceActive;   // 互动动作序列播放中（仅序列本身/抓取打断，情绪切换不打断）
        private bool _proactiveEnabled = true;   // 主动说话开关
        private bool _roamEnabled = true;        // 漫游开关
        private readonly Random random = new Random();
        private readonly DispatcherTimer emotionTimeoutTimer;
        private DispatcherTimer idleActionTimer;

        // ============ DSH 工作状态模式 ============
        /// <summary>DSH 工作模式：期间锁气泡、锁小动作、锁睡觉定时器</summary>
        public enum WorkMode { None, Thinking, Working, Waiting }

        private WorkMode _workMode = WorkMode.None;
        private readonly DispatcherTimer workRotateTimer;
        private string _currentWorkGif = "";

        // working1/2/3 + review1/2 长时间循环、随机替换；working4（打瞌睡）偶尔插播几秒
        private static readonly string[] WorkLongPool =
        {
            "Images/working1.gif",
            "Images/working2.gif",
            "Images/working3.gif",
            "Images/review1.gif",
            "Images/review2.gif",
        };
        private const string WorkDozingGif = "Images/working4.gif";
        private const string WorkThinkingGif = "Images/thinking.gif";
        private const string WorkWaitingGif = "Images/waiting.gif";

        /// <summary>是否处于 DSH 工作模式（MainWindow 据此锁气泡）</summary>
        public bool IsWorkMode => _workMode != WorkMode.None;

        public PetBehavior(MainWindow window)
        {
            _window = window;
            _dispatcher = window.Dispatcher;

            emotionTimeoutTimer = new DispatcherTimer();
            emotionTimeoutTimer.Interval = TimeSpan.FromSeconds(3);
            emotionTimeoutTimer.Tick += EmotionTimeoutTimer_Tick;

            idleActionTimer = new DispatcherTimer();
            idleActionTimer.Interval = TimeSpan.FromSeconds(10);
            idleActionTimer.Tick += IdleActionTimer_Tick;
            idleActionTimer.Start();

            sleepTimer = new DispatcherTimer();
            sleepTimer.Interval = TimeSpan.FromSeconds(AwakeToSleep1Seconds);
            sleepTimer.Tick += SleepTimer_Tick;
            sleepTimer.Start();

            proactiveTimer = new DispatcherTimer();
            proactiveTimer.Interval = TimeSpan.FromMinutes(5);
            proactiveTimer.Tick += ProactiveTimer_Tick;
            proactiveTimer.Start();

            roamTimer = new DispatcherTimer();
            roamTimer.Interval = TimeSpan.FromSeconds(30);
            roamTimer.Tick += RoamTimer_Tick;
            roamTimer.Start();

            workRotateTimer = new DispatcherTimer();
            workRotateTimer.Interval = TimeSpan.FromSeconds(9);
            workRotateTimer.Tick += WorkRotateTimer_Tick;
        }

        // ============ DSH 工作状态模式 ============

        /// <summary>进入 DSH 工作模式：锁小动作、锁睡觉、循环播放对应动画直到状态改变</summary>
        public void EnterWorkMode(WorkMode mode)
        {
            if (_stopped) return;

            // 打断正在播的随机动作/互动动作，工作状态优先
            _actionSequenceActive = false;
            IsPlayingAction = false;

            // 睡觉锁：强制清醒，绝不在工作中睡着
            _sleepStage = SleepStage.Awake;

            // 小动作锁 + 睡觉锁 + 打断主动说话，避免抢动画/抢气泡
            idleActionTimer.Stop();
            sleepTimer.Stop();
            proactiveTimer.Stop();
            emotionTimeoutTimer.Stop();

            _workMode = mode;
            SetRepeatForever();

            switch (mode)
            {
                case WorkMode.Thinking:
                    workRotateTimer.Stop();
                    _currentWorkGif = WorkThinkingGif;
                    _window.CurrentGifPath = WorkThinkingGif;
                    break;

                case WorkMode.Waiting:
                    workRotateTimer.Stop();
                    _currentWorkGif = WorkWaitingGif;
                    _window.CurrentGifPath = WorkWaitingGif;
                    break;

                default:   // Working：长时间池循环 + 随机替换
                    _currentWorkGif = PickWorkGif("");
                    _window.CurrentGifPath = _currentWorkGif;
                    StartWorkRotation();
                    break;
            }
        }

        /// <summary>退出工作模式：解锁定时器并回到待机</summary>
        public void ExitWorkMode()
        {
            if (_workMode == WorkMode.None) return;
            _workMode = WorkMode.None;
            workRotateTimer.Stop();
            SetRepeatForever();
            ResetToStandby();

            if (_stopped) return;
            sleepTimer.Stop();
            sleepTimer.Interval = TimeSpan.FromSeconds(AwakeToSleep1Seconds);
            sleepTimer.Start();
            idleActionTimer.Start();
            if (_proactiveEnabled) proactiveTimer.Start();
        }

        private void StartWorkRotation()
        {
            workRotateTimer.Stop();
            workRotateTimer.Interval = TimeSpan.FromSeconds(8 + random.NextDouble() * 5);
            workRotateTimer.Start();
        }

        private async void WorkRotateTimer_Tick(object? sender, EventArgs e)
        {
            workRotateTimer.Stop();
            if (_workMode != WorkMode.Working || _stopped) return;

            // 偶尔（约 15%）插播一次打瞌睡，几秒后继续干活
            if (random.NextDouble() < 0.15)
            {
                SetRepeatOnce();
                _window.CurrentGifPath = WorkDozingGif;
                await Task.Delay(3500);
                if (_workMode != WorkMode.Working || _stopped) return;
                SetRepeatForever();
            }

            _currentWorkGif = PickWorkGif(_currentWorkGif);
            _window.CurrentGifPath = _currentWorkGif;
            StartWorkRotation();
        }

        /// <summary>从长时间播放池里随机挑一个（尽量不与当前重复）</summary>
        private string PickWorkGif(string current)
        {
            if (WorkLongPool.Length == 0) return "Images/Standby_fan.gif";
            string pick = current;
            for (int i = 0; i < 8 && pick == current; i++)
                pick = WorkLongPool[random.Next(WorkLongPool.Length)];
            return pick;
        }

        private void SetRepeatForever()
        {
            try
            {
                _dispatcher.Invoke(() =>
                {
                    WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, RepeatBehavior.Forever);
                });
            }
            catch { }
        }

        private void SetRepeatOnce()
        {
            try
            {
                _dispatcher.Invoke(() =>
                {
                    WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(
                        _window.PetImage, new System.Windows.Media.Animation.RepeatBehavior(1));
                });
            }
            catch { }
        }

        private void EmotionTimeoutTimer_Tick(object sender, EventArgs e)
        {
            emotionTimeoutTimer.Stop();
            SetEmotion("平静");
        }

        public void SetEmotion(string emotion)
        {
            if (IsWorkMode) return;   // 工作模式下动画被锁定，不接受情绪切换

            if (IsPlayingAction)
            {
                IsPlayingAction = false;
            }

            string gifPath = "Images/Standby_fan.gif";
            bool isThinking = false;

            switch (emotion)
            {
                case "思考中":
                    gifPath = "Images/thinking.gif";
                    isThinking = true;
                    break;
                case "指着用户大笑":
                    gifPath = "Images/laugh_point.gif";
                    break;
                case "开心":
                    gifPath = "Images/Happy.gif";
                    break;
                case "难过":
                    gifPath = "Images/sad.gif";
                    break;
                case "大哭":
                    gifPath = "Images/crying.gif";
                    break;
                case "比心":
                    gifPath = "Images/love.gif";
                    break;
                case "吃爆米花":
                    gifPath = "Images/popcorn.gif";
                    break;
                case "紧张":
                    gifPath = "Images/sweat.gif";
                    break;
                case "慌乱":
                    gifPath = "Images/panic.gif";
                    break;
                case "晕":
                    gifPath = "Images/spinning.gif";
                    break;
                case "赞同":
                    gifPath = "Images/thumbs_up.gif";
                    break;
                case "反对":
                    gifPath = "Images/thumbs_down.gif";
                    break;
                case "期待":
                    gifPath = "Images/expect.gif";
                    break;
                case "平静":
                    gifPath = "Images/Standby_fan.gif";
                    break;
                case "害怕":
                    gifPath = "Images/scared.gif";
                    break;
                case "偷看":
                    gifPath = "Images/peek.gif";
                    break;
                case "害羞":
                    gifPath = "Images/shy.gif";
                    break;
                case "生气":
                    gifPath = "Images/angry.gif";
                    break;
                default:
                    gifPath = "Images/Standby_fan.gif";
                    break;
            }

            // 情绪动画都是循环播放的：先重置 RepeatBehavior，避免被残留的“只播一次”卡住
            try
            {
                _dispatcher.Invoke(() =>
                {
                    WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, RepeatBehavior.Forever);
                });
            }
            catch { }

            _window.CurrentGifPath = gifPath;

            if (!isThinking)
            {
                emotionTimeoutTimer.Stop();
                emotionTimeoutTimer.Interval = TimeSpan.FromSeconds(EmotionTimeoutSeconds);
                emotionTimeoutTimer.Start();
            }
            else
            {
                emotionTimeoutTimer.Stop();
            }
        }

        private async void IdleActionTimer_Tick(object sender, EventArgs e)
        {
            idleActionTimer.Stop();

            if (IsWorkMode) return;   // 工作模式锁小动作（定时器已停，防御性判断）

            if (IsPlayingAction || _actionSequenceActive)
            {
                idleActionTimer.Start();
                return;
            }
            // 睡觉/犯困时不播随机动作
            if (IsSleeping)
            {
                idleActionTimer.Start();
                return;
            }
            // 物理打飞（砸）后鱼还在飞/弹时，不播随机动画，等它停下
            if (_window.Physics != null && _window.Physics.IsFlying)
            {
                idleActionTimer.Start();
                return;
            }
            if (_window.InputPanel.Visibility == Visibility.Visible)
            {
                idleActionTimer.Start();
                return;
            }
            if (_window.BubbleBorder.Visibility == Visibility.Visible)
            {
                idleActionTimer.Start();
                return;
            }
            if (_window.CurrentGifPath.Contains("thinking"))
            {
                idleActionTimer.Start();
                return;
            }

            var action = idleActions[random.Next(idleActions.Length)];
            string actionGif = action.path;
            bool isLooping = action.isLooping;
            double duration = action.duration;

            IsPlayingAction = true;
            _window.CurrentGifPath = actionGif;

            if (!isLooping)
            {
                try
                {
                    await Task.Delay(50);
                    if (_stopped) return;
                    // 动作被中断（情绪/睡眠/抓取接管）时不要再设“只播一次”
                    if (IsPlayingAction && !IsSleeping)
                    {
                        _dispatcher.Invoke(() =>
                        {
                            try
                            {
                                WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, new System.Windows.Media.Animation.RepeatBehavior(1));
                            }
                            catch { }
                        });
                    }
                }
                catch { }
            }

            double waitSeconds = isLooping ? duration : 2.5;
            await Task.Delay((int)(waitSeconds * 1000));
            if (_stopped) return;

            // 厕所组合动作：toilet1 播完紧接着播 toilet2（各 4s）
            if (actionGif == "Interact/toilet1.gif")
            {
                if (!IsPlayingAction)
                {
                    idleActionTimer.Start();
                    return;
                }
                _window.CurrentGifPath = "Interact/toilet2.gif";
                await Task.Delay(50);
                if (_stopped) return;
                _dispatcher.Invoke(() =>
                {
                    try
                    {
                        WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, RepeatBehavior.Forever);
                    }
                    catch { }
                });
                await Task.Delay(4000);
                if (_stopped) return;
            }

            _dispatcher.Invoke(() =>
            {
                if (IsPlayingAction && !IsSleeping)
                {
                    ResetToStandby();
                }
                else if (!_stopped)
                {
                    // 动作被情绪等打断：把 RepeatBehavior 恢复为循环，避免 GIF 只播一次卡住
                    try
                    {
                        WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, RepeatBehavior.Forever);
                    }
                    catch { }
                }
                idleActionTimer.Start();
            });
        }

        public void ResetToStandby()
        {
            // ✅ 先重置 RepeatBehavior，再切换 GIF
            try
            {
                _dispatcher.Invoke(() =>
                {
                    WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, System.Windows.Media.Animation.RepeatBehavior.Forever);
                });
            }
            catch { }

            _window.CurrentGifPath = "Images/Standby_fan.gif";
            IsPlayingAction = false;
        }

        public void InterruptAction()
        {
            _actionSequenceActive = false;
            IsPlayingAction = false;
            ResetToStandby();
        }

        // ============ 睡眠系统 ============

        /// <summary>是否处于睡眠相关阶段（sleep1/sleep2/sleep3）</summary>
        public bool IsSleeping => _sleepStage != SleepStage.Awake;

        /// <summary>是否睡熟（sleep3，此时不能聊天）</summary>
        public bool IsAsleep => _sleepStage == SleepStage.Sleep3;

        /// <summary>任何互动都调用：打断犯困（sleep1/sleep2 直接叫醒），睡熟（sleep3）不响应</summary>
        public void NotifyInteraction()
        {
            if (_stopped) return;
            if (_sleepStage == SleepStage.Sleep3) return;   // 睡熟了，只有敲/时间能叫醒
            WakeUp();
        }

        /// <summary>叫醒（任何阶段都生效），并重新开始 120s 无互动计时</summary>
        public void WakeUp()
        {
            if (_sleepStage != SleepStage.Awake)
            {
                _sleepStage = SleepStage.Awake;
                if (!IsPlayingAction && !_actionSequenceActive)
                {
                    ResetToStandby();
                }
            }
            sleepTimer.Stop();
            sleepTimer.Interval = TimeSpan.FromSeconds(AwakeToSleep1Seconds);
            if (!IsWorkMode) sleepTimer.Start();   // 工作模式下睡觉计时保持锁定
        }

        /// <summary>重新开始主动说话倒计时（从当前设置的间隔重新计时）</summary>
        public void ResetProactiveTimer()
        {
            if (!_proactiveEnabled) return;   // 开关关闭时不启动
            proactiveTimer.Stop();
            proactiveTimer.Start();
        }

        private void SleepTimer_Tick(object? sender, EventArgs e)
        {
            switch (_sleepStage)
            {
                case SleepStage.Awake:
                    EnterSleepStage(SleepStage.Sleep1);
                    break;
                case SleepStage.Sleep1:
                    EnterSleepStage(SleepStage.Sleep2);
                    break;
                case SleepStage.Sleep2:
                    EnterSleepStage(SleepStage.Sleep3);
                    break;
                case SleepStage.Sleep3:
                    WakeUp();   // 睡够时间自动醒
                    break;
            }
        }

        private void EnterSleepStage(SleepStage stage)
        {
            // 先打断正在播放的随机动画/互动动画，避免它们稍后的收尾逻辑把睡眠 GIF 顶掉
            if (IsPlayingAction || _actionSequenceActive)
            {
                InterruptAction();
            }

            _sleepStage = stage;
            emotionTimeoutTimer.Stop();

            string gif = stage switch
            {
                SleepStage.Sleep1 => "Interact/sleep1.gif",
                SleepStage.Sleep2 => "Interact/sleep2.gif",
                _ => "Interact/sleep3.gif"
            };
            _window.CurrentGifPath = gif;

            // 睡眠动画循环播放
            try
            {
                _dispatcher.Invoke(() =>
                {
                    WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, RepeatBehavior.Forever);
                });
            }
            catch { }

            sleepTimer.Stop();
            sleepTimer.Interval = stage == SleepStage.Sleep3
                ? TimeSpan.FromSeconds(Sleep3AutoWakeSeconds)
                : TimeSpan.FromSeconds(SleepStageIntervalSeconds);
            sleepTimer.Start();
        }

        /// <summary>窗口关闭时调用：停止所有定时器与后台任务</summary>
        public void Stop()
        {
            _stopped = true;
            emotionTimeoutTimer.Stop();
            idleActionTimer.Stop();
            sleepTimer.Stop();
            proactiveTimer.Stop();
            roamTimer.Stop();
        }

        /// <summary>设置随机动作触发间隔（秒，最低 10）</summary>
        public void SetIdleInterval(int seconds)
        {
            int clamped = Math.Max(10, seconds);
            idleActionTimer.Interval = TimeSpan.FromSeconds(clamped);
        }

        /// <summary>设置漫游间隔（秒，最低 1）</summary>
        public void SetRoamInterval(int seconds)
        {
            int clamped = Math.Max(1, seconds);
            roamTimer.Interval = TimeSpan.FromSeconds(clamped);
        }

        /// <summary>开启/关闭漫游</summary>
        public void SetRoamEnabled(bool enabled)
        {
            _roamEnabled = enabled;
            if (enabled)
                roamTimer.Start();
            else
                roamTimer.Stop();
        }

        /// <summary>窗口隐藏到托盘时暂停全部行为计时器（避免后台空耗），显示时恢复</summary>
        public void SetActive(bool active)
        {
            if (active)
            {
                if (_roamEnabled) roamTimer.Start();
                // 工作模式：自发对话/睡觉/小动作保持锁定，只恢复漫游
                if (IsWorkMode) return;
                if (_proactiveEnabled) proactiveTimer.Start();
                sleepTimer.Start();
                idleActionTimer.Start();
            }
            else
            {
                proactiveTimer.Stop();
                roamTimer.Stop();
                sleepTimer.Stop();
                idleActionTimer.Stop();
            }
        }

        /// <summary>
        /// 玩家在玩数独等内嵌小游戏、但桌宠仍停留在桌面可交互时调用。
        /// 只暂停「自发对话」和「睡觉」两个会影响游戏的计时器；
        /// 发呆动作、漫游、情绪表情仍保持运行，让桌宠不显得"卡住"。
        /// </summary>
        public void SetPlayerBusy(bool busy)
        {
            if (busy)
            {
                proactiveTimer.Stop();
                sleepTimer.Stop();
            }
            else
            {
                sleepTimer.Start();
                if (_proactiveEnabled) proactiveTimer.Start();
            }
        }

        /// <summary>停止 GIF 动画并释放解码帧（隐藏到托盘时调用，可显著降低内存）</summary>
        public void StopAnimation()
        {
            // 通过绑定把 AnimatedSource 置空：停止动画、释放解码帧。
            // 不能用 SetAnimatedSource 直接设值——那会覆盖 XAML 绑定，导致之后换 GIF 全失效。
            _window.CurrentGifPath = null!;
        }

        /// <summary>重新显示时恢复动画（工作模式下恢复到当前工作动画）</summary>
        public void RestoreAnimation()
        {
            // 值从 null 变回路径，绑定会重新推送并加载 GIF（绑定始终有效）
            _window.CurrentGifPath = IsWorkMode && !string.IsNullOrEmpty(_currentWorkGif)
                ? _currentWorkGif
                : "Images/Standby_fan.gif";
        }

        /// <summary>
        /// 漫游计时器：与睡觉、互动动画、输入框、对话、拖拽、飞行互斥——
        /// 这些情况下跳过本轮，等下一次到点。
        /// </summary>
        private void RoamTimer_Tick(object? sender, EventArgs e)
        {
            roamTimer.Stop();

            if (IsSleeping ||
                _actionSequenceActive ||
                _window.InputPanel.Visibility == Visibility.Visible ||
                _window.IsSending ||
                _window.IsMouseGrabbed ||
                (_window.Physics != null && _window.Physics.IsFlying))
            {
                roamTimer.Start();
                return;
            }

            RoamRequested?.Invoke();
            roamTimer.Start();
        }

        /// <summary>设置主动说话间隔（秒，最低 10）</summary>
        public void SetProactiveInterval(int seconds)
        {
            int clamped = Math.Max(10, seconds);
            proactiveTimer.Interval = TimeSpan.FromSeconds(clamped);
        }

        /// <summary>开启/关闭主动说话</summary>
        public void SetProactiveEnabled(bool enabled)
        {
            _proactiveEnabled = enabled;
            if (enabled)
                proactiveTimer.Start();
            else
                proactiveTimer.Stop();
        }

        /// <summary>
        /// 主动说话计时器：与睡觉、互动动画、输入框、对话中互斥——
        /// 这些情况下跳过本轮，不打断用户。物理飞行中照样可以说话。
        /// </summary>
        private void ProactiveTimer_Tick(object? sender, EventArgs e)
        {
            proactiveTimer.Stop();

            if (IsSleeping ||                              // 睡觉/犯困时不说
                _actionSequenceActive ||                    // 互动动画播放中不插话
                _window.InputPanel.Visibility == Visibility.Visible ||   // 用户正在输入
                _window.IsSending)                          // 正在对话/思考中
            {
                proactiveTimer.Start();
                return;
            }

            ProactiveSpeakRequested?.Invoke();
            proactiveTimer.Start();
        }

        /// <summary>
        /// 按顺序播放一组 GIF（互动动作 / 开场动画）。
        /// loop=true：在 seconds 内循环播放；loop=false：只播放一遍（seconds 应 ≥ GIF 一个循环时长）。
        /// 播放期间会中断其它动作，结束后回到待机。
        /// </summary>
        public async void PlayActionSequence(params (string path, double seconds, bool loop)[] steps)
        {
            if (steps == null || steps.Length == 0) return;
            if (IsPlayingAction) InterruptAction();
            IsPlayingAction = true;
            _actionSequenceActive = true;
            emotionTimeoutTimer.Stop();   // 互动期间情绪超时不要打断动作

            try
            {
                foreach (var step in steps)
                {
                    if (_stopped || !_actionSequenceActive) return;

                    _window.CurrentGifPath = step.path;
                    await Task.Delay(50);   // 等 WpfAnimatedGif 加载新源
                    if (_stopped || !_actionSequenceActive) return;

                    _dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var repeat = step.loop
                                ? RepeatBehavior.Forever
                                : new RepeatBehavior(1);
                            WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, repeat);
                        }
                        catch { }
                    });

                    await Task.Delay((int)(step.seconds * 1000));
                    if (_stopped || !_actionSequenceActive) return;
                }
            }
            finally
            {
                _actionSequenceActive = false;
                if (!_stopped)
                {
                    _dispatcher.Invoke(() =>
                    {
                        // 先恢复循环，避免最后一个非循环步骤把“只播一次”留在 GIF 上卡住
                        try
                        {
                            WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(_window.PetImage, RepeatBehavior.Forever);
                        }
                        catch { }
                        if (IsPlayingAction)
                        {
                            ResetToStandby();
                        }
                    });
                }
            }
        }

        public void PlayManualAction(string gifPath)
        {
            if (IsPlayingAction) return;
            IsPlayingAction = true;
            _window.CurrentGifPath = gifPath;

            Task.Run(async () =>
            {
                await Task.Delay(2500);
                if (_stopped) return;
                _dispatcher.Invoke(() =>
                {
                    if (IsPlayingAction)
                    {
                        ResetToStandby();
                    }
                });
            });
        }
    }
}
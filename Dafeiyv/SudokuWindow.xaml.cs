using System;
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
    public partial class SudokuWindow : Window
    {
        private readonly MainWindow _mainWindow = null!;   // 构造函数必传
        private readonly SudokuGame _game = new SudokuGame();
        private readonly Button[,] _cells = new Button[9, 9];
        private readonly Grid[] _boxRows = new Grid[3];    // 3 行宫格（用于联动宫格间隙）
        private readonly Border[,] _boxes = new Border[3, 3];  // 9 个宫格（带粗黑外框）
        private int _selectedRow = -1, _selectedCol = -1;
        private bool _aiBusy;
        private bool _loadingSettings;
        private bool _boardReady;   // 棋盘建好之前不响应难度切换（避免初始化时空引用）
        private bool _chatVisible;  // 聊天区是否展开
        private string? _lastUserText;   // 上一条主人原话（仅文本，不含棋盘）
        private string? _lastAiReply;    // 上一条 AI 回复

        // 展开聊天时窗口额外增加的高度（聊天区 120 + 输入行 31 + 间距）
        private const double ChatExpandDelta = 165;

        // 当前格子边长（随窗口缩放动态计算，保持方形）
        private double _cellSize = 26;

        // 数独专属 AI 提示词 = 用户自拟提示词(角色/说话风格) + 数独游戏规则。
        // 独立于桌宠的[情绪]/记忆/长度限制, 只复用用户设定的人格. 标点用半角英文字符。
        private readonly string _sudokuSystemPrompt;

        public SudokuWindow(MainWindow mainWindow)
        {
            InitializeComponent();   // 注意：XAML 里 SelectedIndex="1" 会触发 SelectionChanged，
                                     // 但此时棋盘还没建，靠 _boardReady 拦截
            _mainWindow = mainWindow;

            // 用户自拟提示词作为角色设定基座(去掉可能残留的[情绪]格式要求等桌宠专用句)。
            string userPrompt = _mainWindow?.CurrentUserPrompt?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(userPrompt))
                userPrompt = "你是一只可爱的桌宠, 像大肥鱼一样亲切, 傲娇, 又聪明懒散.";

            // 数独提示词 = 用户自拟提示词 + 数独核心规则。
            // 答案会随每次提问附带, 因此 AI 无需自己解题, 回复更快更省 token。
            // 明确禁止说出答案数字, 只能给位置/逻辑提示。
            _sudokuSystemPrompt =
                userPrompt + "\n" +
                "现在你正在陪主人玩数独. 你会看到当前棋盘 (0 表示空格) 以及这份棋盘的完整正确答案, " +
                "还有你们之间最近的一轮对话记录.\n" +
                "正确答案仅供你内部参考, 用来确保你的提示准确. 规则:\n" +
                "1. 求助时给出具体提示 (指出某一行/列/宫缺什么数字, 建议观察哪个位置), " +
                "但绝对不要直接把答案数字说出来, 只能引导主人自己推理;\n" +
                "2. 主人可能提到刚才聊过的内容, 你可以结合上一轮对话回应, 但不要凭空编造更多记忆;\n" +
                "3. 回复简短, 亲切, 拟人化.";

            BuildBoard();
            _boardReady = true;

            LoadSudokuSettings();
            _game.NewGame(GetDifficulty());
            RefreshBoard();
            UpdateWatermark();   // 输入框初始显示占位水印
        }

        // ---------- 棋盘 ----------

        private void BuildBoard()
        {
            for (int br = 0; br < 3; br++)
            {
                // 一行 3 个宫格；宫格之间留出粗分隔
                var boxRow = new Grid
                {
                    Margin = new Thickness(0, br == 0 ? 0 : 2, 0, br == 2 ? 0 : 2)
                };
                _boxRows[br] = boxRow;
                for (int bc = 0; bc < 3; bc++)
                {
                    // 内部网格（放 9 个按钮）+ 粗黑外框（九宫格边界始终清晰）
                    var boxGrid = new Grid();
                    var box = new Border
                    {
                        Margin = new Thickness(bc == 0 ? 0 : 2, 0, bc == 2 ? 0 : 2, 0),
                        BorderBrush = Brushes.Black,
                        BorderThickness = new Thickness(1.5),
                        Child = boxGrid
                    };
                    _boxes[br, bc] = box;
                    for (int i = 0; i < 3; i++)
                    {
                        boxGrid.RowDefinitions.Add(new RowDefinition());
                        boxGrid.ColumnDefinitions.Add(new ColumnDefinition());
                    }
                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            int gr = br * 3 + r, gc = bc * 3 + c;
                            var btn = new Button
                            {
                                Content = "",
                                Width = _cellSize,
                                Height = _cellSize,
                                FontSize = Math.Max(10, _cellSize * 0.5),
                                Margin = new Thickness(0.5),
                                Background = Brushes.White,
                                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                                BorderThickness = new Thickness(1),
                                Padding = new Thickness(0),
                                Cursor = Cursors.Hand
                            };
                            btn.Click += Cell_Click;
                            btn.Tag = gr * 9 + gc;   // 用 int 编码行列，避免装箱元组
                            Grid.SetRow(btn, r);
                            Grid.SetColumn(btn, c);
                            boxGrid.Children.Add(btn);
                            _cells[gr, gc] = btn;
                        }
                    }
                    boxRow.ColumnDefinitions.Add(new ColumnDefinition());
                    Grid.SetColumn(box, bc);
                    Grid.SetRow(box, 0);
                    boxRow.Children.Add(box);
                }
                BoardHost.RowDefinitions.Add(new RowDefinition());
                Grid.SetRow(boxRow, br);
                BoardHost.Children.Add(boxRow);
            }
        }

        /// <summary>窗口缩放时按可用空间重算格子尺寸（保持方形），并刷新字号</summary>
        private void BoardHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_boardReady || _cells[0, 0] == null) return;
            double availW = e.NewSize.Width;
            double availH = e.NewSize.Height;
            if (availW < 30 || availH < 30) return;

            // 总宽 ≈ 9×size + 8×0.03×size(格距) + 2×0.12×size(宫格间隙) = 9.48×size
            double size = Math.Min(availW / 9.48, availH / 9.48);
            size = Math.Max(14, Math.Min(64, size));   // 限制在 14~64px，避免过小/过大
            _cellSize = size;

            double fontSize = Math.Max(10, size * 0.5);
            double cellGap = Math.Max(0.5, size * 0.03);   // 单元格细间距也随格子缩放
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                {
                    if (_cells[r, c] == null) continue;
                    _cells[r, c].Width = size;
                    _cells[r, c].Height = size;
                    _cells[r, c].Margin = new Thickness(cellGap);
                    _cells[r, c].FontSize = fontSize;
                }

            // 宫格间隙随格子大小等比缩放（约 12% 格子边长），保证九宫格分隔始终明显
            double boxGap = Math.Clamp(size * 0.12, 2, 8);
            double boxLine = Math.Max(1, size * 0.05);   // 宫格外框细黑线粗细
            for (int br = 0; br < 3; br++)
            {
                if (_boxRows[br] != null)
                    _boxRows[br].Margin = new Thickness(0, br == 0 ? 0 : boxGap, 0, br == 2 ? 0 : boxGap);
                for (int bc = 0; bc < 3; bc++)
                {
                    if (_boxes[br, bc] != null)
                    {
                        _boxes[br, bc].Margin = new Thickness(bc == 0 ? 0 : boxGap, 0, bc == 2 ? 0 : boxGap, 0);
                        _boxes[br, bc].BorderThickness = new Thickness(boxLine);
                    }
                }
            }

            // 数字键盘与工具按钮随窗口联动
            double padHeight = Math.Max(22, size * 0.9);
            double padFont = Math.Max(10, size * 0.42);
            foreach (var child in NumPad.Children)
            {
                if (child is Button b)
                {
                    b.Height = padHeight;
                    b.FontSize = padFont;
                }
            }
            foreach (var child in ToolRow.Children)
            {
                if (child is Button b)
                {
                    b.Height = padHeight;
                    b.FontSize = padFont;
                }
            }
            RefreshBoard();   // 重新渲染（备注字号也随之更新）
        }

        private void RefreshBoard()
        {
            if (!_boardReady) return;

            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                {
                    var btn = _cells[r, c];
                    if (btn == null) continue;
                    int v = _game.Board[r, c];
                    btn.Background = (r == _selectedRow && c == _selectedCol) ? Brushes.LightYellow : Brushes.White;
                    btn.FontWeight = FontWeights.Normal;

                    if (v == 0)
                    {
                        // 空格：有备注就渲染铅笔小数字，否则空白
                        btn.Content = HasMarks(r, c) ? BuildMarkGrid(r, c) : "";
                        btn.Foreground = Brushes.Gray;
                    }
                    else if (_game.Given[r, c])
                    {
                        // 自带数字：黑色加粗，与玩家填的数字一眼区分
                        btn.Content = v.ToString();
                        btn.Foreground = Brushes.Black;
                        btn.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        // 玩家填的数字：一律蓝字，对错只由「检查」按钮判定
                        btn.Content = v.ToString();
                        btn.Foreground = Brushes.DodgerBlue;
                    }
                }
        }

        private bool HasMarks(int r, int c)
        {
            for (int n = 1; n <= 9; n++)
                if (_game.Marks[r, c, n]) return true;
            return false;
        }

        /// <summary>把备注数字渲染成 3×3 小网格（铅笔备注样式）</summary>
        private FrameworkElement BuildMarkGrid(int r, int c)
        {
            var grid = new Grid();
            for (int i = 0; i < 3; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            for (int n = 1; n <= 9; n++)
            {
                if (!_game.Marks[r, c, n]) continue;
                var tb = new TextBlock
                {
                    Text = n.ToString(),
                    FontSize = Math.Max(6, _cellSize * 0.3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray
                };
                Grid.SetRow(tb, (n - 1) / 3);
                Grid.SetColumn(tb, (n - 1) % 3);
                grid.Children.Add(tb);
            }
            return grid;
        }

        // ---------- 交互 ----------

        private void Cell_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int idx)
            {
                _selectedRow = idx / 9;
                _selectedCol = idx % 9;
                RefreshBoard();
            }
        }

        private void NumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string s && int.TryParse(s, out int num))
                PlaceNumber(num);
        }

        /// <summary>右键数字：在选中格子切换该数字的备注（铅笔小数字）</summary>
        private void NumberButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string s || !int.TryParse(s, out int num))
                return;
            if (_selectedRow < 0 || _selectedCol < 0)
            {
                MessageBox.Show(this, "请先点击选中一个空格，再用右键添加备注", "🧩 数独", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_game.ToggleMark(_selectedRow, _selectedCol, num))
                RefreshBoard();
        }

        private void PlaceNumber(int num)
        {
            if (_selectedRow < 0 || _selectedCol < 0) return;
            if (_game.TrySet(_selectedRow, _selectedCol, num))
            {
                RefreshBoard();
                if (_game.IsSolved())
                {
                    MessageBox.Show(this, "🎉 数独完成！", "🧩 数独", MessageBoxButton.OK, MessageBoxImage.Information);
                    _ = SendAiMessageAsync("（主人刚刚完成了这盘数独！请祝贺他，语气像可爱的桌宠，简短一点）");
                }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRow >= 0 && _selectedCol >= 0 && !_game.Given[_selectedRow, _selectedCol])
            {
                _game.TrySet(_selectedRow, _selectedCol, 0);
                _game.ClearMarks(_selectedRow, _selectedCol);   // 清除时一并清掉备注
                RefreshBoard();
            }
        }

        private void HintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_game.Hint(out int r, out int c))
            {
                _selectedRow = r; _selectedCol = c;
                RefreshBoard();
            }
            else MessageBox.Show(this, "棋盘已经填满了", "🧩 数独", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            // 纯验证：只报错误数量，不标红具体位置，错在哪靠自己推理
            var errors = _game.GetErrors();
            if (errors.Count == 0)
                MessageBox.Show(this, "✅ 目前没有错误！", "🧩 数独", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(this, $"❌ 有 {errors.Count} 处与答案不符", "🧩 数独", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void NewGameButton_Click(object sender, RoutedEventArgs e)
        {
            _game.NewGame(GetDifficulty());
            _selectedRow = _selectedCol = -1;
            RefreshBoard();
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            _game.Reset();
            _selectedRow = _selectedCol = -1;
            RefreshBoard();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 正在聊天输入框打字时不拦截数字键
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key >= Key.D1 && e.Key <= Key.D9)
            {
                PlaceNumber((int)(e.Key - Key.D1) + 1);
                e.Handled = true;
            }
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
            {
                PlaceNumber((int)(e.Key - Key.NumPad1) + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                if (_selectedRow >= 0 && _selectedCol >= 0 && !_game.Given[_selectedRow, _selectedCol])
                {
                    _game.TrySet(_selectedRow, _selectedCol, 0);
                    _game.ClearMarks(_selectedRow, _selectedCol);   // 一并清掉备注
                    RefreshBoard();
                }
                e.Handled = true;
            }
        }

        // ---------- 难度设置（仅数独窗口可见，单独存 SudokuSettings.json） ----------

        private SudokuDifficulty GetDifficulty() => DifficultyCombo.SelectedIndex switch
        {
            0 => SudokuDifficulty.Easy,
            2 => SudokuDifficulty.Hard,
            _ => SudokuDifficulty.Medium
        };

        private void DifficultyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 初始化时（棋盘未建好）或加载配置时不要触发新游戏
            if (!_boardReady || _loadingSettings) return;
            _game.NewGame(GetDifficulty());
            _selectedRow = _selectedCol = -1;
            RefreshBoard();
            SaveSudokuSettings();
        }

        private void LoadSudokuSettings()
        {
            try
            {
                if (File.Exists("SudokuSettings.json"))
                {
                    var s = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText("SudokuSettings.json"));
                    if (s.TryGetProperty("difficulty", out var d))
                    {
                        _loadingSettings = true;
                        DifficultyCombo.SelectedIndex = d.GetString() switch { "简单" => 0, "困难" => 2, _ => 1 };
                        _loadingSettings = false;
                    }
                }
            }
            catch { }
        }

        private void SaveSudokuSettings()
        {
            try
            {
                string diff = ((ComboBoxItem)DifficultyCombo.SelectedItem)?.Content?.ToString() ?? "中等";
                File.WriteAllText("SudokuSettings.json", JsonSerializer.Serialize(new { difficulty = diff }));
            }
            catch { }
        }

        // ---------- AI 互动 ----------

        private void SendChat_Click(object sender, RoutedEventArgs e)
        {
            string text = ChatInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            ChatInput.Text = "";
            AddChatMessage("你", text);
            _ = SendAiMessageAsync(text);
        }

        private void ChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                SendChat_Click(sender, e);
                e.Handled = true;
            }
        }

        // ---------- 聊天输入框占位水印（提示：无记忆、不记录） ----------

        private void UpdateWatermark()
        {
            if (ChatWatermark == null) return;
            ChatWatermark.Visibility = string.IsNullOrEmpty(ChatInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ChatInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateWatermark();

        private void ChatInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateWatermark();

        private void ChatInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateWatermark();

        /// <summary>顶栏 💬 按钮：展开/收起聊天区，窗口高度相应增减（保留用户缩放的大小）</summary>
        private void ChatToggle_Click(object sender, RoutedEventArgs e)
        {
            _chatVisible = !_chatVisible;
            ChatArea.Visibility = _chatVisible ? Visibility.Visible : Visibility.Collapsed;
            InputRow.Visibility = _chatVisible ? Visibility.Visible : Visibility.Collapsed;
            Height += _chatVisible ? ChatExpandDelta : -ChatExpandDelta;
            ChatToggleButton.Content = _chatVisible ? "🙈" : "💬";
            ChatToggleButton.ToolTip = _chatVisible ? "收起 AI 聊天" : "展开 AI 聊天";
            if (_chatVisible)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (ChatScroll != null) ChatScroll.ScrollToEnd();
                }));
                ChatInput.Focus();
            }
        }

        /// <summary>把棋盘信息连同玩家的话发给 AI（数独专用提示词，独立于桌宠记忆）</summary>
        private async Task SendAiMessageAsync(string playerText)
        {
            if (_aiBusy || _mainWindow?.AiService == null) return;
            _aiBusy = true;
            AddChatMessage("🐟 大肥鱼", "我在思考哦，请稍等");
            try
            {
                string boardText = _game.ToBoardText();
                string userMsg = $"当前棋盘：\n{boardText}\n\n完整正确答案（仅供你内部参考，不要直接告诉主人数字）：\n{BuildAnswerText()}\n\n主人说：{playerText}";

                // 带上一条对话（主人原话 + 你的上一条回复），让大肥鱼记得刚才聊过什么
                var history = new List<(string role, string content)>();
                if (_lastUserText != null && _lastAiReply != null)
                {
                    history.Add(("user", _lastUserText));
                    history.Add(("assistant", _lastAiReply));
                }

                string reply = await _mainWindow.AiService.SendMessageAsync(userMsg, _sudokuSystemPrompt, history);
                AddChatMessage("🐟 大肥鱼", reply);

                // 更新记忆：只记这一轮，旧的丢弃
                _lastUserText = playerText;
                _lastAiReply = reply;
            }
            catch (Exception ex)
            {
                AddChatMessage("🐟 大肥鱼", $"（网络出错：{ex.Message}）");
            }
            finally
            {
                _aiBusy = false;
            }
        }

        /// <summary>把完整解拼成文本（AI 内部参考用，不展示给玩家）</summary>
        private string BuildAnswerText()
        {
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < 9; r++)
            {
                sb.Append("行").Append(r + 1).Append(": ");
                for (int c = 0; c < 9; c++) sb.Append(_game.Solution[r, c]);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private void AddChatMessage(string who, string text)
        {
            var tb = new TextBlock
            {
                Text = $"{who}: {text}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 13
            };
            if (who.Contains("大肥鱼")) tb.Foreground = Brushes.DarkSlateBlue;
            else tb.Foreground = Brushes.DarkGray;
            ChatPanel.Children.Add(tb);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (ChatScroll != null) ChatScroll.ScrollToEnd();
            }));
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveSudokuSettings();
            base.OnClosed(e);
        }
    }
}

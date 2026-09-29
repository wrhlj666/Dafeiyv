using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyv
{
    public partial class SettingsWindow : Window
    {
        private readonly string configPath = "Settings.json";
        private readonly MainWindow _mainWindow;
        private bool _loadingSettings;   // 加载配置时不算“用户手动开启”，不弹提示

        public SettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _loadingSettings = true;
            LoadSettings();
            _loadingSettings = false;
        }

        private void LoadSettings()
        {
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        ApiKeyPasswordBox.Password = config.ApiKey ?? "";
                        ApiKeyTextBox.Text = config.ApiKey ?? "";
                        PromptBox.Text = config.SystemPrompt ?? "";
                        if (config.PetScale >= 0.5 && config.PetScale <= 2.0)
                            PetSizeSlider.Value = config.PetScale * 100.0;
                        if (config.IdleActionIntervalSeconds >= 10)
                            IdleIntervalSlider.Value = config.IdleActionIntervalSeconds;
                        Welcome1Box.Text = string.IsNullOrWhiteSpace(config.Welcome1) ? "大肥鱼来啦！" : config.Welcome1;
                        Welcome2Box.Text = string.IsNullOrWhiteSpace(config.Welcome2) ? "今天想聊什么？" : config.Welcome2;
                        Welcome3Box.Text = string.IsNullOrWhiteSpace(config.Welcome3) ? "点我聊天～" : config.Welcome3;
                        Welcome4Box.Text = string.IsNullOrWhiteSpace(config.Welcome4) ? "我在这里哦！" : config.Welcome4;
                        HistoryRoundsCombo.SelectedIndex = config.MaxHistoryRounds switch
                        {
                            0 => 2,
                            5 => 1,
                            _ => 0
                        };
                        ReplyLengthModeCombo.SelectedIndex = config.EnableMaxReplyLength ? 1 : 0;
                        ReplyLengthBox.Text = config.MaxReplyLength >= 1 ? config.MaxReplyLength.ToString() : "100";
                        ThinkingCheckBox.IsChecked = config.EnableThinking;
                        LongTermMemoryCheckBox.IsChecked = config.EnableLongTermMemory;
                        MemoryMaxCharsSlider.Value = config.MemoryEntryMaxChars >= 10 ? config.MemoryEntryMaxChars : 40;
                        ProactiveSpeakCheckBox.IsChecked = config.EnableProactiveSpeak;
                        int proactiveSec = config.ProactiveSpeakIntervalSeconds >= 10 ? config.ProactiveSpeakIntervalSeconds : 300;
                        ProactiveIntervalSlider.Value = Math.Min(proactiveSec, 300);
                        ProactiveMinutesBox.Text = (proactiveSec / 60.0).ToString("0.#");
                        RoamCheckBox.IsChecked = config.EnableRoam;
                        RoamIntervalSlider.Value = config.RoamIntervalSeconds >= 1 ? config.RoamIntervalSeconds : 30;
                    }
                }
                catch
                {
                    StatusText.Text = "⚠️ 读取配置文件失败，使用默认值";
                    StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                }
            }
            else
            {
                PromptBox.Text = "鲸娘DeepSeek,有鲸尾,爱吃白饭,聪明懒,傲娇甜,听主人的,不爱被说胖";
            }

            // 开机自启状态读取自注册表（不存 Settings.json）
            AutoStartCheckBox.IsChecked = AutoStartHelper.IsEnabled();

            // ✅ 同步两个框的内容
            ApiKeyPasswordBox.PasswordChanged += (s, e) =>
            {
                ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
            };
            ApiKeyTextBox.TextChanged += (s, e) =>
            {
                ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
            };
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = ApiKeyPasswordBox.Password.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = ApiKeyTextBox.Text.Trim();
            }

            string systemPrompt = PromptBox.Text.Trim();

            // ✅ 只验证 API Key
            if (string.IsNullOrEmpty(apiKey))
            {
                StatusText.Text = "⚠️ 请输入 API Key";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // ✅ 如果提示词为空，自动填入默认提示词
            if (string.IsNullOrEmpty(systemPrompt))
            {
                systemPrompt = "鲸娘DeepSeek,有鲸尾,爱吃白饭,聪明懒,傲娇甜,听主人的,不爱被说胖";
                PromptBox.Text = systemPrompt; // 显示在文本框里，让用户看到
                StatusText.Text = "📝 已恢复默认提示词";
                StatusText.Foreground = System.Windows.Media.Brushes.Blue;
            }

            var config = new AppConfig
            {
                ApiKey = apiKey,
                SystemPrompt = systemPrompt,
                PetScale = PetSizeSlider.Value / 100.0,
                IdleActionIntervalSeconds = (int)Math.Round(IdleIntervalSlider.Value),
                Welcome1 = Welcome1Box.Text.Trim(),
                Welcome2 = Welcome2Box.Text.Trim(),
                Welcome3 = Welcome3Box.Text.Trim(),
                Welcome4 = Welcome4Box.Text.Trim(),
                MaxHistoryRounds = HistoryRoundsCombo.SelectedIndex switch { 2 => 0, 1 => 5, _ => 10 },
                EnableMaxReplyLength = ReplyLengthModeCombo.SelectedIndex == 1,
                MaxReplyLength = int.TryParse(ReplyLengthBox.Text.Trim(), out int n) && n > 0 ? n : 100,
                EnableThinking = ThinkingCheckBox.IsChecked == true,
                EnableLongTermMemory = LongTermMemoryCheckBox.IsChecked == true,
                MemoryEntryMaxChars = (int)Math.Round(MemoryMaxCharsSlider.Value),
                EnableProactiveSpeak = ProactiveSpeakCheckBox.IsChecked == true,
                ProactiveSpeakIntervalSeconds = ParseProactiveSeconds(),
                EnableRoam = RoamCheckBox.IsChecked == true,
                RoamIntervalSeconds = (int)Math.Round(RoamIntervalSlider.Value)
            };

            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);

                if (StatusText.Text != "📝 已恢复默认提示词")
                {
                    StatusText.Text = "✅ 设置已保存！";
                    StatusText.Foreground = System.Windows.Media.Brushes.Green;
                }

                _mainWindow?.ReloadConfig(apiKey, systemPrompt);
                _mainWindow?.ApplyMemorySettings(config.MaxHistoryRounds);
                _mainWindow?.ApplyReplyLengthSetting(config.EnableMaxReplyLength, config.MaxReplyLength);
                _mainWindow?.ApplyThinkingSetting(config.EnableThinking);
                _mainWindow?.ApplyLongTermMemory(config.EnableLongTermMemory, config.MemoryEntryMaxChars);
                _mainWindow?.ApplyProactiveSpeak(config.EnableProactiveSpeak, config.ProactiveSpeakIntervalSeconds);
                _mainWindow?.ApplyRoam(config.EnableRoam, config.RoamIntervalSeconds);
                AutoStartHelper.SetEnabled(AutoStartCheckBox.IsChecked == true);
                _mainWindow?.ApplyPetScale(config.PetScale);
                _mainWindow?.SetIdleInterval(config.IdleActionIntervalSeconds);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ 保存失败：{ex.Message}";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ✅ 显示 API Key
        private void ShowPasswordCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
            ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
            ApiKeyTextBox.Visibility = Visibility.Visible;
        }

        // ✅ 隐藏 API Key
        private void ShowPasswordCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyPasswordBox.Visibility = Visibility.Visible;
        }

        // ✅ 宠物大小滑条：实时显示百分比
        private void PetSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PetSizeValueText != null)
                PetSizeValueText.Text = $"{e.NewValue:0}%";
        }

        // ✅ 随机动作间隔滑条：实时显示秒数
        private void IdleIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IdleIntervalValueText != null)
                IdleIntervalValueText.Text = $"{e.NewValue:0} 秒";
        }

        // ✅ 回复字数模式切换：自定义时启用字数输入框
        private void ReplyLengthModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReplyLengthBox != null)
                ReplyLengthBox.IsEnabled = ReplyLengthModeCombo.SelectedIndex == 1;
        }

        // ✅ 深度思考开关
        private void ThinkingCheckBox_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void ThinkingCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
        }

        // ✅ 长期记忆开关：控制字数滑条可用性，并在用户手动开启时弹出 token 提示
        private void LongTermMemoryCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (MemoryMaxCharsSlider != null)
                MemoryMaxCharsSlider.IsEnabled = true;

            if (_loadingSettings) return;   // 加载已保存配置时不算手动开启

            MessageBox.Show(this,
                "记忆量过大会导致token消耗速度增大,请适度调整以及定期清理记忆文档",
                "🧠 长期记忆提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void LongTermMemoryCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (MemoryMaxCharsSlider != null)
                MemoryMaxCharsSlider.IsEnabled = false;
        }

        // ✅ 每条记忆字数滑条：实时显示
        private void MemoryMaxCharsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MemoryMaxCharsValueText != null)
                MemoryMaxCharsValueText.Text = $"{e.NewValue:0} 字";
        }

        // ✅ 主动说话间隔滑条（秒）：拖动时同步到分钟输入框
        private void ProactiveIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingSettings) return;
            if (ProactiveMinutesBox != null)
                ProactiveMinutesBox.Text = (e.NewValue / 60.0).ToString("0.#");
        }

        /// <summary>自填分钟 → 秒（最低 10s，最高 3600s = 60 分钟）</summary>
        private int ParseProactiveSeconds()
        {
            if (double.TryParse(ProactiveMinutesBox.Text.Trim(), out double minutes) && minutes > 0)
                return Math.Clamp((int)Math.Round(minutes * 60), 10, 3600);
            return 300;
        }

        // ✅ 主动说话开关：控制间隔滑条和分钟输入框可用性
        private void ProactiveSpeakCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (ProactiveIntervalSlider != null)
                ProactiveIntervalSlider.IsEnabled = true;
            if (ProactiveMinutesBox != null)
                ProactiveMinutesBox.IsEnabled = true;
        }

        private void ProactiveSpeakCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ProactiveIntervalSlider != null)
                ProactiveIntervalSlider.IsEnabled = false;
            if (ProactiveMinutesBox != null)
                ProactiveMinutesBox.IsEnabled = false;
        }

        // ✅ 漫游间隔滑条：实时显示
        private void RoamIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RoamIntervalValueText != null)
                RoamIntervalValueText.Text = $"{e.NewValue:0} 秒";
        }

        // ✅ 漫游开关：控制间隔滑条可用性
        private void RoamCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (RoamIntervalSlider != null)
                RoamIntervalSlider.IsEnabled = true;
        }

        private void RoamCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (RoamIntervalSlider != null)
                RoamIntervalSlider.IsEnabled = false;
        }

        // ✅ 打开长期记忆文档（Memory.txt）
        private void OpenMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.OpenMemoryFile();
        }
    }
}
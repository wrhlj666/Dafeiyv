using System.Windows;

namespace Dafeiyv
{
    public partial class HistoryWindow : Window
    {
        private MainWindow _mainWindow;

        public HistoryWindow(string historyText, MainWindow mainWindow)
        {
            InitializeComponent();
            HistoryTextBlock.Text = historyText;
            _mainWindow = mainWindow;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.ClearHistory();
            HistoryTextBlock.Text = "🧹 对话记录已清空";

            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = System.TimeSpan.FromSeconds(1.5);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                Close();
            };
            timer.Start();
        }
    }
}
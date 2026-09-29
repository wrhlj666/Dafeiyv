using System.Windows;

namespace Dafeiyv
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // 统一以 exe 所在目录为工作目录，保证 Settings.json / Images/ / Idle/ 等
            // 相对路径无论从哪里启动（双击、VS 调试、快捷方式）都能正确解析。
            Environment.CurrentDirectory = AppContext.BaseDirectory;
        }
    }
}

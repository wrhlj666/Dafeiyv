using System;
using Microsoft.Win32;

namespace Dafeiyv
{
    /// <summary>
    /// 开机自启：写入当前用户的注册表 Run 键（HKCU，无需管理员权限）。
    /// 自启状态存在注册表里，不存 Settings.json。
    /// </summary>
    public static class AutoStartHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Dafeiyv";

        /// <summary>当前程序是否已注册开机自启（按当前 exe 路径比对）</summary>
        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                if (key?.GetValue(ValueName) is not string value) return false;
                if (string.IsNullOrWhiteSpace(value)) return false;

                string path = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(path)) return false;
                return value.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>设置 / 取消开机自启</summary>
        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null) return;

                if (enabled)
                {
                    if (Environment.ProcessPath is string path)
                        key.SetValue(ValueName, $"\"{path}\"");
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
                // 注册表写入失败时静默（如权限受限），由设置界面状态反馈
            }
        }
    }
}

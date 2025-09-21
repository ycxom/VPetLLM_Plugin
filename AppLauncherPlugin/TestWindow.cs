using System;
using System.Windows;

namespace AppLauncherPlugin
{
    public class TestWindow
    {
        public static void TestSettingsWindow()
        {
            try
            {
                // 创建一个简单的测试应用程序上下文
                if (Application.Current == null)
                {
                    var app = new Application();
                }

                var plugin = new AppLauncherPlugin();
                plugin.PluginDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                
                var settingWindow = new winAppLauncherSetting(plugin);
                settingWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"测试失败: {ex.Message}\n\n详细信息:\n{ex.StackTrace}", 
                    "测试错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
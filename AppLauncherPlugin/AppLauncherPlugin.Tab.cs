using System.Windows;

namespace AppLauncherPlugin
{
    public partial class AppLauncherPlugin
    {
        public string TabTitle => _vpetLLM?.Settings.Language switch
        {
            "zh-hant" => "應用啟動器",
            "en" => "App Launcher",
            "ja" => "アプリランチャー",
            _ => "应用启动器"
        };

        public FrameworkElement CreatePanel() => new winAppLauncherSetting(this);
    }
}

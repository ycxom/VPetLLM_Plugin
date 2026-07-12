using System.Windows;

namespace ForegroundAppPlugin
{
    public partial class ForegroundAppPlugin
    {
        public string TabTitle => _vpetLLM?.Settings.Language switch
        {
            "zh-hant" => "前臺應用",
            "en" => "Foreground App",
            "ja" => "フォアグラウンド",
            _ => "前台应用"
        };

        public FrameworkElement CreatePanel() => new winForegroundAppSetting(this);
    }
}

using System.Windows;

namespace OneBotPlugin
{
    public partial class OneBotPlugin
    {
        public string TabTitle => _vpetLLM?.Settings.Language switch
        {
            "zh-hant" => "OneBot",
            "en" => "OneBot",
            "ja" => "OneBot",
            _ => "OneBot"
        };

        public FrameworkElement CreatePanel() => new winOneBotSetting(this, _settings, _vpetLLM?.Settings.Language ?? "en", OnSettingsSaved);
    }
}

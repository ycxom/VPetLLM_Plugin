using System.Windows;

namespace PixivPlugin
{
    public partial class PixivPlugin
    {
        public string TabTitle => _vpetLLM?.Settings.Language switch
        {
            "zh-hant" => "Pixiv",
            "en" => "Pixiv",
            "ja" => "Pixiv",
            _ => "Pixiv"
        };

        public FrameworkElement CreatePanel() => new winPixivSetting(this);
    }
}

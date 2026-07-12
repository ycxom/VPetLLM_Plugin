using System.Windows;

namespace StickerPlugin
{
    public partial class StickerPlugin
    {
        public string TabTitle => _vpetLLM?.Settings.Language switch
        {
            "zh-hant" => "表情包",
            "en" => "Stickers",
            "ja" => "スタンプ",
            _ => "表情包"
        };

        public FrameworkElement CreatePanel() => new winStickerSetting(this);
    }
}

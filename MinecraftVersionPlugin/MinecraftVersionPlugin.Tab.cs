using System.Windows;

namespace MinecraftVersionPlugin
{
    public partial class MinecraftVersionPlugin
    {
        public string TabTitle => _vpetLLM?.Settings.Language switch
        {
            "zh-hant" => "Minecraft",
            "en" => "Minecraft",
            "ja" => "Minecraft",
            _ => "Minecraft"
        };

        public FrameworkElement CreatePanel() => new winMinecraftSetting(this);
    }
}

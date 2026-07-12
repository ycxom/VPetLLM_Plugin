using System.Windows;

namespace WebSearchPlugin
{
    public partial class WebSearchPlugin
    {
        public string TabTitle => _vpetLLM?.Settings.Language switch
        {
            "zh-hant" => "網路搜尋",
            "en" => "Web Search",
            "ja" => "ウェブ検索",
            _ => "网络搜索"
        };

        public FrameworkElement CreatePanel()
        {
            _settings.SetPluginDataDir(PluginDataDir);
            return new winWebSearchSettings(_settings, OnSettingsSaved);
        }
    }
}

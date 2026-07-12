using System.Windows;

namespace WeatherPlugin
{
    public partial class WeatherPlugin
    {
        public string TabTitle => _vpetLLM?.Settings.Language switch
        {
            "zh-hant" => "天氣",
            "en" => "Weather",
            "ja" => "天気",
            _ => "天气"
        };

        public FrameworkElement CreatePanel() => new winWeatherSetting(this);
    }
}

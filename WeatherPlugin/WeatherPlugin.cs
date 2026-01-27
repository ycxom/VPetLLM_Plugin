using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Infrastructure.Configuration;
using WeatherPlugin.Data;
using WeatherPlugin.Models;
using WeatherPlugin.Providers;
using WeatherPlugin.Search;
using WeatherPlugin.Utils;

namespace WeatherPlugin
{
    /// <summary>
    /// VPetLLM 天气插件
    /// 支持查询中国地区天气信息
    /// </summary>
    public class WeatherPlugin : IActionPlugin, IPluginWithData, IDynamicInfoPlugin
    {
        public string Name => "Weather";
        public string Author => "ycxom";

        public string Description
        {
            get
            {
                if (_vpetLLM is null)
                    return "查询中国地区天气信息。注意：该接口仅支持中国地区数据。";

                return _vpetLLM.Settings.Language switch
                {
                    "ja" => "中国地域の天気情報を照会します。注意：このAPIは中国地域のデータのみをサポートしています。",
                    "zh-hant" => "查詢中國地區天氣資訊。注意：該接口僅支持中國地區數據。",
                    "en" => "Query weather information for China regions. Note: This API only supports China region data.",
                    _ => "查询中国地区天气信息。注意：该接口仅支持中国地区数据。"
                };
            }
        }

        public string Parameters => "city(string, optional), type(string, optional: current/forecast), action(string, optional: setting)";

        public string Examples => "Example: `<|plugin_Weather_begin|> city(北京), type(current) <|plugin_Weather_end|>` or `<|plugin_Weather_begin|> city(上海), type(forecast) <|plugin_Weather_end|>`";

        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = string.Empty;
        public string PluginDataDir { get; set; } = string.Empty;

        private VPetLLM.VPetLLM? _vpetLLM;
        private WeatherSettings _settings = new WeatherSettings();
        private CityDatabase? _cityDatabase;
        private CityVectorSearch? _citySearch;
        private IWeatherApiProvider? _weatherProvider;

        private const string SettingsFileName = "WeatherPlugin.json";

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            // 注意：不要覆盖 FilePath，PluginManager 已经正确设置了 DLL 文件路径

            // 加载设置
            LoadSettings();

            // 初始化城市数据库和搜索（传递数据目录用于缓存城市数据）
            _cityDatabase = new CityDatabase(PluginDataDir);
            _citySearch = new CityVectorSearch(_cityDatabase);

            // 初始化天气提供商
            _weatherProvider = new ExlbWeatherProvider();

            // 应用设置到提供商
            _weatherProvider.SetTimeout(_settings.TimeoutSeconds);

            VPetLLM.Utils.System.Logger.Log("Weather Plugin Initialized!");
        }

        public async Task<string> Function(string arguments)
        {
            try
            {
                // 检查是否是打开设置窗口
                var actionMatch = Regex.Match(arguments, @"action\((\w+)\)");
                if (actionMatch.Success && actionMatch.Groups[1].Value.ToLower() == "setting")
                {
                    OpenSettingsWindow();
                    return "设置窗口已打开。";
                }

                // 解析城市参数
                var cityMatch = Regex.Match(arguments, @"city\(([^)]+)\)");
                var cityName = cityMatch.Success ? cityMatch.Groups[1].Value.Trim('"', ' ') : _settings.DefaultCity;

                // 解析类型参数
                var typeMatch = Regex.Match(arguments, @"type\((\w+)\)");
                var queryType = typeMatch.Success ? typeMatch.Groups[1].Value.ToLower() : "current";

                // 检查是否为非中国地区
                if (_citySearch is not null && _citySearch.IsNonChinaRegion(cityName))
                {
                    return "该接口仅支持中国地区天气数据查询，暂不支持其他国家和地区。";
                }

                // 查找城市 Adcode
                int? adcode = null;
                if (_citySearch is not null)
                {
                    adcode = _citySearch.FindBestMatch(cityName);
                }

                if (!adcode.HasValue)
                {
                    return $"未找到城市 '{cityName}'，请检查城市名称是否正确。支持的城市包括中国大陆各省市区县。";
                }

                // 获取天气数据
                if (_weatherProvider is null)
                {
                    return "天气服务未初始化，请稍后重试。";
                }

                if (queryType == "forecast")
                {
                    var forecast = await _weatherProvider.GetForecastAsync(adcode.Value);
                    if (forecast is null)
                    {
                        return $"获取 {cityName} 天气预报失败，请稍后重试。";
                    }
                    return WeatherFormatter.FormatForecastShort(forecast);
                }
                else
                {
                    var weather = await _weatherProvider.GetCurrentWeatherAsync(adcode.Value);
                    if (weather is null)
                    {
                        return $"获取 {cityName} 当前天气失败，请稍后重试。";
                    }
                    return WeatherFormatter.FormatCurrentWeatherShort(weather);
                }
            }
            catch (TimeoutException)
            {
                return "网络请求超时，请稍后重试。";
            }
            catch (Exception ex)
            {
                Log($"Weather Plugin Error: {ex.Message}");
                return $"天气查询失败: {ex.Message}";
            }
        }

        public void Unload()
        {
            SaveSettings();
            VPetLLM.Utils.System.Logger.Log("Weather Plugin Unloaded!");
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }

        private void OpenSettingsWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var settingWindow = new winWeatherSetting(this);
                settingWindow.Show();
            });
        }

        #region Settings Management

        public void LoadSettings()
        {
            _settings = PluginConfigHelper.Load<WeatherSettings>("Weather");
        }

        public void SaveSettings()
        {
            PluginConfigHelper.Save("Weather", _settings);
        }

        public WeatherSettings GetSettings() => _settings;

        public void UpdateSettings(WeatherSettings settings)
        {
            _settings = settings;
            _weatherProvider?.SetTimeout(_settings.TimeoutSeconds);
            SaveSettings();
        }

        public CityDatabase? GetCityDatabase() => _cityDatabase;

        #endregion

        #region IDynamicInfoPlugin

        /// <summary>
        /// 向 AI 提供用户所在城市的动态信息
        /// </summary>
        public string GetDynamicInfo()
        {
            if (!string.IsNullOrEmpty(_settings.DefaultCity))
            {
                return $"The user is located in {_settings.DefaultCity}, China.";
            }
            return string.Empty;
        }

        #endregion
    }
}

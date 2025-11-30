using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WeatherPlugin.Models;
using WeatherPlugin.Utils;

namespace WeatherPlugin.Providers
{
    /// <summary>
    /// Exlb 天气 API 提供商实现
    /// 使用 weather.exlb.net API，返回 LPS 格式数据
    /// </summary>
    public class ExlbWeatherProvider : IWeatherApiProvider
    {
        private const string BaseUrl = "https://weather.exlb.net/Weather";
        private int _timeoutSeconds = 5;
        private string _apiKey = string.Empty;

        public string Name => "Exlb";

        public bool RequiresApiKey => false;

        public void SetApiKey(string apiKey)
        {
            _apiKey = apiKey;
        }

        public void SetTimeout(int timeoutSeconds)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        public async Task<CurrentWeather?> GetCurrentWeatherAsync(int adcode)
        {
            try
            {
                var param = $"adcode={adcode}&extensions=base";
                var responseStr = await HttpHelper.SendGetRequestAsync(BaseUrl, param, _timeoutSeconds);

                if (string.IsNullOrWhiteSpace(responseStr))
                    return null;

                return ParseCurrentWeatherFromLps(responseStr);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<Forecast?> GetForecastAsync(int adcode)
        {
            try
            {
                var param = $"adcode={adcode}&extensions=all";
                var responseStr = await HttpHelper.SendGetRequestAsync(BaseUrl, param, _timeoutSeconds);

                if (string.IsNullOrWhiteSpace(responseStr))
                    return null;

                return ParseForecastFromLps(responseStr);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 从 LPS 格式解析当前天气
        /// </summary>
        private CurrentWeather? ParseCurrentWeatherFromLps(string lpsData)
        {
            try
            {
                var weather = new CurrentWeather();

                // Lives 数据在整个响应中
                weather.Province = ExtractLpsValue(lpsData, "Province");
                weather.City = ExtractLpsValue(lpsData, "City");
                weather.Weather = ExtractLpsValue(lpsData, "Weather");
                weather.WindDirectionStr = ExtractLpsValue(lpsData, "WindDirection");
                weather.WindPower = ExtractLpsValue(lpsData, "WindPower");

                if (int.TryParse(ExtractLpsValue(lpsData, "Adcode"), out var adcode))
                    weather.Adcode = adcode;

                if (double.TryParse(ExtractLpsValue(lpsData, "TemperatureFloat"), out var temp))
                    weather.TemperatureFloat = temp;

                if (double.TryParse(ExtractLpsValue(lpsData, "HumidityFloat"), out var humidity))
                    weather.HumidityFloat = humidity;

                // 解析时间（LPS 格式的 ticks）
                if (long.TryParse(ExtractLpsValue(lpsData, "ReportTime"), out var ticks))
                    weather.ReportTime = new DateTime(ticks);
                else
                    weather.ReportTime = DateTime.Now;

                return weather;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从 LPS 格式解析天气预报
        /// </summary>
        private Forecast? ParseForecastFromLps(string lpsData)
        {
            try
            {
                var forecast = new Forecast();

                // 解析 Forecasts 部分
                var forecastsMatch = Regex.Match(lpsData, @"Forecasts#(.+?)(?:Lives#|$)");
                if (!forecastsMatch.Success)
                    return null;

                var forecastsData = forecastsMatch.Groups[1].Value;

                forecast.City = ExtractLpsValue(forecastsData, "City");
                forecast.Province = ExtractLpsValue(forecastsData, "Province");

                if (int.TryParse(ExtractLpsValue(forecastsData, "Adcode"), out var adcode))
                    forecast.Adcode = adcode;

                if (long.TryParse(ExtractLpsValue(forecastsData, "ReportTime"), out var ticks))
                    forecast.ReportTime = new DateTime(ticks);
                else
                    forecast.ReportTime = DateTime.Now;

                // 解析 Casts（每日预报）
                forecast.Casts = ParseCastsFromLps(forecastsData);

                return forecast;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析每日天气预报列表
        /// </summary>
        private List<WeatherCast> ParseCastsFromLps(string data)
        {
            var casts = new List<WeatherCast>();

            // 按 "deflinename:" 分割获取每个 cast
            var castParts = data.Split(new[] { "deflinename:" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in castParts)
            {
                if (!part.Contains("Date"))
                    continue;

                var cast = new WeatherCast();

                if (long.TryParse(ExtractLpsValue(part, "Date"), out var dateTicks))
                    cast.Date = new DateTime(dateTicks);

                cast.DayWeather = ExtractLpsValue(part, "DayWeather");
                cast.NightWeather = ExtractLpsValue(part, "NightWeather");
                cast.DayWind = ExtractLpsValue(part, "DayWind");
                cast.NightWind = ExtractLpsValue(part, "NightWind");
                cast.DayPower = ExtractLpsValue(part, "DayPower");
                cast.NightPower = ExtractLpsValue(part, "NightPower");

                if (double.TryParse(ExtractLpsValue(part, "DayTempFloat"), out var dayTemp))
                    cast.DayTempFloat = dayTemp;

                if (double.TryParse(ExtractLpsValue(part, "NightTempFloat"), out var nightTemp))
                    cast.NightTempFloat = nightTemp;

                casts.Add(cast);
            }

            return casts;
        }

        /// <summary>
        /// 从 LPS 数据中提取指定字段的值
        /// 格式: FieldName/!id值:/!!/!| 或 FieldName/!!!id值:/!!!!
        /// 实际格式示例: Province/!id北京:/!!/!|
        /// </summary>
        private string ExtractLpsValue(string data, string fieldName)
        {
            // 匹配模式: FieldName/!+id值: (值可能包含中文、数字、负号等)
            var pattern = $@"{fieldName}/!+id([^:]+):";
            var match = Regex.Match(data, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return string.Empty;
        }
    }
}

using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace WeatherPlugin.Models
{
    /// <summary>
    /// 表示天气预报的响应模型类
    /// </summary>
    public class WeatherResponse
    {
        [JsonProperty("status")]
        public int Status { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("info")]
        public string Info { get; set; } = string.Empty;

        [JsonProperty("infocode")]
        public int Infocode { get; set; }

        [JsonProperty("forecasts")]
        public List<Forecast> Forecasts { get; set; } = new List<Forecast>();

        [JsonProperty("lives")]
        public List<CurrentWeather> Lives { get; set; } = new List<CurrentWeather>();
    }

    /// <summary>
    /// 表示单个天气预报的模型类
    /// </summary>
    public class Forecast
    {
        [JsonProperty("city")]
        public string City { get; set; } = string.Empty;

        [JsonProperty("adcode")]
        public int Adcode { get; set; }

        [JsonProperty("province")]
        public string Province { get; set; } = string.Empty;

        [JsonProperty("reporttime")]
        public DateTime ReportTime { get; set; }

        [JsonProperty("casts")]
        public List<WeatherCast> Casts { get; set; } = new List<WeatherCast>();
    }

    /// <summary>
    /// 表示单个天气情况的模型类
    /// </summary>
    public class WeatherCast
    {
        [JsonProperty("date")]
        public DateTime Date { get; set; }

        [JsonProperty("dayweather")]
        public string DayWeather { get; set; } = string.Empty;

        [JsonProperty("nightweather")]
        public string NightWeather { get; set; } = string.Empty;

        [JsonProperty("daywind")]
        public string DayWind { get; set; } = string.Empty;

        [JsonProperty("nightwind")]
        public string NightWind { get; set; } = string.Empty;

        [JsonProperty("daypower")]
        public string DayPower { get; set; } = string.Empty;

        [JsonProperty("nightpower")]
        public string NightPower { get; set; } = string.Empty;

        [JsonProperty("daytemp_float")]
        public double DayTempFloat { get; set; }

        [JsonProperty("nighttemp_float")]
        public double NightTempFloat { get; set; }

        /// <summary>
        /// 获取白天天气条件枚举
        /// </summary>
        public WeatherCondition GetDayWeatherCondition()
        {
            return WeatherConditionHelper.Parse(DayWeather);
        }

        /// <summary>
        /// 获取夜间天气条件枚举
        /// </summary>
        public WeatherCondition GetNightWeatherCondition()
        {
            return WeatherConditionHelper.Parse(NightWeather);
        }

        /// <summary>
        /// 获取白天风向枚举
        /// </summary>
        public WindDirection GetDayWindDirection()
        {
            return WindDirectionHelper.Parse(DayWind);
        }

        /// <summary>
        /// 获取夜间风向枚举
        /// </summary>
        public WindDirection GetNightWindDirection()
        {
            return WindDirectionHelper.Parse(NightWind);
        }
    }

    /// <summary>
    /// 表示单个城市当前天气的模型类
    /// </summary>
    public class CurrentWeather
    {
        [JsonProperty("province")]
        public string Province { get; set; } = string.Empty;

        [JsonProperty("city")]
        public string City { get; set; } = string.Empty;

        [JsonProperty("adcode")]
        public int Adcode { get; set; }

        [JsonProperty("weather")]
        public string Weather { get; set; } = string.Empty;

        [JsonProperty("winddirection")]
        public string WindDirectionStr { get; set; } = string.Empty;

        [JsonProperty("windpower")]
        public string WindPower { get; set; } = string.Empty;

        [JsonProperty("reporttime")]
        public DateTime ReportTime { get; set; }

        [JsonProperty("temperature_float")]
        public double TemperatureFloat { get; set; }

        [JsonProperty("humidity_float")]
        public double HumidityFloat { get; set; }

        /// <summary>
        /// 获取天气条件枚举
        /// </summary>
        public WeatherCondition GetWeatherCondition()
        {
            return WeatherConditionHelper.Parse(Weather);
        }

        /// <summary>
        /// 获取风向枚举
        /// </summary>
        public WindDirection GetWindDirection()
        {
            return WindDirectionHelper.Parse(WindDirectionStr);
        }
    }

    /// <summary>
    /// 天气现象枚举
    /// </summary>
    public enum WeatherCondition
    {
        晴, 少云, 晴间多云, 多云, 阴, 有风, 平静, 微风, 和风, 清风,
        强风, 疾风, 大风, 烈风, 风暴, 狂爆风, 飓风, 热带风暴, 霾,
        中度霾, 重度霾, 严重霾, 阵雨, 雷阵雨, 雷阵雨并伴有冰雹,
        小雨, 中雨, 大雨, 暴雨, 大暴雨, 特大暴雨, 强阵雨, 强雷阵雨,
        极端降雨, 毛毛雨, 雨, 小雨中雨, 中雨大雨, 大雨暴雨,
        暴雨大暴雨, 大暴雨特大暴雨, 雨雪天气, 雨夹雪, 阵雨夹雪,
        冻雨, 雪, 阵雪, 小雪, 中雪, 大雪, 暴雪, 小雪中雪,
        中雪大雪, 大雪暴雪, 浮尘, 扬沙, 沙尘暴, 强沙尘暴,
        龙卷风, 雾, 浓雾, 强浓雾, 轻雾, 大雾, 特强浓雾, 热, 冷, 未知
    }

    /// <summary>
    /// 风向枚举
    /// </summary>
    public enum WindDirection
    {
        无风向, 东北, 东, 东南, 南, 西南, 西, 西北, 北, 旋转不定
    }

    /// <summary>
    /// 天气条件解析帮助类
    /// </summary>
    public static class WeatherConditionHelper
    {
        public static WeatherCondition Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return WeatherCondition.未知;

            if (Enum.TryParse<WeatherCondition>(value, out var result))
                return result;

            return WeatherCondition.未知;
        }
    }

    /// <summary>
    /// 风向解析帮助类
    /// </summary>
    public static class WindDirectionHelper
    {
        public static WindDirection Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return WindDirection.无风向;

            if (Enum.TryParse<WindDirection>(value, out var result))
                return result;

            return WindDirection.无风向;
        }
    }
}

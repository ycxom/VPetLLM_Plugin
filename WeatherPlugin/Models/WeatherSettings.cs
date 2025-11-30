using Newtonsoft.Json;

namespace WeatherPlugin.Models
{
    /// <summary>
    /// 天气插件设置
    /// </summary>
    public class WeatherSettings
    {
        /// <summary>
        /// 默认城市名称
        /// </summary>
        [JsonProperty("default_city")]
        public string DefaultCity { get; set; } = "北京市";

        /// <summary>
        /// 默认城市行政区划代码
        /// </summary>
        [JsonProperty("default_adcode")]
        public int DefaultAdcode { get; set; } = 110000;



        /// <summary>
        /// 请求超时时间（秒）
        /// </summary>
        [JsonProperty("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 5;

        public override bool Equals(object? obj)
        {
            if (obj is not WeatherSettings other)
                return false;

            return DefaultCity == other.DefaultCity &&
                   DefaultAdcode == other.DefaultAdcode &&
                   TimeoutSeconds == other.TimeoutSeconds;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DefaultCity, DefaultAdcode, TimeoutSeconds);
        }
    }
}

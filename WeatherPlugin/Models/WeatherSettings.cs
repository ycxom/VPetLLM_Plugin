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

        /// <summary>
        /// 代理模式：System = 跟随系统代理（默认，与历史行为一致）；Direct = 直连；Custom = 自定义代理
        /// </summary>
        [JsonProperty("proxy_mode")]
        public string ProxyMode { get; set; } = "System";

        /// <summary>
        /// 自定义代理地址（仅 ProxyMode = Custom 时生效），如 http://127.0.0.1:7890
        /// </summary>
        [JsonProperty("proxy_address")]
        public string ProxyAddress { get; set; } = "";

        public override bool Equals(object? obj)
        {
            if (obj is not WeatherSettings other)
                return false;

            return DefaultCity == other.DefaultCity &&
                   DefaultAdcode == other.DefaultAdcode &&
                   TimeoutSeconds == other.TimeoutSeconds &&
                   ProxyMode == other.ProxyMode &&
                   ProxyAddress == other.ProxyAddress;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DefaultCity, DefaultAdcode, TimeoutSeconds, ProxyMode, ProxyAddress);
        }
    }
}

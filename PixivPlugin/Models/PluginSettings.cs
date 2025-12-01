using Newtonsoft.Json;

namespace PixivPlugin.Models
{
    /// <summary>
    /// Pixiv 插件设置
    /// </summary>
    public class PluginSettings
    {
        /// <summary>
        /// 是否启用代理（仅用于图片下载）
        /// </summary>
        [JsonProperty("useProxy")]
        public bool UseProxy { get; set; } = false;

        /// <summary>
        /// 是否跟随 VPetLLM 代理设置
        /// </summary>
        [JsonProperty("followVPetLLMProxy")]
        public bool FollowVPetLLMProxy { get; set; } = true;

        /// <summary>
        /// 自定义代理 URL
        /// </summary>
        [JsonProperty("proxyUrl")]
        public string? ProxyUrl { get; set; }

        /// <summary>
        /// 图片下载保存目录
        /// </summary>
        [JsonProperty("downloadPath")]
        public string? DownloadPath { get; set; }

        /// <summary>
        /// 请求超时时间（秒）
        /// </summary>
        [JsonProperty("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 30;
    }
}

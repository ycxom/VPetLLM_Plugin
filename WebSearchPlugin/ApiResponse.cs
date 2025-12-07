using Newtonsoft.Json;

namespace WebSearchPlugin
{
    /// <summary>
    /// API 响应模型
    /// </summary>
    public class ApiResponse
    {
        /// <summary>
        /// 请求是否成功
        /// </summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>
        /// 请求的 URL
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; } = "";

        /// <summary>
        /// 返回的内容（Markdown 格式）
        /// </summary>
        [JsonProperty("content")]
        public string Content { get; set; } = "";

        /// <summary>
        /// 内容格式
        /// </summary>
        [JsonProperty("format")]
        public string Format { get; set; } = "";

        /// <summary>
        /// 抓取方法
        /// </summary>
        [JsonProperty("fetch_method")]
        public string FetchMethod { get; set; } = "";
    }
}

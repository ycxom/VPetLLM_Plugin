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

    /// <summary>
    /// API 错误响应模型：后端返回 error 字段，鉴权代理返回 message 字段
    /// </summary>
    public class ApiErrorResponse
    {
        [JsonProperty("error")]
        public string? Error { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }
    }
}

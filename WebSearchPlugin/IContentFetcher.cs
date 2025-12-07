using System.Threading.Tasks;

namespace WebSearchPlugin
{
    /// <summary>
    /// 内容抓取结果
    /// </summary>
    public class FetchResult
    {
        /// <summary>
        /// 是否成功获取内容
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 获取到的内容（Markdown 格式）
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// 使用的模式：API 或 Local
        /// </summary>
        public string Mode { get; set; } = "";

        /// <summary>
        /// 是否使用了降级（fallback）
        /// </summary>
        public bool UsedFallback { get; set; }

        /// <summary>
        /// 错误信息（如果失败）
        /// </summary>
        public string ErrorMessage { get; set; } = "";
    }

    /// <summary>
    /// 内容抓取器接口
    /// </summary>
    public interface IContentFetcher
    {
        /// <summary>
        /// 异步获取指定 URL 的内容
        /// </summary>
        /// <param name="url">要获取内容的 URL</param>
        /// <returns>抓取结果</returns>
        Task<FetchResult> FetchAsync(string url);
    }
}

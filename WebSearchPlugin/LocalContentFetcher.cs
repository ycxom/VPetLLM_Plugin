using System;
using System.Threading.Tasks;

namespace WebSearchPlugin
{
    /// <summary>
    /// 本地内容抓取器，使用 HTML 解析获取网页内容
    /// </summary>
    public class LocalContentFetcher : IContentFetcher
    {
        private readonly WebScraper _scraper;

        public LocalContentFetcher(WebScraper scraper)
        {
            _scraper = scraper ?? throw new ArgumentNullException(nameof(scraper));
        }

        public async Task<FetchResult> FetchAsync(string url)
        {
            try
            {
                VPetLLM.Utils.System.Logger.Log($"LocalContentFetcher: Fetching {url}");
                
                var content = await _scraper.FetchAsMarkdown(url);
                
                if (string.IsNullOrEmpty(content))
                {
                    return new FetchResult
                    {
                        Success = false,
                        Mode = "Local",
                        ErrorMessage = "无法获取网页内容"
                    };
                }

                // 检查是否返回了错误信息
                if (content.StartsWith("错误："))
                {
                    return new FetchResult
                    {
                        Success = false,
                        Mode = "Local",
                        ErrorMessage = content
                    };
                }

                return new FetchResult
                {
                    Success = true,
                    Content = content,
                    Mode = "Local",
                    UsedFallback = false
                };
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.System.Logger.Log($"LocalContentFetcher error: {ex.Message}");
                return new FetchResult
                {
                    Success = false,
                    Mode = "Local",
                    ErrorMessage = $"本地抓取失败: {ex.Message}"
                };
            }
        }
    }
}

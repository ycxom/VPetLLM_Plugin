using PixivPlugin.Models;

namespace PixivPlugin.Services
{
    /// <summary>
    /// 图片反向代理服务，处理 Pixiv 图片 URL 的代理转换
    /// </summary>
    public class ImageProxyService
    {
        private PluginSettings _settings;
        private const string DefaultTemplate = "https://pixiv.shojo.cn/{pid}";

        public ImageProxyService(PluginSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// 更新设置
        /// </summary>
        public void UpdateSettings(PluginSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// 是否应该使用代理
        /// </summary>
        public bool ShouldUseProxy => _settings.UseImageProxy;

        /// <summary>
        /// 获取图片 URL（根据设置决定是否使用代理）
        /// </summary>
        /// <param name="originalUrl">原始 Pixiv 图片 URL</param>
        /// <param name="pid">作品 ID</param>
        /// <param name="pageIndex">页码索引（0-based）</param>
        /// <param name="isMultiPage">是否为多页作品</param>
        /// <returns>最终使用的图片 URL</returns>
        public string GetImageUrl(string originalUrl, long pid, int pageIndex = 0, bool isMultiPage = false)
        {
            if (!ShouldUseProxy || pid <= 0)
            {
                return originalUrl;
            }

            return GenerateProxyUrl(pid, pageIndex, isMultiPage);
        }

        /// <summary>
        /// 生成代理 URL
        /// </summary>
        /// <param name="pid">作品 ID</param>
        /// <param name="pageIndex">页码索引（0-based）</param>
        /// <param name="isMultiPage">是否为多页作品</param>
        /// <returns>代理 URL</returns>
        public string GenerateProxyUrl(long pid, int pageIndex = 0, bool isMultiPage = false)
        {
            var template = string.IsNullOrWhiteSpace(_settings.ImageProxyUrlTemplate) 
                ? DefaultTemplate 
                : _settings.ImageProxyUrlTemplate;

            // 替换 {pid} 占位符
            var url = template.Replace("{pid}", pid.ToString());

            // 处理多页作品
            if (isMultiPage && pageIndex > 0)
            {
                // 转换为 1-based 索引
                var oneBasedIndex = pageIndex + 1;

                if (template.Contains("{index}"))
                {
                    // 模板包含 {index}，直接替换
                    url = url.Replace("{index}", oneBasedIndex.ToString());
                }
                else
                {
                    // 模板不包含 {index}，在末尾追加
                    url = $"{url}-{oneBasedIndex}";
                }
            }
            else if (isMultiPage && pageIndex == 0)
            {
                // 多页作品的第一页，如果模板包含 {index}，替换为 1
                if (template.Contains("{index}"))
                {
                    url = url.Replace("{index}", "1");
                }
            }
            else
            {
                // 单页作品，移除 {index} 占位符（如果存在）
                url = url.Replace("-{index}", "").Replace("{index}", "");
            }

            // 如果模板不包含 {pid}，在末尾追加 PID
            if (!template.Contains("{pid}"))
            {
                url = url.TrimEnd('/') + "/" + pid;
            }

            return url;
        }

        /// <summary>
        /// 验证设置是否有效
        /// </summary>
        /// <returns>验证结果和错误信息</returns>
        public (bool IsValid, string? ErrorMessage) ValidateSettings()
        {
            if (_settings.UseImageProxy && string.IsNullOrWhiteSpace(_settings.ImageProxyUrlTemplate))
            {
                return (false, "启用图片反向代理时，URL 模板不能为空");
            }

            return (true, null);
        }
    }
}

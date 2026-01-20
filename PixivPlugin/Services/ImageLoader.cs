using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace PixivPlugin.Services
{
    /// <summary>
    /// 图片加载器，处理 Pixiv 防盗链，支持内存缓存和反向代理
    /// </summary>
    public class ImageLoader
    {
        private const string PixivReferer = "https://www.pixiv.net/";
        private const int MaxCacheSize = 100; // 最大缓存数量
        
        private HttpClient _httpClient;
        private HttpClient _proxyHttpClient; // 用于反向代理请求（不带 Referer）
        private string? _proxyUrl;
        private ImageProxyService? _imageProxyService;
        
        // 图片缓存（URL -> BitmapImage）
        private readonly ConcurrentDictionary<string, BitmapImage> _imageCache = new();
        // 缓存访问顺序（用于LRU淘汰）
        private readonly ConcurrentQueue<string> _cacheOrder = new();

        public ImageLoader()
        {
            _httpClient = CreateHttpClient(null, addReferer: true);
            _proxyHttpClient = CreateHttpClient(null, addReferer: false);
        }

        /// <summary>
        /// 设置图片反向代理服务
        /// </summary>
        public void SetImageProxyService(ImageProxyService? proxyService)
        {
            _imageProxyService = proxyService;
        }

        /// <summary>
        /// 设置代理
        /// </summary>
        public void SetProxy(string? proxyUrl)
        {
            _proxyUrl = proxyUrl;
            _httpClient.Dispose();
            _proxyHttpClient.Dispose();
            _httpClient = CreateHttpClient(proxyUrl, addReferer: true);
            _proxyHttpClient = CreateHttpClient(proxyUrl, addReferer: false);
        }

        private HttpClient CreateHttpClient(string? proxyUrl, bool addReferer = true)
        {
            HttpClientHandler handler;
            
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                try
                {
                    handler = new HttpClientHandler
                    {
                        Proxy = new WebProxy(proxyUrl),
                        UseProxy = true
                    };
                }
                catch (Exception)
                {
                    // 代理URL格式错误，使用默认handler
                    handler = new HttpClientHandler();
                }
            }
            else
            {
                handler = new HttpClientHandler();
            }

            var client = new HttpClient(handler);
            if (addReferer)
            {
                client.DefaultRequestHeaders.Add("Referer", PixivReferer);
            }
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.Timeout = TimeSpan.FromSeconds(60);
            
            return client;
        }

        /// <summary>
        /// 异步加载图片（带缓存，支持反向代理）
        /// </summary>
        /// <param name="url">原始图片 URL</param>
        /// <param name="pid">作品 ID（用于反向代理）</param>
        /// <param name="pageIndex">页码索引（0-based）</param>
        /// <param name="isMultiPage">是否为多页作品</param>
        public async Task<BitmapImage?> LoadImageAsync(string url, long pid = 0, int pageIndex = 0, bool isMultiPage = false)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // 确定实际使用的 URL 和 HttpClient
            var actualUrl = url;
            var useProxyClient = false;

            if (_imageProxyService is not null && _imageProxyService.ShouldUseProxy && pid > 0)
            {
                actualUrl = _imageProxyService.GetImageUrl(url, pid, pageIndex, isMultiPage);
                useProxyClient = true; // 使用反向代理时不需要 Referer
            }

            // 检查缓存
            if (_imageCache.TryGetValue(actualUrl, out var cachedImage))
            {
                return cachedImage;
            }

            try
            {
                var client = useProxyClient ? _proxyHttpClient : _httpClient;
                var bytes = await client.GetByteArrayAsync(actualUrl);
                var image = BytesToBitmapImage(bytes);
                
                // 添加到缓存
                if (image is not null)
                {
                    AddToCache(actualUrl, image);
                }
                
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }
        
        /// <summary>
        /// 添加图片到缓存，超出限制时淘汰最旧的
        /// </summary>
        private void AddToCache(string url, BitmapImage image)
        {
            // 如果已存在，不重复添加
            if (_imageCache.ContainsKey(url))
                return;
                
            // 淘汰旧缓存
            while (_imageCache.Count >= MaxCacheSize && _cacheOrder.TryDequeue(out var oldUrl))
            {
                _imageCache.TryRemove(oldUrl, out _);
            }
            
            // 添加新缓存
            if (_imageCache.TryAdd(url, image))
            {
                _cacheOrder.Enqueue(url);
            }
        }
        
        /// <summary>
        /// 清空缓存
        /// </summary>
        public void ClearCache()
        {
            _imageCache.Clear();
            while (_cacheOrder.TryDequeue(out _)) { }
        }

        /// <summary>
        /// 异步下载图片到文件（支持反向代理）
        /// </summary>
        /// <param name="url">原始图片 URL</param>
        /// <param name="savePath">保存路径</param>
        /// <param name="progress">下载进度</param>
        /// <param name="pid">作品 ID（用于反向代理）</param>
        /// <param name="pageIndex">页码索引（0-based）</param>
        /// <param name="isMultiPage">是否为多页作品</param>
        public async Task<bool> DownloadImageAsync(string url, string savePath, IProgress<int>? progress = null, long pid = 0, int pageIndex = 0, bool isMultiPage = false)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(savePath))
                return false;

            // 确定实际使用的 URL 和 HttpClient
            var actualUrl = url;
            var useProxyClient = false;

            if (_imageProxyService is not null && _imageProxyService.ShouldUseProxy && pid > 0)
            {
                actualUrl = _imageProxyService.GetImageUrl(url, pid, pageIndex, isMultiPage);
                useProxyClient = true;
            }

            try
            {
                var client = useProxyClient ? _proxyHttpClient : _httpClient;
                using var response = await client.GetAsync(actualUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                // 确保目录存在
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0 && progress is not null)
                    {
                        var percentage = (int)((downloadedBytes * 100) / totalBytes);
                        progress.Report(percentage);
                    }
                }

                progress?.Report(100);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 获取默认下载文件名
        /// </summary>
        public static string GetDefaultFileName(string url)
        {
            try
            {
                var uri = new Uri(url);
                return Path.GetFileName(uri.LocalPath);
            }
            catch
            {
                return $"pixiv_{DateTime.Now:yyyyMMddHHmmss}.jpg";
            }
        }

        private static BitmapImage BytesToBitmapImage(byte[] bytes)
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        public void Dispose()
        {
            ClearCache();
            _httpClient.Dispose();
            _proxyHttpClient.Dispose();
        }
    }
}

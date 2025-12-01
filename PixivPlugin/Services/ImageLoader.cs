using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace PixivPlugin.Services
{
    /// <summary>
    /// 图片加载器，处理 Pixiv 防盗链，支持内存缓存
    /// </summary>
    public class ImageLoader
    {
        private const string PixivReferer = "https://www.pixiv.net/";
        private const int MaxCacheSize = 100; // 最大缓存数量
        
        private HttpClient _httpClient;
        private string? _proxyUrl;
        
        // 图片缓存（URL -> BitmapImage）
        private readonly ConcurrentDictionary<string, BitmapImage> _imageCache = new();
        // 缓存访问顺序（用于LRU淘汰）
        private readonly ConcurrentQueue<string> _cacheOrder = new();

        public ImageLoader()
        {
            _httpClient = CreateHttpClient(null);
        }

        /// <summary>
        /// 设置代理
        /// </summary>
        public void SetProxy(string? proxyUrl)
        {
            _proxyUrl = proxyUrl;
            _httpClient.Dispose();
            _httpClient = CreateHttpClient(proxyUrl);
        }

        private HttpClient CreateHttpClient(string? proxyUrl)
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
            client.DefaultRequestHeaders.Add("Referer", PixivReferer);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.Timeout = TimeSpan.FromSeconds(60);
            
            return client;
        }

        /// <summary>
        /// 异步加载图片（带缓存）
        /// </summary>
        public async Task<BitmapImage?> LoadImageAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // 检查缓存
            if (_imageCache.TryGetValue(url, out var cachedImage))
            {
                return cachedImage;
            }

            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                var image = BytesToBitmapImage(bytes);
                
                // 添加到缓存
                if (image != null)
                {
                    AddToCache(url, image);
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
        /// 异步下载图片到文件
        /// </summary>
        public async Task<bool> DownloadImageAsync(string url, string savePath, IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(savePath))
                return false;

            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
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

                    if (totalBytes > 0 && progress != null)
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
        }
    }
}

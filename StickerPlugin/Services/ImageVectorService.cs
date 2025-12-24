using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using StickerPlugin.Models;

namespace StickerPlugin.Services
{
    /// <summary>
    /// Image Vector Service API 客户端
    /// </summary>
    public class ImageVectorService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string? _apiKey;
        private readonly Action<string>? _logger;

        // 标签缓存
        private List<string>? _cachedTags;
        private DateTime _cacheTime = DateTime.MinValue;

        // 最后一次错误信息
        public string? LastError { get; private set; }

        public ImageVectorService(string baseUrl, string? apiKey = null, Action<string>? logger = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
            _logger = logger;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // 设置 API Key 头
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            }
        }

        private void Log(string message)
        {
            _logger?.Invoke($"[ImageVectorService] {message}");
        }

        /// <summary>
        /// 健康检查
        /// </summary>
        public async Task<bool> HealthCheckAsync()
        {
            try
            {
                var response = await PostAsync<object, HealthResponse>("/api/health", new { });
                return response?.Success ?? false;
            }
            catch (Exception ex)
            {
                Log($"HealthCheck failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public async Task<StatsResponse?> GetStatsAsync()
        {
            try
            {
                return await PostAsync<object, StatsResponse>("/api/stats", new { });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 搜索表情包
        /// </summary>
        public async Task<SearchResponse?> SearchAsync(string query, int limit = 1, double minScore = 0.2)
        {
            try
            {
                var request = new SearchRequest
                {
                    Query = query,
                    Limit = limit,
                    MinScore = minScore
                };
                return await PostAsync<SearchRequest, SearchResponse>("/api/search", request);
            }
            catch (Exception ex)
            {
                Log($"Search failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取所有唯一标签
        /// </summary>
        public async Task<TagsResponse?> GetTagsAsync()
        {
            try
            {
                return await PostAsync<object, TagsResponse>("/api/tags", new { });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取缓存的标签（带缓存时长检查）
        /// </summary>
        public async Task<List<string>> GetCachedTagsAsync(TimeSpan cacheDuration)
        {
            // 检查缓存是否有效
            if (_cachedTags != null && DateTime.Now - _cacheTime < cacheDuration)
            {
                return _cachedTags;
            }

            // 从服务获取新标签
            var response = await GetTagsAsync();
            if (response?.Success == true && response.Tags != null)
            {
                _cachedTags = response.Tags;
                _cacheTime = DateTime.Now;
                return _cachedTags;
            }

            // 如果获取失败但有旧缓存，返回旧缓存
            if (_cachedTags != null)
            {
                return _cachedTags;
            }

            return new List<string>();
        }

        /// <summary>
        /// 使缓存失效
        /// </summary>
        public void InvalidateCache()
        {
            _cachedTags = null;
            _cacheTime = DateTime.MinValue;
        }

        /// <summary>
        /// 通用 POST 请求方法
        /// </summary>
        private async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest request)
            where TResponse : class
        {
            try
            {
                var url = _baseUrl + endpoint;
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<TResponse>(responseJson);
                }

                // 记录非成功状态码
                LastError = $"HTTP {(int)response.StatusCode} from {endpoint}";
                Log(LastError);
                return null;
            }
            catch (TaskCanceledException)
            {
                LastError = $"Request timeout for {endpoint}";
                Log(LastError);
                return null;
            }
            catch (HttpRequestException ex)
            {
                LastError = $"Network error: {ex.Message}";
                Log(LastError);
                return null;
            }
            catch (Exception ex)
            {
                LastError = $"Error: {ex.Message}";
                Log(LastError);
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

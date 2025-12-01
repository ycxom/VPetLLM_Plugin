using System.Net.Http;
using Newtonsoft.Json;
using PixivPlugin.Models;

namespace PixivPlugin.Services
{
    /// <summary>
    /// Pixiv API 服务
    /// </summary>
    public class PixivApiService
    {
        private const string BaseUrl = "https://ai.ycxom.top:6523/api";
        private const string ApiKey = "pk_8a73dbf63a8d7c1535946e69d6b789fd";

        private readonly HttpClient _httpClient;
        private readonly Random _random = new();

        public PixivApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// 设置请求超时时间
        /// </summary>
        public void SetTimeout(int seconds)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// 验证搜索关键词
        /// </summary>
        public static bool ValidateKeyword(string? keyword)
        {
            return !string.IsNullOrWhiteSpace(keyword);
        }

        /// <summary>
        /// 搜索图片
        /// </summary>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="page">页码（从1开始），每页30条</param>
        public async Task<PixivResponse?> SearchAsync(string keyword, int page = 1)
        {
            if (!ValidateKeyword(keyword))
            {
                return null;
            }

            try
            {
                // 页码转换为 offset：第1页 offset=30，第2页 offset=60，以此类推
                var offset = page * 30;
                var url = $"{BaseUrl}/search?word={Uri.EscapeDataString(keyword)}&offset={offset}";
                var response = await _httpClient.GetStringAsync(url);
                return JsonConvert.DeserializeObject<PixivResponse>(response);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 获取排行榜
        /// </summary>
        public async Task<PixivResponse?> GetRankingAsync(string mode = "day")
        {
            try
            {
                var url = $"{BaseUrl}/ranking?mode={mode}";
                var response = await _httpClient.GetStringAsync(url);
                return JsonConvert.DeserializeObject<PixivResponse>(response);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 从排行榜随机获取一张图片
        /// </summary>
        public async Task<PixivIllust?> GetRandomRankingImageAsync(string mode = "day")
        {
            var response = await GetRankingAsync(mode);
            if (response?.Illusts == null || response.Illusts.Count == 0)
            {
                return null;
            }

            return SelectRandom(response.Illusts);
        }

        /// <summary>
        /// 从列表中随机选择一个元素
        /// </summary>
        public T SelectRandom<T>(IList<T> list)
        {
            if (list == null || list.Count == 0)
            {
                throw new ArgumentException("List cannot be null or empty", nameof(list));
            }

            var index = _random.Next(list.Count);
            return list[index];
        }
    }
}

using System.Net.Http;
using Newtonsoft.Json;
using PixivPlugin.Models;

namespace PixivPlugin.Services
{
    public class PixivApiService
    {
        // git有开源项目，可用自己部署的
        private const string BaseUrl = "https://ai.ycxom.top:8025/pixiv";
        private const string ApiKey = "pk_8a73dbf63a8d7c1535946e69d6b789fd";

        private readonly HttpClient _httpClient;
        private readonly Random _random = new();
        private readonly VPetLLM.VPetLLM _host;

        public PixivApiService(VPetLLM.VPetLLM host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            // 中转服务器可直连，显式禁用代理（避免 HttpClientHandler 默认静默走系统代理）
            _httpClient = new HttpClient(new HttpClientHandler { UseProxy = false, Proxy = null });
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public void SetTimeout(int seconds)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(seconds);
        }

        public static bool ValidateKeyword(string? keyword)
        {
            return !string.IsNullOrWhiteSpace(keyword);
        }

        private async Task<T?> SendRequestAsync<T>(string url) where T : class
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _host.SendAuthenticatedServiceRequestAsync(_httpClient, request);
                if (!response.IsSuccessStatusCode) return null;
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(content);
            }
            catch { return null; }
        }

        public async Task<PixivResponse?> SearchAsync(string keyword, int page = 1)
        {
            if (!ValidateKeyword(keyword)) return null;
            string url = page <= 1 
                ? $"{BaseUrl}/search?word={Uri.EscapeDataString(keyword)}"
                : $"{BaseUrl}/search?word={Uri.EscapeDataString(keyword)}&offset={(page - 1) * 30}";
            return await SendRequestAsync<PixivResponse>(url);
        }

        public async Task<PixivResponse?> GetRankingAsync(string mode = "day")
        {
            return await SendRequestAsync<PixivResponse>($"{BaseUrl}/ranking?mode={mode}");
        }

        public async Task<PixivIllust?> GetRandomRankingImageAsync(string mode = "day")
        {
            var response = await GetRankingAsync(mode);
            if (response?.Illusts is null || response.Illusts.Count == 0) return null;
            return SelectRandom(response.Illusts);
        }

        public T SelectRandom<T>(IList<T> list)
        {
            if (list is null || list.Count == 0)
                throw new ArgumentException("List cannot be null or empty", nameof(list));
            return list[_random.Next(list.Count)];
        }

    }
}

using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using StickerPlugin.Models;

namespace StickerPlugin.Services
{
    public class ImageVectorService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string? _apiKey;
        private readonly Action<string>? _logger;
        private readonly ulong _steamId;
        private readonly Func<Task<int>>? _getAuthKey;
        private readonly bool _useBuiltInCredentials;
        private List<string>? _cachedTags;
        private DateTime _cacheTime = DateTime.MinValue;
        public string? LastError { get; private set; }

        public ImageVectorService(string baseUrl, string? apiKey = null, Action<string>? logger = null, 
            ulong steamId = 0, Func<Task<int>>? getAuthKey = null, bool useBuiltInCredentials = true)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
            _logger = logger;
            _steamId = steamId;
            _getAuthKey = getAuthKey;
            _useBuiltInCredentials = useBuiltInCredentials;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (!string.IsNullOrEmpty(_apiKey)) _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
        }

        private void Log(string message) => _logger?.Invoke($"[ImageVectorService] {message}");

        public async Task<bool> HealthCheckAsync()
        {
            try { return (await PostAsync<object, HealthResponse>("/api/health", new { }))?.Success ?? false; }
            catch { return false; }
        }

        public async Task<StatsResponse?> GetStatsAsync()
        {
            try { return await PostAsync<object, StatsResponse>("/api/stats", new { }); }
            catch { return null; }
        }

        public async Task<SearchResponse?> SearchAsync(string query, int limit = 1, double minScore = 0.2)
        {
            try { return await PostAsync<SearchRequest, SearchResponse>("/api/search", new SearchRequest { Query = query, Limit = limit, MinScore = minScore }); }
            catch { return null; }
        }

        public async Task<TagsResponse?> GetTagsAsync()
        {
            try { return await PostAsync<object, TagsResponse>("/api/tags", new { }); }
            catch { return null; }
        }

        public async Task<List<string>> GetCachedTagsAsync(TimeSpan cacheDuration)
        {
            if (_cachedTags != null && DateTime.Now - _cacheTime < cacheDuration) return _cachedTags;
            var response = await GetTagsAsync();
            if (response?.Success == true && response.Tags != null) { _cachedTags = response.Tags; _cacheTime = DateTime.Now; return _cachedTags; }
            return _cachedTags ?? new List<string>();
        }

        public void InvalidateCache() { _cachedTags = null; _cacheTime = DateTime.MinValue; }


        private async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest request) where TResponse : class
        {
            try
            {
                var url = _baseUrl + endpoint;
                var json = JsonConvert.SerializeObject(request);
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
                requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (_useBuiltInCredentials)
                {
                    var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    int ck = 0;
                    if (_getAuthKey != null) { try { ck = await _getAuthKey(); } catch { } }
                    requestMessage.Headers.Add("X-Cache-Token", E(_steamId.ToString(), ts));
                    requestMessage.Headers.Add("X-Request-Signature", E(M(), ts));
                    requestMessage.Headers.Add("X-Check-Key", E(ck.ToString(), ts));
                    requestMessage.Headers.Add("X-Trace-Id", T(ts));
                }
                var response = await _httpClient.SendAsync(requestMessage);
                var responseJson = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return JsonConvert.DeserializeObject<TResponse>(responseJson);
                LastError = $"HTTP {(int)response.StatusCode}";
                return null;
            }
            catch (TaskCanceledException) { LastError = "Timeout"; return null; }
            catch (HttpRequestException ex) { LastError = ex.Message; return null; }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }

        public void Dispose() => _httpClient?.Dispose();

        private static string M()
        {
            var a = 0x1A2B ^ 0x1A2B;
            var p = new[] { (char)(51+a), (char)(53+a), (char)(54+a), (char)(49+a), (char)(57+a), (char)(51+a), (char)(50+a), (char)(52+a), (char)(49+a), (char)(53+a) };
            return new string(p);
        }

        private static long F(long t)
        {
            var d = t.ToString();
            long f = 0;
            for (int i = 0; i < d.Length; i++) f += (d[i] - '0') * (i + 1);
            return f % 60;
        }

        private static long O(long t) => (t ^ (F(t) * 0x5A5A)) + F(t);

        private static string E(string p, long t)
        {
            try
            {
                var o = O(t);
                using var s = SHA256.Create();
                var k = s.ComputeHash(Encoding.UTF8.GetBytes(o.ToString() + "VPetLLM_"));
                using var m = MD5.Create();
                var iv = m.ComputeHash(Encoding.UTF8.GetBytes(t.ToString()));
                using var a = Aes.Create();
                a.Key = k; a.IV = iv; a.Mode = CipherMode.CBC; a.Padding = PaddingMode.PKCS7;
                using var e = a.CreateEncryptor();
                var b = Encoding.UTF8.GetBytes(p);
                return Convert.ToBase64String(e.TransformFinalBlock(b, 0, b.Length));
            }
            catch { return p; }
        }

        private static string T(long t)
        {
            try
            {
                var r = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(r);
                var c = new byte[20];
                Array.Copy(r, 0, c, 0, 16);
                Array.Copy(BitConverter.GetBytes((int)(t % 10000)), 0, c, 16, 4);
                var x = (byte)(O(t) & 0xFF);
                for (int i = 0; i < 20; i++) c[i] ^= x;
                return Convert.ToBase64String(c);
            }
            catch { return Convert.ToBase64String(Guid.NewGuid().ToByteArray()); }
        }
    }
}

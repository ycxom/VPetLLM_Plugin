using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
        private readonly ulong _steamId;
        private readonly Func<Task<int>>? _getAuthKey;

        public PixivApiService(ulong steamId = 0, Func<Task<int>>? getAuthKey = null)
        {
            _steamId = steamId;
            _getAuthKey = getAuthKey;
            _httpClient = new HttpClient();
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
                await AddAuthHeadersAsync(request);
                var response = await _httpClient.SendAsync(request);
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
            if (response?.Illusts == null || response.Illusts.Count == 0) return null;
            return SelectRandom(response.Illusts);
        }

        public T SelectRandom<T>(IList<T> list)
        {
            if (list == null || list.Count == 0)
                throw new ArgumentException("List cannot be null or empty", nameof(list));
            return list[_random.Next(list.Count)];
        }


        private async Task AddAuthHeadersAsync(HttpRequestMessage request)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int ck = 0;
            if (_getAuthKey != null) { try { ck = await _getAuthKey(); } catch { } }
            request.Headers.Add("X-Cache-Token", E(_steamId.ToString(), ts));
            request.Headers.Add("X-Request-Signature", E(M(), ts));
            request.Headers.Add("X-Check-Key", E(ck.ToString(), ts));
            request.Headers.Add("X-Trace-Id", T(ts));
        }

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

using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WebSearchPlugin
{
    public class ApiContentFetcher : IContentFetcher
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _bearerToken;
        private readonly IContentFetcher? _fallbackFetcher;
        private readonly bool _enableFallback;
        private readonly ulong _steamId;
        private readonly Func<Task<int>>? _getAuthKey;
        private readonly bool _useBuiltInCredentials;

        public ApiContentFetcher(HttpClient httpClient, string apiUrl, string bearerToken,
            ulong steamId = 0, Func<Task<int>>? getAuthKey = null,
            IContentFetcher? fallbackFetcher = null, bool enableFallback = true, bool useBuiltInCredentials = true)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiUrl = apiUrl ?? throw new ArgumentNullException(nameof(apiUrl));
            _bearerToken = bearerToken ?? "";
            _steamId = steamId;
            _getAuthKey = getAuthKey;
            _fallbackFetcher = fallbackFetcher;
            _enableFallback = enableFallback;
            _useBuiltInCredentials = useBuiltInCredentials;
        }

        public async Task<FetchResult> FetchAsync(string url)
        {
            string apiError;

            // 内置凭证走服务端的签名鉴权，而签名鉴权要求 steam_id 为正整数。
            // 非 Steam 用户 SteamID 恒为 0，请求必定 403，跳过以免每次白等一轮网络往返。
            if (_useBuiltInCredentials && _steamId == 0)
            {
                apiError = "内置凭证需要 Steam 版 VPet（当前 SteamID 为 0），API 模式不可用";
            }
            else
            {
                apiError = "";
                try
                {
                    var result = await FetchViaApiAsync(url);
                    if (result.Success) return result;
                    apiError = result.ErrorMessage;
                }
                catch (Exception ex) { apiError = ex.Message; }
            }

            // 失败原因此前被直接丢弃，导致日志里只看得到「已降级」而无从排查
            VPetLLM.Utils.System.Logger.Log($"ApiContentFetcher: API 模式失败: {apiError}");

            if (_enableFallback && _fallbackFetcher is not null)
            {
                var fallbackResult = await _fallbackFetcher.FetchAsync(url);
                if (fallbackResult.Success) { fallbackResult.UsedFallback = true; return fallbackResult; }
                return new FetchResult { Success = false, Mode = "API+Local", UsedFallback = true,
                    ErrorMessage = $"API: {apiError}\nLocal: {fallbackResult.ErrorMessage}" };
            }
            return new FetchResult { Success = false, Mode = "API", ErrorMessage = apiError };
        }

        private async Task<FetchResult> FetchViaApiAsync(string url)
        {
            // 后端 /extract 为 POST，参数从 JSON body 读取（不接受 query string）
            using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
            var payload = JsonConvert.SerializeObject(new { url = url, output_format = "markdown" });
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(_bearerToken))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);
            if (_useBuiltInCredentials) await AddAuthHeadersAsync(request);
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new FetchResult { Success = false, Mode = "API", ErrorMessage = await DescribeErrorAsync(response) };
            var json = await response.Content.ReadAsStringAsync();
            ApiResponse? apiResponse;
            try { apiResponse = JsonConvert.DeserializeObject<ApiResponse>(json); }
            catch { return new FetchResult { Success = false, Mode = "API", ErrorMessage = "JSON parse error" }; }
            if (apiResponse is null || !apiResponse.Success)
                return new FetchResult { Success = false, Mode = "API", ErrorMessage = "API returned false" };
            return new FetchResult { Success = true, Content = apiResponse.Content, Mode = "API", UsedFallback = false };
        }

        /// <summary>
        /// 提取错误详情：后端返回 {"error":"..."}，鉴权代理返回 {"success":false,"message":"..."}
        /// </summary>
        private static async Task<string> DescribeErrorAsync(HttpResponseMessage response)
        {
            var status = $"HTTP {(int)response.StatusCode}";
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body)) return status;
                var detail = JsonConvert.DeserializeObject<ApiErrorResponse>(body);
                var message = detail?.Error ?? detail?.Message;
                return string.IsNullOrWhiteSpace(message) ? status : $"{status}: {message}";
            }
            catch { return status; }
        }


        private async Task AddAuthHeadersAsync(HttpRequestMessage request)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int ck = 0;
            if (_getAuthKey is not null) { try { ck = await _getAuthKey(); } catch { } }
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

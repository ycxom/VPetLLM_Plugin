using System;
using System.Net.Http;
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
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>? _authenticatedSender;
        private readonly bool _useBuiltInCredentials;

        public ApiContentFetcher(HttpClient httpClient, string apiUrl, string bearerToken,
            Func<HttpRequestMessage, Task<HttpResponseMessage>>? authenticatedSender = null,
            IContentFetcher? fallbackFetcher = null, bool enableFallback = true, bool useBuiltInCredentials = true)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiUrl = apiUrl ?? throw new ArgumentNullException(nameof(apiUrl));
            _bearerToken = bearerToken ?? "";
            _authenticatedSender = authenticatedSender;
            _fallbackFetcher = fallbackFetcher;
            _enableFallback = enableFallback;
            _useBuiltInCredentials = useBuiltInCredentials;
        }

        public async Task<FetchResult> FetchAsync(string url)
        {
            string apiError;

            // 内置服务必须由新版 VPetLLM 宿主完成身份采集、鉴权和应用层加密。
            // 插件本身不读取或传递 SteamID、CheckKey、设备信息及协议密钥。
            if (_useBuiltInCredentials && _authenticatedSender is null)
            {
                apiError = "当前 VPetLLM 版本不支持内置服务安全通讯，请先更新主 MOD";
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
            using var response = _useBuiltInCredentials
                ? await _authenticatedSender!(request)
                : await _httpClient.SendAsync(request);
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


    }
}

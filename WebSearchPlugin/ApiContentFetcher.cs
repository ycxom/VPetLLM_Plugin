using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WebSearchPlugin
{
    /// <summary>
    /// API 内容抓取器，使用远程 API 获取网页内容
    /// </summary>
    public class ApiContentFetcher : IContentFetcher
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _bearerToken;
        private readonly IContentFetcher? _fallbackFetcher;
        private readonly bool _enableFallback;

        public ApiContentFetcher(
            HttpClient httpClient,
            string apiUrl,
            string bearerToken,
            IContentFetcher? fallbackFetcher = null,
            bool enableFallback = true)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiUrl = apiUrl ?? throw new ArgumentNullException(nameof(apiUrl));
            _bearerToken = bearerToken ?? "";
            _fallbackFetcher = fallbackFetcher;
            _enableFallback = enableFallback;
        }

        public async Task<FetchResult> FetchAsync(string url)
        {
            string apiError = "";
            
            try
            {
                VPetLLM.Utils.Logger.Log($"ApiContentFetcher: Fetching {url} via API");
                
                var result = await FetchViaApiAsync(url);
                if (result.Success)
                {
                    return result;
                }
                
                apiError = result.ErrorMessage;
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"ApiContentFetcher API error: {ex.Message}");
                apiError = $"API 请求异常: {ex.Message}";
            }

            // API 失败，尝试降级到本地模式
            if (_enableFallback && _fallbackFetcher != null)
            {
                VPetLLM.Utils.Logger.Log($"ApiContentFetcher: API failed ({apiError}), falling back to local mode");
                
                var fallbackResult = await _fallbackFetcher.FetchAsync(url);
                
                if (fallbackResult.Success)
                {
                    fallbackResult.UsedFallback = true;
                    VPetLLM.Utils.Logger.Log("ApiContentFetcher: Fallback to local mode succeeded");
                    return fallbackResult;
                }
                
                // 两种模式都失败
                VPetLLM.Utils.Logger.Log($"ApiContentFetcher: Both API and local mode failed");
                return new FetchResult
                {
                    Success = false,
                    Mode = "API+Local",
                    UsedFallback = true,
                    ErrorMessage = $"API 失败: {apiError}\n本地模式失败: {fallbackResult.ErrorMessage}"
                };
            }

            // 没有启用降级或没有降级抓取器
            return new FetchResult
            {
                Success = false,
                Mode = "API",
                ErrorMessage = apiError
            };
        }

        private async Task<FetchResult> FetchViaApiAsync(string url)
        {
            // 构建 API 请求 URL
            var encodedUrl = Uri.EscapeDataString(url);
            var requestUrl = $"{_apiUrl}?url={encodedUrl}&output_format=markdown";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            
            // 添加 Bearer Token
            if (!string.IsNullOrEmpty(_bearerToken))
            {
                request.Headers.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);
            }

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                return new FetchResult
                {
                    Success = false,
                    Mode = "API",
                    ErrorMessage = $"API 返回错误状态码: {(int)response.StatusCode} {response.ReasonPhrase}"
                };
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            
            ApiResponse? apiResponse;
            try
            {
                apiResponse = JsonConvert.DeserializeObject<ApiResponse>(jsonContent);
            }
            catch (JsonException ex)
            {
                return new FetchResult
                {
                    Success = false,
                    Mode = "API",
                    ErrorMessage = $"API 响应 JSON 解析失败: {ex.Message}"
                };
            }

            if (apiResponse == null)
            {
                return new FetchResult
                {
                    Success = false,
                    Mode = "API",
                    ErrorMessage = "API 响应为空"
                };
            }

            if (!apiResponse.Success)
            {
                return new FetchResult
                {
                    Success = false,
                    Mode = "API",
                    ErrorMessage = "API 返回 success=false"
                };
            }

            return new FetchResult
            {
                Success = true,
                Content = apiResponse.Content,
                Mode = "API",
                UsedFallback = false
            };
        }
    }
}

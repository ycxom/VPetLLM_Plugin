using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WeatherPlugin.Utils
{
    /// <summary>
    /// HTTP 请求帮助类
    /// </summary>
    public static class HttpHelper
    {
        private static readonly HttpClient _client = new HttpClient();

        /// <summary>
        /// 发送带参数的 GET 请求
        /// </summary>
        /// <param name="url">请求的 URL</param>
        /// <param name="parameter">请求参数（例如: param=value）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应内容</returns>
        public static async Task<string> SendGetRequestAsync(string url, string parameter, CancellationToken cancellationToken)
        {
            try
            {
                var fullUrl = string.IsNullOrWhiteSpace(parameter) ? url : $"{url}?{parameter}";

                HttpResponseMessage response = await _client.GetAsync(fullUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
                else
                {
                    throw new HttpRequestException($"请求失败，状态码: {response.StatusCode}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("网络请求超时，请稍后重试");
            }
            catch (HttpRequestException ex)
            {
                throw new HttpRequestException($"网络请求失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 发送带超时的 GET 请求
        /// </summary>
        /// <param name="url">请求的 URL</param>
        /// <param name="parameter">请求参数</param>
        /// <param name="timeoutSeconds">超时时间（秒）</param>
        /// <returns>响应内容</returns>
        public static async Task<string> SendGetRequestAsync(string url, string parameter, int timeoutSeconds = 5)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            return await SendGetRequestAsync(url, parameter, cts.Token);
        }
    }
}

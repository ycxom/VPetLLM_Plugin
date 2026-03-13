using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace OneBotPlugin
{
    public class OneBotHttpClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly Action<string> _log;
        private bool _disposed;

        public OneBotHttpClient(string baseUrl, string accessToken, Action<string> log)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _log = log;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrEmpty(accessToken))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        public async Task<OneBotActionResponse> SendActionAsync(string action, object? parameters = null)
        {
            try
            {
                var url = $"{_baseUrl}/{action}";
                var json = parameters is not null ? JsonConvert.SerializeObject(parameters) : "{}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeObject<OneBotActionResponse>(body)
                    ?? new OneBotActionResponse { Status = "parse_error", RetCode = -3 };
            }
            catch (Exception ex)
            {
                _log($"OneBot HTTP: Action '{action}' failed: {ex.Message}");
                return new OneBotActionResponse { Status = "failed", RetCode = -1 };
            }
        }

        public async Task<bool> SendMessageAsync(long? userId, long? groupId, string text, string? image = null)
        {
            object message;
            if (!string.IsNullOrEmpty(image) && !string.IsNullOrEmpty(text))
                message = CQCodeParser.ParseTextWithOptionalImage(text, image);
            else if (!string.IsNullOrEmpty(image))
                message = CQCodeParser.BuildImageMessage(image);
            else
                message = CQCodeParser.ParseTextToSegments(text);

            var msgParams = new SendMsgParams { Message = message };

            if (groupId.HasValue && groupId.Value > 0)
            {
                msgParams.MessageType = "group";
                msgParams.GroupId = groupId;
            }
            else if (userId.HasValue && userId.Value > 0)
            {
                msgParams.MessageType = "private";
                msgParams.UserId = userId;
            }
            else
            {
                return false;
            }

            var resp = await SendActionAsync("send_msg", msgParams);
            return resp.IsOk;
        }

        public async Task<bool> SendReplyAsync(OneBotMessageContext context, string text, bool withAt)
        {
            object message;
            if (context.IsGroup && withAt)
                message = CQCodeParser.BuildAtAndTextMessage(context.UserId, text);
            else
                message = CQCodeParser.BuildTextMessage(text);

            var msgParams = new SendMsgParams { Message = message };

            if (context.IsGroup)
            {
                msgParams.MessageType = "group";
                msgParams.GroupId = context.GroupId;
            }
            else
            {
                msgParams.MessageType = "private";
                msgParams.UserId = context.UserId;
            }

            var resp = await SendActionAsync("send_msg", msgParams);
            return resp.IsOk;
        }

        public async Task<GetLoginInfoResult?> GetLoginInfoAsync()
        {
            var resp = await SendActionAsync("get_login_info");
            if (resp.IsOk && resp.Data is not null)
                return resp.Data.ToObject<GetLoginInfoResult>();
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }
    }
}

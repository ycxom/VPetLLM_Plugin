using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OneBotPlugin
{
    public class OneBotClient : IDisposable
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly string _url;
        private readonly string _accessToken;
        private readonly Action<string> _log;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<OneBotActionResponse>> _pendingRequests = new();
        private Task? _receiveTask;
        private bool _disposed;
        private int _reconnectAttempts;
        private const int MaxReconnectAttempts = 10;
        private const int ReconnectBaseDelayMs = 2000;

        public event EventHandler<OneBotEvent>? OnEvent;
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public OneBotClient(string url, string accessToken, Action<string> log)
        {
            _url = url;
            _accessToken = accessToken;
            _log = log;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await ConnectInternalAsync();
            }
            catch (Exception ex)
            {
                _log($"OneBot Client: Initial connection failed: {ex.Message}, will retry in background");
            }

            // Always start receive loop — it handles reconnection when not connected
            _receiveTask = ReceiveLoopAsync(_cts.Token);
        }

        private async Task ConnectInternalAsync()
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();

            if (!string.IsNullOrEmpty(_accessToken))
                _ws.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken}");

            _log($"OneBot Client: Connecting to {_url}");
            await _ws.ConnectAsync(new Uri(_url), _cts!.Token);
            _reconnectAttempts = 0;
            _log("OneBot Client: Connected");
        }

        public async Task DisconnectAsync()
        {
            _cts?.Cancel();

            if (_ws?.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Plugin unloading", CancellationToken.None);
                }
                catch { }
            }

            if (_receiveTask is not null)
            {
                try { await _receiveTask; } catch { }
            }
        }

        public async Task<OneBotActionResponse> SendActionAsync(string action, object? parameters = null, int timeoutMs = 10000)
        {
            if (_ws?.State != WebSocketState.Open)
                return new OneBotActionResponse { Status = "failed", RetCode = -1 };

            var echo = Guid.NewGuid().ToString("N")[..8];
            var request = new OneBotActionRequest
            {
                Action = action,
                Params = parameters,
                Echo = echo
            };

            var tcs = new TaskCompletionSource<OneBotActionResponse>();
            _pendingRequests[echo] = tcs;

            try
            {
                var json = JsonConvert.SerializeObject(request);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);

                using var cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => tcs.TrySetResult(new OneBotActionResponse { Status = "timeout", RetCode = -2, Echo = echo }));

                return await tcs.Task;
            }
            finally
            {
                _pendingRequests.TryRemove(echo, out _);
            }
        }

        public async Task<bool> SendMessageAsync(long? userId, long? groupId, string text, string? image = null)
        {
            object message;
            if (!string.IsNullOrEmpty(image) && !string.IsNullOrEmpty(text))
                message = CQCodeParser.BuildTextAndImageMessage(text, image);
            else if (!string.IsNullOrEmpty(image))
                message = CQCodeParser.BuildImageMessage(image);
            else
                message = CQCodeParser.BuildTextMessage(text);

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

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_ws?.State != WebSocketState.Open)
                    {
                        if (!await TryReconnectAsync(ct))
                            break;
                        continue;
                    }

                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _log("OneBot Client: Server closed connection");
                        if (!await TryReconnectAsync(ct))
                            break;
                        continue;
                    }

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    HandleMessage(json);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    _log($"OneBot Client: WebSocket error: {ex.Message}");
                    if (!await TryReconnectAsync(ct))
                        break;
                }
                catch (Exception ex)
                {
                    _log($"OneBot Client: Receive error: {ex.Message}");
                }
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                var jObj = JObject.Parse(json);

                // Check if this is a response to a pending request
                var echo = jObj.Value<string>("echo");
                if (!string.IsNullOrEmpty(echo) && _pendingRequests.TryRemove(echo, out var tcs))
                {
                    var response = jObj.ToObject<OneBotActionResponse>() ?? new OneBotActionResponse { Status = "parse_error", RetCode = -3 };
                    tcs.TrySetResult(response);
                    return;
                }

                // Otherwise it's an event
                var evt = jObj.ToObject<OneBotEvent>();
                if (evt is not null)
                    OnEvent?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                _log($"OneBot Client: Parse error: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if reconnected or still retrying, false if gave up.
        /// </summary>
        private async Task<bool> TryReconnectAsync(CancellationToken ct)
        {
            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                _log($"OneBot Client: Max reconnect attempts ({MaxReconnectAttempts}) reached, giving up");
                return false;
            }

            _reconnectAttempts++;
            var delay = Math.Min(ReconnectBaseDelayMs * _reconnectAttempts, 30000);
            _log($"OneBot Client: Reconnecting in {delay}ms (attempt {_reconnectAttempts})");

            try
            {
                await Task.Delay(delay, ct);
                await ConnectInternalAsync();
                _log("OneBot Client: Reconnected");
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                _log($"OneBot Client: Reconnect failed: {ex.Message}");
                return true; // still has attempts left, keep trying
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _ws?.Dispose();
            _cts?.Dispose();
        }
    }
}

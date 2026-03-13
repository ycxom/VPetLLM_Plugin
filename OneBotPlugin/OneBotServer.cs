using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OneBotPlugin
{
    public class OneBotServer : IDisposable
    {
        private HttpListener? _httpListener;
        private readonly string _host;
        private readonly int _port;
        private readonly string _accessToken;
        private readonly Action<string> _log;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
        private bool _disposed;

        public event EventHandler<OneBotEvent>? OnEvent;
        public bool IsRunning => _httpListener?.IsListening == true;
        public int ClientCount => _clients.Count;

        public OneBotServer(string host, int port, string accessToken, Action<string> log)
        {
            _host = host;
            _port = port;
            _accessToken = accessToken;
            _log = log;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://{_host}:{_port}/");

            try
            {
                _httpListener.Start();
                _log($"OneBot Server: Listening on ws://{_host}:{_port}/");
                _listenTask = AcceptLoopAsync(_cts.Token);
            }
            catch (HttpListenerException ex)
            {
                _log($"OneBot Server: Failed to start: {ex.Message}. Try running as admin or use 'netsh http add urlacl url=http://{_host}:{_port}/ user=Everyone'");
                throw;
            }
        }

        public async Task StopAsync()
        {
            _cts?.Cancel();
            _httpListener?.Stop();

            foreach (var kv in _clients)
            {
                try
                {
                    if (kv.Value.State == WebSocketState.Open)
                        await kv.Value.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None);
                }
                catch { }
            }
            _clients.Clear();

            if (_listenTask is not null)
            {
                try { await _listenTask; } catch { }
            }
        }

        public async Task BroadcastEventAsync(object evt)
        {
            var json = JsonConvert.SerializeObject(evt);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            var deadClients = new List<string>();

            foreach (var kv in _clients)
            {
                try
                {
                    if (kv.Value.State == WebSocketState.Open)
                        await kv.Value.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    else
                        deadClients.Add(kv.Key);
                }
                catch
                {
                    deadClients.Add(kv.Key);
                }
            }

            foreach (var id in deadClients)
            {
                _clients.TryRemove(id, out _);
            }
        }

        public async Task SendActionResponseAsync(string clientId, OneBotActionResponse response)
        {
            if (_clients.TryGetValue(clientId, out var ws) && ws.State == WebSocketState.Open)
            {
                var json = JsonConvert.SerializeObject(response);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener!.GetContextAsync();

                    if (!context.Request.IsWebSocketRequest)
                    {
                        await HandleHttpRequestAsync(context);
                        continue;
                    }

                    // Verify access token
                    if (!VerifyToken(context.Request))
                    {
                        context.Response.StatusCode = 401;
                        context.Response.Close();
                        _log("OneBot Server: Rejected connection - invalid token");
                        continue;
                    }

                    var wsContext = await context.AcceptWebSocketAsync(null);
                    var clientId = Guid.NewGuid().ToString("N")[..8];
                    _clients[clientId] = wsContext.WebSocket;
                    _log($"OneBot Server: Client connected ({clientId}), total: {_clients.Count}");

                    _ = HandleClientAsync(clientId, wsContext.WebSocket, ct);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _log($"OneBot Server: Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleHttpRequestAsync(HttpListenerContext context)
        {
            // Handle HTTP API requests (e.g., from AstrBot HTTP mode)
            try
            {
                if (!VerifyToken(context.Request))
                {
                    context.Response.StatusCode = 401;
                    context.Response.Close();
                    return;
                }

                if (context.Request.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();

                    var evt = JsonConvert.DeserializeObject<OneBotEvent>(body);
                    if (evt is not null)
                        OnEvent?.Invoke(this, evt);

                    context.Response.StatusCode = 200;
                    var responseBytes = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(responseBytes);
                }
                else
                {
                    context.Response.StatusCode = 200;
                    var status = JsonConvert.SerializeObject(new { status = "ok", clients = _clients.Count });
                    var bytes = Encoding.UTF8.GetBytes(status);
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
            }
            catch (Exception ex)
            {
                _log($"OneBot Server: HTTP error: {ex.Message}");
                context.Response.StatusCode = 500;
            }
            finally
            {
                context.Response.Close();
            }
        }

        private async Task HandleClientAsync(string clientId, WebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[8192];

            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    HandleClientMessage(clientId, json);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception ex)
            {
                _log($"OneBot Server: Client {clientId} error: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                _log($"OneBot Server: Client disconnected ({clientId}), total: {_clients.Count}");
                ws.Dispose();
            }
        }

        private void HandleClientMessage(string clientId, string json)
        {
            try
            {
                var jObj = JObject.Parse(json);

                // Client sends OneBot actions (e.g., send_msg from AstrBot)
                if (jObj.ContainsKey("action"))
                {
                    var request = jObj.ToObject<OneBotActionRequest>();
                    if (request is not null)
                    {
                        // Wrap as event for the plugin to handle
                        var evt = new OneBotEvent
                        {
                            PostType = "_action",
                            RawMessage = json,
                        };
                        OnEvent?.Invoke(this, evt);
                    }
                    return;
                }

                // Client sends events (e.g., from reverse WS OneBot impl)
                var oneBotEvt = jObj.ToObject<OneBotEvent>();
                if (oneBotEvt is not null)
                    OnEvent?.Invoke(this, oneBotEvt);
            }
            catch (Exception ex)
            {
                _log($"OneBot Server: Parse error from client {clientId}: {ex.Message}");
            }
        }

        private bool VerifyToken(HttpListenerRequest request)
        {
            if (string.IsNullOrEmpty(_accessToken)) return true;

            var auth = request.Headers["Authorization"];
            if (auth is not null)
            {
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return auth[7..] == _accessToken;
                if (auth.StartsWith("Token ", StringComparison.OrdinalIgnoreCase))
                    return auth[6..] == _accessToken;
            }

            var token = request.QueryString["access_token"];
            return token == _accessToken;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _httpListener?.Close();
            foreach (var kv in _clients)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _clients.Clear();
            _cts?.Dispose();
        }
    }
}

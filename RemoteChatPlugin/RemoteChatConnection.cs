using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Globalization;
using VPetLLM.Core.RemoteChat;

namespace RemoteChatPlugin;

internal sealed class RemoteChatConnection : IAsyncDisposable
{
    private readonly RemoteChatSettings _settings;
    private readonly string _serverUrl;
    private readonly Func<string, string, Task> _onChat;
    private readonly Func<IReadOnlyList<RemoteChatHistoryItem>?> _historyProvider;
    private readonly Action<bool> _onPresence;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Queue<string> _seenOrder = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<(bool confirmed, string? value)>> _pendingInteractions = new(StringComparer.Ordinal);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private DerivedKeys? _keys;

    internal string Status { get; private set; } = "disconnected";

    internal RemoteChatConnection(RemoteChatSettings settings, string serverUrl, Func<string, string, Task> onChat,
        Func<IReadOnlyList<RemoteChatHistoryItem>?> historyProvider, Action<bool> onPresence, Action<string> log)
    {
        _settings = settings;
        _serverUrl = serverUrl ?? "";
        _onChat = onChat;
        _historyProvider = historyProvider;
        _onPresence = onPresence;
        _log = log;
    }

    internal void Start()
    {
        if (_runTask is { IsCompleted: false }) return;
        _cts = new CancellationTokenSource();
        // 必须在线程池上起循环：Start() 通常由 WPF UI 线程调用，直接 await 会把
        // 整条收发链的续体 post 回 Dispatcher，卸载时同步等待就会死锁。
        var token = _cts.Token;
        _runTask = Task.Run(() => RunWithReconnectAsync(token));
    }

    private async Task RunWithReconnectAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReadAsync(cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Status = "disconnected";
                _log($"RemoteChat: connection error ({ex.GetType().Name}); retrying");
            }

            try
            {
                await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
            catch (OperationCanceledException) { break; }
        }
        Status = "stopped";
    }

    private async Task ConnectAndReadAsync(CancellationToken cancellationToken)
    {
        if (!RemoteChatPlugin.IsSafeServerUrl(_serverUrl))
        {
            Status = "not_configured";
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            return;
        }

        var secret = ProtocolCrypto.UnprotectSecret(_settings.ProtectedSecret);
        try
        {
            _keys?.Dispose();
            _keys = ProtocolCrypto.DeriveKeys(secret, _settings.RoomId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        _socket = socket;
        Status = "connecting";
        await socket.ConnectAsync(new Uri(_serverUrl), cancellationToken).ConfigureAwait(false);
        await SendJsonAsync(new
        {
            type = "hello", v = 1, room_id = _settings.RoomId, role = "plugin", verifier = _keys.Verifier
        }, cancellationToken).ConfigureAwait(false);
        Status = "connected";
        _log("RemoteChat: encrypted relay connected");

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var text = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false);
            if (text is null) break;
            await HandleFrameAsync(text, cancellationToken).ConfigureAwait(false);
        }
        Status = "disconnected";
        _socket = null;
    }

    private async Task HandleFrameAsync(string raw, CancellationToken cancellationToken)
    {
        RelayEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<RelayEnvelope>(raw, JsonOptions.Default); }
        catch (JsonException) { return; }
        if (envelope is null) return;

        // 中继下发的在线状态：浏览器上/下线。驱动桌面接入确认与挂件。
        if (envelope.Type == "presence")
        {
            _onPresence(envelope.PeerOnline == true);
            return;
        }

        if (envelope.Type != "relay" || envelope.Version != 1) return;
        if (envelope.RoomId != _settings.RoomId) { _log("RemoteChat: dropped frame (room mismatch)"); return; }
        if (envelope.MessageId.Length is < 16 or > 32) { _log("RemoteChat: dropped frame (bad message id)"); return; }
        if (!Remember(envelope.MessageId)) { _log("RemoteChat: dropped frame (duplicate/replay)"); return; }
        if (_keys is null) return;

        try
        {
            using var payload = ProtocolCrypto.Decrypt(_keys.EncryptionKey, _settings.RoomId, envelope);
            var root = payload.RootElement;
            if (!root.TryGetProperty("type", out var type)) return;
            var kind = type.GetString();
            _log($"RemoteChat: received frame '{kind}'");

            if (kind == "interaction_response")
            {
                HandleInteractionResponse(root);
                return;
            }

            if (kind == "history_request")
            {
                await SendHistorySnapshotAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (kind != "chat" ||
                !root.TryGetProperty("request_id", out var requestIdElement) ||
                !root.TryGetProperty("sent_at", out var sentAtElement) ||
                !root.TryGetProperty("text", out var textElement)) { _log("RemoteChat: dropped frame (missing chat fields)"); return; }
            var requestId = requestIdElement.GetString() ?? "";
            var text = textElement.GetString() ?? "";
            if (!DateTimeOffset.TryParse(sentAtElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sentAt) ||
                (DateTimeOffset.UtcNow - sentAt).Duration() > TimeSpan.FromMinutes(5) ||
                requestId.Length is < 8 or > 64 || string.IsNullOrWhiteSpace(text) || text.Length > _settings.MaxMessageLength)
            { _log("RemoteChat: dropped chat (failed validation: time/length)"); return; }
            _log($"RemoteChat: dispatching chat request ({text.Length} chars)");
            await _onChat(requestId, text).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            _log("RemoteChat: rejected a message that failed authentication");
        }
        catch (JsonException)
        {
            _log("RemoteChat: rejected malformed encrypted content");
        }
    }

    internal async Task SendReplyAsync(string requestId, string text, bool isError = false)
    {
        if (_keys is null || _socket?.State != WebSocketState.Open) return;
        var frame = ProtocolCrypto.Encrypt(_keys.EncryptionKey, _settings.RoomId, new
        {
            type = isError ? "error" : "reply", request_id = requestId,
            sent_at = DateTimeOffset.UtcNow.ToString("O"), text
        });
        await SendJsonAsync(frame, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
    }

    internal async Task SendResultAsync(string requestId, IReadOnlyList<RemoteChatEvent> events)
    {
        if (_keys is null || _socket?.State != WebSocketState.Open) return;
        var safeEvents = new List<object>();
        var remaining = 40_000;
        foreach (var item in events.Take(64))
        {
            if (remaining <= 0) break;
            var content = Limit(item.Content, Math.Min(remaining, 24_000));
            remaining -= content?.Length ?? 0;
            var arguments = Limit(item.Arguments, Math.Min(remaining, 8_000));
            remaining -= arguments?.Length ?? 0;
            var result = Limit(item.Result, Math.Min(remaining, 8_000));
            remaining -= result?.Length ?? 0;
            safeEvents.Add(new
            {
                kind = item.Kind,
                content,
                plugin_name = item.PluginName,
                arguments,
                result,
                success = item.Success
            });
        }
        var frame = ProtocolCrypto.Encrypt(_keys.EncryptionKey, _settings.RoomId, new
        {
            type = "chat_result", request_id = requestId,
            sent_at = DateTimeOffset.UtcNow.ToString("O"), events = safeEvents
        });
        await SendJsonAsync(frame, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 把一次交互请求推送到浏览器并等待用户回执。超时或通道异常时返回 (false, null)（安全默认：拒绝）。
    /// </summary>
    internal async Task<(bool confirmed, string? value)> RequestInteractionAsync(
        string requestId, VPetLLM.Core.Interaction.InteractionRequest interaction, CancellationToken cancellationToken)
    {
        if (_keys is null || _socket?.State != WebSocketState.Open) return (false, null);

        var interactionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var tcs = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingInteractions[interactionId] = tcs;
        try
        {
            var frame = ProtocolCrypto.Encrypt(_keys.EncryptionKey, _settings.RoomId, new
            {
                type = "interaction_request",
                request_id = requestId,
                interaction_id = interactionId,
                sent_at = DateTimeOffset.UtcNow.ToString("O"),
                kind = interaction.Kind.ToString().ToLowerInvariant(),
                source = Limit(interaction.Source, 200),
                title = Limit(interaction.Title, 200),
                message = Limit(interaction.Message, 8_000),
                default_value = Limit(interaction.DefaultValue, 8_000),
                choices = interaction.Choices,
                confirm_text = Limit(interaction.ConfirmText, 80),
                cancel_text = Limit(interaction.CancelText, 80)
            });
            await SendJsonAsync(frame, cancellationToken).ConfigureAwait(false);

            var timeout = interaction.Timeout;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            using (timeoutCts.Token.Register(() => tcs.TrySetResult((false, null))))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            return (false, null);
        }
        finally
        {
            _pendingInteractions.TryRemove(interactionId, out _);
        }
    }

    /// <summary>要求已连接的浏览器主动断开（如桌面端点击「断开」）。</summary>
    internal async Task SendDisconnectAsync(string reason)
    {
        if (_keys is null || _socket?.State != WebSocketState.Open) return;
        try
        {
            var frame = ProtocolCrypto.Encrypt(_keys.EncryptionKey, _settings.RoomId, new
            {
                type = "disconnect",
                sent_at = DateTimeOffset.UtcNow.ToString("O"),
                reason = Limit(reason, 200)
            });
            await SendJsonAsync(frame, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// 应浏览器请求回传近期本地聊天历史。历史共享未开启时回传 enabled=false 的空快照。
    /// </summary>
    private async Task SendHistorySnapshotAsync(CancellationToken cancellationToken)
    {
        if (_keys is null || _socket?.State != WebSocketState.Open) return;

        IReadOnlyList<RemoteChatHistoryItem>? history;
        try { history = _historyProvider(); }
        catch { history = null; }

        var enabled = history is not null;
        var messages = new List<object>();
        if (history is not null)
        {
            var remaining = 60_000;
            foreach (var item in history)
            {
                if (remaining <= 0) break;
                var content = Limit(item.Content, Math.Min(remaining, 4_000));
                remaining -= content?.Length ?? 0;
                messages.Add(new { role = item.Role, content, unix_time = item.UnixTime });
            }
        }

        var frame = ProtocolCrypto.Encrypt(_keys.EncryptionKey, _settings.RoomId, new
        {
            type = "history_snapshot",
            sent_at = DateTimeOffset.UtcNow.ToString("O"),
            enabled,
            messages
        });
        await SendJsonAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private void HandleInteractionResponse(JsonElement root)
    {
        if (!root.TryGetProperty("interaction_id", out var idElement)) return;
        var interactionId = idElement.GetString();
        if (string.IsNullOrEmpty(interactionId) || !_pendingInteractions.TryGetValue(interactionId, out var tcs)) return;
        var confirmed = root.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;
        string? value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (value is { Length: > 8_000 }) value = value[..8_000];
        tcs.TrySetResult((confirmed, value));
    }

    private static string? Limit(string? value, int length) => value is null || value.Length <= length
        ? value
        : value[..Math.Max(0, length)] + "…";

    private async Task SendJsonAsync(object value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions.Default);
        if (bytes.Length > 96 * 1024) throw new InvalidOperationException("Frame too large");
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var socket = _socket ?? throw new InvalidOperationException("Socket unavailable");
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new WebSocketException("Binary frames are not supported");
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > 96 * 1024) throw new WebSocketException("Frame too large");
            if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
        }
    }

    private bool Remember(string messageId)
    {
        lock (_seen)
        {
            if (!_seen.Add(messageId)) return false;
            _seenOrder.Enqueue(messageId);
            while (_seenOrder.Count > 512) _seen.Remove(_seenOrder.Dequeue());
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }

        // 释放所有还在等浏览器回执的交互，避免调用方永远挂在 tcs 上。
        foreach (var pending in _pendingInteractions.Values) pending.TrySetResult((false, null));
        _pendingInteractions.Clear();

        if (_socket is { State: WebSocketState.Open } socket)
        {
            // CloseAsync 会等对端回 close 帧；中继无响应时必须有上限，否则卸载会一直挂住。
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "plugin unloaded", closeCts.Token).ConfigureAwait(false); }
            catch { /* best-effort shutdown */ }
        }
        if (_runTask is not null)
        {
            try { await _runTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch { /* best-effort shutdown */ }
        }
        _keys?.Dispose();
        _cts?.Dispose();
        _sendLock.Dispose();
    }
}

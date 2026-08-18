using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Infrastructure.Configuration;
using VPetLLM.Core.RemoteChat;

namespace RemoteChatPlugin;

public sealed class RemoteChatPlugin : IActionPlugin, IPluginWithData, IPluginTab
{
    private const string ConfigName = "RemoteChatPlugin";

    // 官方 VPetLLM 聊天服务器地址（占位常量，正式发布前替换为真实地址）。
    internal const string DefaultServerUrl = "wss://chat.ycxom.com/";
    internal const string DefaultWebUrl = "https://chat.ycxom.com";

    /// <summary>按当前模式解析出实际使用的中继地址。</summary>
    internal string EffectiveServerUrl => _settings.UseOfficialServer ? DefaultServerUrl : _settings.ServerUrl;
    /// <summary>按当前模式解析出实际使用的网页地址。</summary>
    internal string EffectiveWebUrl => _settings.UseOfficialServer ? DefaultWebUrl : _settings.WebUrl;

    private readonly SemaphoreSlim _chatLock = new(1, 1);
    private VPetLLM.VPetLLM? _vpetLLM;
    private RemoteChatSettings _settings = new();
    private RemoteChatConnection? _connection;
    private RemoteChatOverlay? _overlay;
    private volatile bool _browserApproved;
    private volatile bool _browserOnline;

    public string Name => "remote_chat";
    public string Author => "ycxom";
    public string Description => "管理端到端加密的 VPetLLM 远端聊天连接。配对密钥不会发送给中继服务或 LLM。";
    public string Parameters => "action(status/pair/rotate/configure/reconnect), server(wss://...), web(https://...)";
    public string Examples => "`<|plugin_remote_chat_begin|> action(status) <|plugin_remote_chat_end|>`";
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";
    public string PluginDataDir { get; set; } = "";

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        _settings = PluginConfigHelper.Load<RemoteChatSettings>(ConfigName);
        _overlay = new RemoteChatOverlay(Language.StartsWith("zh"));
        EnsurePairingMaterial();
        StartConnection();
        Log("RemoteChat Plugin initialized (message contents are never logged)");
    }

    public async Task<string> Function(string arguments)
    {
        var action = Regex.Match(arguments, @"action\(([^)]+)\)", RegexOptions.IgnoreCase).Groups[1].Value.Trim().ToLowerInvariant();
        switch (action)
        {
            case "status":
                var mode = _settings.UseOfficialServer ? "official VPetLLM server" : "self-hosted";
                return $"Remote chat status: {_connection?.Status ?? "stopped"} ({mode}). Room identifier: {RedactedRoom()}.";
            case "official":
                _settings.UseOfficialServer = true;
                SaveSettings();
                await RestartConnectionAsync();
                return "Switched to the official VPetLLM chat server.";
            case "pair":
                return CopyPairingLink();
            case "key":
                return CopyAccessKey();
            case "disconnect":
                DisconnectBrowser();
                return "Disconnected the remote browser (plugin stays online).";
            case "rotate":
                await StopConnectionAsync();
                var pairing = ProtocolCrypto.CreatePairing();
                _settings.RoomId = pairing.RoomId;
                _settings.ProtectedSecret = pairing.ProtectedSecret;
                SaveSettings();
                StartConnection();
                return CopyPairingLink("Pairing credentials rotated. New link copied locally; old links no longer work.");
            case "configure":
                return await ConfigureAsync(arguments);
            case "reconnect":
                await RestartConnectionAsync();
                return "Remote chat reconnect started.";
            default:
                return "Supported actions: status, pair, key, disconnect, rotate, configure, official, reconnect.";
        }
    }

    private async Task<string> ConfigureAsync(string arguments)
    {
        var server = Regex.Match(arguments, @"server\(([^)]+)\)", RegexOptions.IgnoreCase).Groups[1].Value.Trim().Trim('"', '\'');
        var web = Regex.Match(arguments, @"web\(([^)]+)\)", RegexOptions.IgnoreCase).Groups[1].Value.Trim().Trim('"', '\'');
        if (!IsSafeServerUrl(server) || !IsSafeWebUrl(web))
            return "Configuration rejected: production URLs must use wss:// and https:// (localhost may use ws:// and http://).";
        _settings.ServerUrl = server;
        _settings.WebUrl = web.TrimEnd('/');
        _settings.UseOfficialServer = false;
        SaveSettings();
        await RestartConnectionAsync();
        return "Remote chat endpoints saved (self-hosted mode). Use action(pair) to copy the private pairing link locally.";
    }

    private async Task HandleRemoteChatAsync(string requestId, string text)
    {
        if (_vpetLLM is null || _connection is null) return;
        if (!_browserApproved)
        {
            // 接入尚未被桌面端确认：拒绝处理，提示等待。
            Log("RemoteChat: chat rejected — browser not approved yet");
            await _connection.SendReplyAsync(requestId, "等待桌面端确认接入…", true);
            return;
        }
        await _chatLock.WaitAsync();
        try
        {
            // VPetLLM owns the complete local-equivalent processing pipeline. This plugin
            // only transports the encrypted request and the resulting UI/plugin events.
            Log("RemoteChat: processing chat through local pipeline…");
            var responder = new RemoteInteractionResponder(_connection, requestId);
            var response = await _vpetLLM.SendRemoteChatAsync(text, responder);
            Log($"RemoteChat: pipeline done, sending {response.Events.Count} event(s) back");
            await _connection.SendResultAsync(requestId, response.Events);
        }
        catch (Exception ex)
        {
            Log($"RemoteChat: chat processing failed ({ex.GetType().Name})");
            await _connection.SendReplyAsync(requestId, "VPetLLM 处理消息时发生错误。", true);
        }
        finally { _chatLock.Release(); }
    }

    private string CopyPairingLink(string success = "Private pairing link copied to the local clipboard.")
    {
        var serverUrl = EffectiveServerUrl;
        var webUrl = EffectiveWebUrl;
        if (!IsSafeServerUrl(serverUrl) || !IsSafeWebUrl(webUrl))
            return "Configure server(...) and web(...) endpoints first.";
        var secret = ProtocolCrypto.ExportSecret(_settings.ProtectedSecret);
        var fragment = $"room={Uri.EscapeDataString(_settings.RoomId)}&secret={Uri.EscapeDataString(secret)}&server={Uri.EscapeDataString(serverUrl)}";
        var link = $"{webUrl}/#{fragment}";
        if (!TrySetClipboard(link))
            return "Could not access the local clipboard. The pairing secret was not printed or logged.";
        _ = ClearClipboardLaterAsync(link);
        return success + " Clipboard will be cleared after two minutes if unchanged.";
    }

    /// <summary>
    /// 在 UI 线程写剪贴板并重试。WPF 的 Clipboard 在被其它进程短暂占用时会抛 COMException，
    /// 单次调用容易失败；用 SetDataObject(copy:true) + 多次重试更稳，且退出后内容仍保留。
    /// </summary>
    private static bool TrySetClipboard(string text)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return false;
        return dispatcher.Invoke(() =>
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try { Clipboard.SetDataObject(text, true); return true; }
                catch { System.Threading.Thread.Sleep(90); }
            }
            try { Clipboard.SetText(text); return true; }
            catch { return false; }
        });
    }

    private static async Task ClearClipboardLaterAsync(string expected)
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == expected) Clipboard.Clear();
            });
        }
        catch { /* clipboard ownership may have changed */ }
    }

    private void EnsurePairingMaterial()
    {
        try
        {
            var secret = ProtocolCrypto.UnprotectSecret(_settings.ProtectedSecret);
            try
            {
                if (_settings.RoomId.Length >= 22 && secret.Length == 32) return;
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret);
            }
        }
        catch { /* rotate corrupt/legacy credentials */ }
        var pairing = ProtocolCrypto.CreatePairing();
        _settings.RoomId = pairing.RoomId;
        _settings.ProtectedSecret = pairing.ProtectedSecret;
        SaveSettings();
    }

    private void StartConnection()
    {
        _connection = new RemoteChatConnection(_settings, EffectiveServerUrl, HandleRemoteChatAsync, ProvideHistory, OnBrowserPresence, Log);
        _connection.Start();
    }

    private async Task RestartConnectionAsync()
    {
        await StopConnectionAsync();
        StartConnection();
    }

    private async Task StopConnectionAsync()
    {
        _browserApproved = false;
        _browserOnline = false;
        _overlay?.CloseApproval();
        _overlay?.HideWidget();
        if (_connection is not null) await _connection.DisposeAsync();
        _connection = null;
    }

    private void SaveSettings() => PluginConfigHelper.Save(ConfigName, _settings);
    private string RedactedRoom() => _settings.RoomId.Length >= 6 ? $"{_settings.RoomId[..6]}…" : "not configured";
    internal static bool IsSafeServerUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "wss" || (uri.Scheme == "ws" && IsLoopback(uri)));
    private static bool IsSafeWebUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || (uri.Scheme == "http" && IsLoopback(uri)));
    private static bool IsLoopback(Uri uri) => uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    public void Unload()
    {
        _overlay?.Dispose();
        _overlay = null;

        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            // Unload() 由 UI 线程同步调用。断连必须整体跑在线程池上并且有超时上限，
            // 否则续体排回 Dispatcher 而 UI 又在等它，直接死锁。
            try
            {
                Task.Run(async () => await connection.DisposeAsync().ConfigureAwait(false))
                    .Wait(TimeSpan.FromSeconds(5));
            }
            catch { /* best-effort shutdown */ }
        }

        _chatLock.Dispose();
        Log("RemoteChat Plugin unloaded");
    }

    private void Log(string message) => _vpetLLM?.Log(message);

    // ---------------- 供设置面板调用的公共方法（自托管配置入口）----------------

    internal bool UseOfficialServer => _settings.UseOfficialServer;

    /// <summary>是否允许远端回看本地聊天历史（隐私项，改动即时保存）。</summary>
    internal bool ShareHistory
    {
        get => _settings.ShareHistory;
        set { _settings.ShareHistory = value; SaveSettings(); }
    }

    /// <summary>历史快照提供者：关闭时返回 null（连接侧据此回传 enabled=false）。</summary>
    private IReadOnlyList<VPetLLM.Core.RemoteChat.RemoteChatHistoryItem>? ProvideHistory()
    {
        // 未经桌面端确认接入的浏览器不得拉取历史。
        if (!_browserApproved || !_settings.ShareHistory || _vpetLLM is null) return null;
        try { return _vpetLLM.GetRemoteHistory(_settings.HistoryMaxMessages); }
        catch { return null; }
    }

    // ---------- 接入确认（3 秒弹窗）与常驻挂件 ----------

    /// <summary>浏览器在中继上/下线。上线时若未批准则弹出接入确认。</summary>
    private void OnBrowserPresence(bool online)
    {
        _browserOnline = online;
        if (!online)
        {
            _browserApproved = false;
            _overlay?.CloseApproval();
            _overlay?.HideWidget();
            return;
        }
        if (_browserApproved) return; // 已批准会话的重连，无需再次确认
        _overlay?.ShowApproval(3, ApproveBrowser, DisconnectAndResetKey);
    }

    private void ApproveBrowser()
    {
        if (!_browserOnline) return;
        _browserApproved = true;
        _overlay?.ShowWidget(
            () => _browserOnline ? Localize("状态：已接入", "Status: connected") : Localize("状态：已断开", "Status: disconnected"),
            DisconnectBrowser,
            DisconnectAndResetKey);
    }

    /// <summary>断开当前浏览器：通知其关闭，插件仍在线等待下一次接入。</summary>
    internal void DisconnectBrowser()
    {
        _browserApproved = false;
        _overlay?.CloseApproval();
        _overlay?.HideWidget();
        var conn = _connection;
        if (conn is not null) _ = conn.SendDisconnectAsync("disconnected_by_host");
    }

    /// <summary>断开并重置密钥：通知浏览器关闭并轮换配对凭证，旧密钥立即失效。</summary>
    internal void DisconnectAndResetKey()
    {
        _browserApproved = false;
        _overlay?.CloseApproval();
        _overlay?.HideWidget();
        _ = ResetKeyAsync();
    }

    private async Task ResetKeyAsync()
    {
        var conn = _connection;
        if (conn is not null) await conn.SendDisconnectAsync("key_reset");
        await RotateAsync();
    }

    // ---------- 接入密钥（公开 WebUI 手动输入用）----------

    /// <summary>构建单串接入密钥：内含服务器地址+房间+密钥，供公开网页粘贴连接。</summary>
    internal string BuildAccessKey()
    {
        var server = EffectiveServerUrl;
        if (!IsSafeServerUrl(server)) return "";
        var secret = ProtocolCrypto.ExportSecret(_settings.ProtectedSecret);
        var raw = $"{server}\n{_settings.RoomId}\n{secret}";
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "vpl1_" + b64;
    }

    internal string CopyAccessKey()
    {
        var key = BuildAccessKey();
        if (string.IsNullOrEmpty(key))
            return Localize("请先配置服务器地址。", "Configure the server endpoint first.");
        if (!TrySetClipboard(key))
            return Localize("无法访问本地剪贴板，接入密钥未打印或记录。", "Could not access the local clipboard; the access key was not printed or logged.");
        _ = ClearClipboardLaterAsync(key);
        return Localize(
            "接入密钥已复制到本地剪贴板。在网页粘贴即可连接（两分钟后若未变更将自动清除）。",
            "Access key copied to the local clipboard. Paste it into the web page to connect (cleared after two minutes if unchanged).");
    }
    internal string CurrentServerUrl => _settings.ServerUrl;
    internal string CurrentWebUrl => _settings.WebUrl;
    internal string StatusText => _connection?.Status ?? "stopped";
    internal string Language => _vpetLLM?.Settings.Language ?? "en";

    /// <summary>
    /// 保存服务器模式并重连。官方模式使用内置地址；自部署模式校验并保存用户地址。
    /// </summary>
    internal async Task<string> SaveEndpointsAsync(bool useOfficial, string server, string web)
    {
        if (!useOfficial)
        {
            server = (server ?? "").Trim().Trim('"', '\'');
            web = (web ?? "").Trim().Trim('"', '\'');
            if (!IsSafeServerUrl(server) || !IsSafeWebUrl(web))
                return Localize(
                    "地址无效：生产环境须用 wss:// 与 https://（localhost 可用 ws:// 与 http://）。",
                    "Invalid endpoints: production must use wss:// and https:// (localhost may use ws:// and http://).");
            _settings.ServerUrl = server;
            _settings.WebUrl = web.TrimEnd('/');
        }
        _settings.UseOfficialServer = useOfficial;
        SaveSettings();
        await RestartConnectionAsync();
        return useOfficial
            ? Localize("已切换到 VPetLLM 官方聊天服务器并重新连接。", "Switched to the official VPetLLM chat server and reconnecting.")
            : Localize("已保存自部署地址并重新连接。", "Saved self-hosted endpoints and reconnecting.");
    }

    internal string CopyPairing() => CopyPairingLink();

    internal async Task<string> RotateAsync()
    {
        await StopConnectionAsync();
        var pairing = ProtocolCrypto.CreatePairing();
        _settings.RoomId = pairing.RoomId;
        _settings.ProtectedSecret = pairing.ProtectedSecret;
        SaveSettings();
        StartConnection();
        return CopyPairingLink(Localize(
            "配对凭证已轮换，新链接已复制到本地；旧链接不再有效。",
            "Pairing credentials rotated. New link copied locally; old links no longer work."));
    }

    internal Task ReconnectAsync() => RestartConnectionAsync();

    private string Localize(string zh, string en) => Language.StartsWith("zh") ? zh : en;

    // ---------------- IPluginTab（设置窗口内嵌面板）----------------

    public string TabTitle => Localize("远端聊天", "Remote Chat");

    public System.Windows.FrameworkElement CreatePanel() => new RemoteChatSettingPanel(this);
}

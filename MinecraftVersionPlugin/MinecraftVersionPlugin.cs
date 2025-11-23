using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VPetLLM.Core;

namespace MinecraftVersionPlugin
{
    public class MinecraftVersionPlugin : IVPetLLMPlugin, IPluginWithData, IActionPlugin
    {
        public string Name => "MinecraftVersionWatcher";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM == null) return "监听并识别我的世界（Java/基岩）版本与玩家事件，按标准JSON向AI推送。";
                return _vpetLLM.Settings.Language switch
                {
                    "ja" => "Minecraft（Java/Bedrock）のバージョンとプレイヤーイベントを監視し、標準JSONでAIに通知します。",
                    "zh-hans" => "监听并识别我的世界（Java/基岩）版本与玩家事件，按标准JSON向AI推送。",
                    "zh-hant" => "監聽並識別我的世界（Java/基岩）版本與玩家事件，按標準JSON向AI推送。",
                    _ => "Watches Minecraft (Java/Bedrock) version and player events, pushes standard JSON to AI."
                };
            }
        }
        public string Parameters => "setting";
        public string Examples => "Examples: `<|plugin_MinecraftVersionWatcher_begin|> action(setting) <|plugin_MinecraftVersionWatcher_end|>`, `<|plugin_MinecraftVersionWatcher_begin|> action(servers_status) <|plugin_MinecraftVersionWatcher_end|>`";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string PluginDataDir { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;
        private CancellationTokenSource? _cts;
        private Task? _task;
        private volatile bool _suppressSend = false;

        private const string SettingFileName = "MinecraftVersionPlugin.json";
        private Setting _setting = new Setting();

        private string _lastReportedKey = "";

        // 多服务器：每个服务器独立在线集合
        private readonly Dictionary<string, HashSet<string>> _onlineByServer = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _onlineLock = new();
        // 基线标记：记录每个服务器是否已完成首次基线建立
        private readonly HashSet<string> _hasBaselineServers = new(StringComparer.OrdinalIgnoreCase);
        // 每服务器最近版本记录
        private readonly Dictionary<string, string> _versionByServer = new(StringComparer.OrdinalIgnoreCase);

        // 缓存文件名
        private const string CacheFileName = "MinecraftVersionPlugin.cache.json";

        // 缓存结构
        private class CacheState
        {
            [JsonProperty("last_version_key")] public string LastVersionKey { get; set; } = "";
            [JsonProperty("online_by_server")] public Dictionary<string, string[]> OnlineByServer { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            [JsonProperty("updated_at")] public string UpdatedAt { get; set; } = DateTime.Now.ToString("o");
        }

        private void LoadCache()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(PluginDataDir)) return;
                var path = Path.Combine(PluginDataDir, CacheFileName);
                if (!File.Exists(path)) return;
                var cache = JsonConvert.DeserializeObject<CacheState>(File.ReadAllText(path));
                if (cache == null) return;

                _lastReportedKey = cache.LastVersionKey ?? "";
                if (cache.OnlineByServer != null)
                {
                    lock (_onlineLock)
                    {
                        _onlineByServer.Clear();
                        foreach (var kv in cache.OnlineByServer)
                        {
                            _onlineByServer[kv.Key] = new HashSet<string>(kv.Value ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"LoadCache error: {ex.Message}");
            }
        }

        private void SaveCache()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(PluginDataDir)) return;
                Directory.CreateDirectory(PluginDataDir);
                var path = Path.Combine(PluginDataDir, CacheFileName);

                var cache = new CacheState
                {
                    LastVersionKey = _lastReportedKey ?? "",
                    UpdatedAt = DateTime.Now.ToString("o"),
                    OnlineByServer = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                };

                lock (_onlineLock)
                {
                    foreach (var kv in _onlineByServer)
                        cache.OnlineByServer[kv.Key] = kv.Value?.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(cache, Formatting.Indented));
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"SaveCache error: {ex.Message}");
            }
        }

        public class Setting
        {
            [JsonProperty("jitter_delay")] public int JitterDelay { get; set; } = 10000;
            [JsonProperty("enable_minecraft_detect")] public bool EnableMinecraftDetect { get; set; } = true;
            [JsonProperty("java_allowed_versions")] public string[] JavaAllowedVersions { get; set; } = Array.Empty<string>();
            [JsonProperty("bedrock_allowed_versions")] public string[] BedrockAllowedVersions { get; set; } = Array.Empty<string>();
            [JsonProperty("bedrock_manual_version")] public string BedrockManualVersion { get; set; } = "";

            // 多服务器配置（新）
            [JsonProperty("servers")] public List<ServerConfig> Servers { get; set; } = new();

            // 向下兼容（旧）
            [JsonProperty("server_name")] public string ServerName { get; set; } = "";

            // 本地 SLP 查询（旧单服务器入口，作为回退）
            [JsonProperty("enable_local_status_query")] public bool EnableLocalStatusQuery { get; set; } = false;
            [JsonProperty("server_address")] public string ServerAddress { get; set; } = "127.0.0.1:25565";
        }

        public class ServerConfig
        {
            [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
            [JsonProperty("address")] public string Address { get; set; } = "127.0.0.1:25565";
            [JsonProperty("alias")] public string Alias { get; set; } = "";
            // 服务器类型（java/bedrock/be），默认 java
            [JsonProperty("product")] public string Product { get; set; } = "java";
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowTextLength(IntPtr hWnd);

        private static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                int length = GetWindowTextLength(hWnd);
                if (length == 0) return string.Empty;
                var sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            try
            {
                if (string.IsNullOrWhiteSpace(PluginDataDir))
                {
                    var baseDir = Path.GetDirectoryName(FilePath);
                    if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                    {
                        baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "VPetLLM_Plugin", "MinecraftVersionPlugin");
                    }
                    PluginDataDir = Path.Combine(baseDir, "plugin-data");
                }
                Directory.CreateDirectory(PluginDataDir);
            }
            catch { }

            LoadSetting();
            LoadCache();
            _cts = new CancellationTokenSource();
            _task = Task.Run(() => LoopAsync(_cts.Token));
            VPetLLM.Utils.Logger.Log("Minecraft Version Plugin initialized and monitoring started.");
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_setting.EnableMinecraftDetect)
                    {
                        IntPtr hWnd = GetForegroundWindow();
                        if (hWnd != IntPtr.Zero)
                        {
                            GetWindowThreadProcessId(hWnd, out uint pid);
                            Process proc = Process.GetProcessById((int)pid);
                            string pn = proc.ProcessName;
                            string title = GetWindowTitle(hWnd);
                            if (pn != "VPet-Simulator.Windows")
                            {
                                if (TryDetectMinecraft(pn, title, out string product, out string version) && IsAllowed(product, version))
                                {
                                    string key = $"{product}:{version}";
                                    if (!string.IsNullOrWhiteSpace(version) && !string.Equals(key, _lastReportedKey, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _lastReportedKey = key;
                                        SaveCache();
                                        var msg = $"Minecraft({product}) detected, version: {version}, Time: {DateTime.Now}";
                                        _vpetLLM?.Log(msg);
                                        SendMcJson(new
                                        {
                                            type = "mc_event",
                                            eventType = "version",
                                            product,
                                            version,
                                            title = $"[{product}] version {version}",
                                            ts = DateTime.Now.ToString("o")
                                        });
                                    }
                                }
                            }
                        }
                    }

                    if (_setting.EnableLocalStatusQuery)
                    {
                        var servers = _setting.Servers ?? new List<ServerConfig>();
                        if (servers.Count > 0)
                        {
                            foreach (var s in servers)
                            {
                                if (s != null && s.Enabled && !string.IsNullOrWhiteSpace(s.Address))
                                {
                                    await QueryLocalStatusAndDiffAsync(s, token);
                                }
                            }
                        }
                        else
                        {
                            await QueryLocalStatusAndDiffAsync(_setting.ServerAddress, token);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _vpetLLM?.Log($"MinecraftVersionPlugin error: {ex.Message}");
                }
                SaveCache();
                await Task.Delay(_setting.JitterDelay, token);
            }
        }

        private bool TryDetectMinecraft(string processName, string windowTitle, out string product, out string version)
        {
            product = ""; version = "";
            if (IsMinecraftJava(processName, windowTitle, out string jv)) { product = "Java"; version = jv; return true; }
            if (IsMinecraftBedrock(processName, windowTitle, out string bv)) { product = "Bedrock"; version = bv; return true; }
            return false;
        }

        private static bool IsMinecraftJava(string processName, string windowTitle, out string version)
        {
            version = "";
            var pn = (processName ?? "").ToLowerInvariant();
            var title = windowTitle ?? "";
            if (pn == "javaw" || pn == "javaw.exe" || title.IndexOf("Minecraft", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string[] patterns =
                {
                    @"Minecraft(?:\s*:?[\sA-Za-z]*)?\s(\d+(?:\.\d+){1,3}\w*)",
                    @"\((\d+(?:\.\d+){1,3}\w*)\)"
                };
                foreach (var pat in patterns)
                {
                    var m = Regex.Match(title, pat, RegexOptions.IgnoreCase);
                    if (m.Success) { version = m.Groups[1].Value.Trim(); break; }
                }
                return !string.IsNullOrWhiteSpace(version);
            }
            return false;
        }

        private bool IsMinecraftBedrock(string processName, string windowTitle, out string version)
        {
            version = "";
            var pn = (processName ?? "").ToLowerInvariant();
            var title = windowTitle ?? "";
            bool looksLike = pn == "minecraft.windows" || pn == "minecraft" || (title.IndexOf("Minecraft", StringComparison.OrdinalIgnoreCase) >= 0 && pn != "javaw" && pn != "javaw.exe");
            if (!looksLike) return false;
            var m = Regex.Match(title, @"\b(\d+\.\d+\.\d+(?:\.\d+)?)\b");
            version = m.Success ? m.Groups[1].Value.Trim() : _setting.BedrockManualVersion?.Trim() ?? "";
            return true;
        }

        private bool IsAllowed(string product, string version)
        {
            if (string.Equals(product, "Java", StringComparison.OrdinalIgnoreCase)) return VersionAllowed(version, _setting.JavaAllowedVersions);
            if (string.Equals(product, "Bedrock", StringComparison.OrdinalIgnoreCase)) return VersionAllowed(version, _setting.BedrockAllowedVersions);
            return false;
        }

        private static bool VersionAllowed(string detected, string[] allowed)
        {
            if (allowed == null || allowed.Length == 0) return true;
            if (string.IsNullOrWhiteSpace(detected)) return false;
            foreach (var v in allowed) if (string.Equals(v?.Trim(), detected.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void Unload()
        {
            _cts?.Cancel();
            VPetLLM.Utils.Logger.Log("Minecraft Version Plugin unload signal sent.");
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }

        // 测试期间抑制发送
        public void SetSuppressSend(bool suppress)
        {
            _suppressSend = suppress;
        }

        public Task<string> Function(string arguments)
        {
            var m = Regex.Match(arguments ?? "", @"action\((\w+)\)", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups.Count > 1)
            {
                var act = m.Groups[1].Value;
                if (act.Equals("setting", StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var win = new winMinecraftSetting(this);
                        win.Show();
                    });
                    return Task.FromResult("Setting window opened.");
                }
                else if (act.Equals("servers_status", StringComparison.OrdinalIgnoreCase))
                {
                    var list = new List<object>();
                    lock (_onlineLock)
                    {
                        var servers = (_setting.Servers ?? new List<ServerConfig>()).Where(s => s != null).ToList();
                        if (servers.Count > 0)
                        {
                            foreach (var s in servers)
                            {
                                var addr = (s.Address ?? "").Trim();
                                var alias = (s.Alias ?? "").Trim();
                                _onlineByServer.TryGetValue(addr, out var set);
                                _versionByServer.TryGetValue(addr, out var ver);
                                list.Add(new
                                {
                                    address = addr,
                                    alias = alias,
                                    version = ver ?? "",
                                    online = set == null ? Array.Empty<string>() : set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                                });
                            }
                        }
                        else
                        {
                            var addr = (_setting.ServerAddress ?? "").Trim();
                            var alias = (_setting.ServerName ?? "").Trim();
                            _onlineByServer.TryGetValue(addr, out var set);
                            _versionByServer.TryGetValue(addr, out var ver);
                            list.Add(new
                            {
                                address = addr,
                                alias = alias,
                                version = ver ?? "",
                                online = set == null ? Array.Empty<string>() : set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                            });
                        }
                    }
                    var payload = new
                    {
                        type = "mc_event",
                        eventType = "servers_status",
                        servers = list,
                        ts = DateTime.Now.ToString("o")
                    };
                    SendMcJson(payload);
                    return Task.FromResult("Servers status sent.");
                }
            }
            return Task.FromResult("Invalid action.");
        }

        public void SaveSetting()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(PluginDataDir))
                    return;
                Directory.CreateDirectory(PluginDataDir);
                var path = Path.Combine(PluginDataDir, SettingFileName);
                File.WriteAllText(path, JsonConvert.SerializeObject(_setting, Formatting.Indented));
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"SaveSetting error: {ex.Message}");
            }
        }

        private void LoadSetting()
        {
            if (string.IsNullOrEmpty(PluginDataDir)) return;
            var path = Path.Combine(PluginDataDir, SettingFileName);
            _setting = File.Exists(path)
                ? (JsonConvert.DeserializeObject<Setting>(File.ReadAllText(path)) ?? new Setting())
                : new Setting();
        }

        private string GetServerDisplayName()
        {
            // 用于非服务器特定场景：优先第一个服务器的别名/地址，其次旧别名，再次默认
            var s = _setting.Servers?.FirstOrDefault(x => x != null);
            if (s != null)
            {
                if (!string.IsNullOrWhiteSpace(s.Alias)) return s.Alias.Trim();
                if (!string.IsNullOrWhiteSpace(s.Address)) return s.Address.Trim();
            }
            if (!string.IsNullOrWhiteSpace(_setting.ServerName)) return _setting.ServerName.Trim();
            var addr = _setting.ServerAddress;
            return string.IsNullOrWhiteSpace(addr) ? "Minecraft服务器" : addr.Trim();
        }

        private static string RemoveColorAndFormatting(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var noColor = Regex.Replace(s, "§.", "");
            return noColor.Replace("\r", "").Replace("\n", "").Trim();
        }

        private static string ExtractDescription(dynamic jo)
        {
            try
            {
                if (jo == null || jo.description == null) return "";
                var desc = jo.description;
                try { string s = (string)desc; if (!string.IsNullOrWhiteSpace(s)) return s; } catch { }
                try { if (desc.text != null) { string t = (string)desc.text; if (!string.IsNullOrWhiteSpace(t)) return t; } } catch { }
                try
                {
                    if (desc.extra != null)
                    {
                        var sb = new StringBuilder();
                        foreach (var e in desc.extra)
                        {
                            try { if (e.text != null) { string t = (string)e.text; if (!string.IsNullOrEmpty(t)) sb.Append(t); } } catch { }
                        }
                        var combined = sb.ToString();
                        if (!string.IsNullOrWhiteSpace(combined)) return combined;
                    }
                }
                catch { }
                try { string any = desc.ToString(); return any ?? ""; } catch { return ""; }
            }
            catch { return ""; }
        }

        private void SendMcJson(object payload)
        {
            try
            {
                if (_suppressSend) return;
                var json = JsonConvert.SerializeObject(payload);
                VPetLLM.Handlers.PluginHandler.SendPluginMessage("MinecraftVersionWatcher", json);
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"SendMcJson error: {ex.Message}");
            }
        }

        // 多服务器入口：按单服务器配置查询
        private async Task QueryLocalStatusAndDiffAsync(ServerConfig server, CancellationToken token)
        {
            try
            {
                var serverAddress = (server.Address ?? "").Trim();
                ParseHostPort(serverAddress, out var host, out var port);
                // 基岩版（Bedrock/BE）服务器使用 UDP 19132 状态查询
                if (IsBedrockServer(server, port))
                {
                    await QueryBedrockStatusAndDiffAsync(server, host, port, token);
                    return;
                }
                using var client = new TcpClient();
                client.ReceiveTimeout = 4000;
                client.SendTimeout = 4000;
                await client.ConnectAsync(host, port);
                using var stream = client.GetStream();

                var hsPayload = BuildHandshakePayload(host, port);
                var handshake = PrependVarInt(hsPayload);
                await stream.WriteAsync(handshake, 0, handshake.Length, token);
                await stream.WriteAsync(new byte[] { 0x01, 0x00 }, 0, 2, token);

                var json = await ReadStatusJsonAsync(stream, token);
                dynamic jo = JsonConvert.DeserializeObject(json);
                int online = 0;
                string versionName = "";
                try
                {
                    online = (int?)jo.players?.online ?? 0;
                    versionName = (string?)jo.version?.name ?? "";
                    lock (_onlineLock)
                    {
                        _versionByServer[serverAddress] = versionName ?? "";
                    }
                }
                catch { }

                string descText = ExtractDescription(jo);
                string cleanedDesc = RemoveColorAndFormatting(descText);
                string displayName = !string.IsNullOrWhiteSpace(server.Alias)
                    ? server.Alias.Trim()
                    : (!string.IsNullOrWhiteSpace(cleanedDesc) ? cleanedDesc : serverAddress);
                if (displayName.Length > 40) displayName = displayName.Substring(0, 40);

                var sample = new List<string>();
                try
                {
                    if (jo.players?.sample != null)
                    {
                        foreach (var p in jo.players.sample)
                        {
                            try
                            {
                                string name = RemoveColorAndFormatting((string)p.name);
                                name = (name ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(name))
                                    sample.Add(name);
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                if (sample.Count > 0)
                {
                    HashSet<string> onlineSet;
                    bool hasBaseline = false;
                    lock (_onlineLock)
                    {
                        if (!_onlineByServer.TryGetValue(serverAddress, out onlineSet!))
                        {
                            onlineSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            _onlineByServer[serverAddress] = onlineSet;
                        }
                        hasBaseline = _hasBaselineServers.Contains(serverAddress);
                    }

                    // 本轮新集合（已规范化的玩家名）
                    var newSet = new HashSet<string>(sample, StringComparer.OrdinalIgnoreCase);

                    if (!hasBaseline)
                    {
                        // 首次建立基线：记录当前样本为在线，不触发加入/退出事件
                        lock (_onlineLock)
                        {
                            onlineSet.Clear();
                            foreach (var p in newSet) onlineSet.Add(p);
                            _hasBaselineServers.Add(serverAddress);
                        }
                    }
                    else
                    {
                        // 加入：newSet - onlineSet
                        var joins = newSet.Where(p => !onlineSet.Contains(p)).ToArray();
                        foreach (var p in joins)
                        {
                            lock (_onlineLock) { onlineSet.Add(p); }
                            var msg = $"[{displayName}] 玩家加入: {p}。在线: {string.Join(", ", onlineSet)}";
                            _vpetLLM?.Log(msg);
                            VPetLLM.Handlers.PluginHandler.SendPluginMessage("MinecraftVersionWatcher", $"[{displayName}] join play: {p}。playl_ist: {string.Join(",", onlineSet)}");
                        }

                        // 退出：onlineSet - newSet
                        var leaves = onlineSet.Where(p => !newSet.Contains(p)).ToArray();
                        foreach (var p in leaves)
                        {
                            lock (_onlineLock) { onlineSet.Remove(p); }
                            var msg = $"[{displayName}] 玩家退出: {p}。在线: {string.Join(", ", onlineSet)}";
                            _vpetLLM?.Log(msg);
                            VPetLLM.Handlers.PluginHandler.SendPluginMessage("MinecraftVersionWatcher", $"[{displayName}] left play: {p}。playl_ist: {string.Join(",", onlineSet)}");
                        }
                    }
                }
                else
                {
                    // 仅人数；当在线人数为 0 时，逐个判定为退出并清空集合，并建立空基线
                    // _vpetLLM?.Log($"SLP: {displayName} Online={online}, Version={versionName}");
                    if (online == 0)
                    {
                        HashSet<string>? snapshot = null;
                        lock (_onlineLock)
                        {
                            if (_onlineByServer.TryGetValue(serverAddress, out var onlineSet))
                            {
                                snapshot = new HashSet<string>(onlineSet, StringComparer.OrdinalIgnoreCase);
                                onlineSet.Clear();
                            }
                        }
                        if (snapshot != null && snapshot.Count > 0)
                        {
                            foreach (var p in snapshot)
                            {
                                var msg = $"[{displayName}] 玩家退出: {p}。在线: ";
                                _vpetLLM?.Log(msg);
                                VPetLLM.Handlers.PluginHandler.SendPluginMessage("MinecraftVersionWatcher", $"[{displayName}] left play: {p}。playl_ist: ");
                            }
                        }
                        // 标记该服务器已建立空基线
                        lock (_onlineLock)
                        {
                            _hasBaselineServers.Add(serverAddress);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"QueryLocalStatus error: {ex.Message}");
            }
        }

        // 旧单服务器回退路径，复用同样差异逻辑并落入按地址的独立集合
        private async Task QueryLocalStatusAndDiffAsync(string serverAddress, CancellationToken token)
        {
            await QueryLocalStatusAndDiffAsync(new ServerConfig { Enabled = true, Address = serverAddress ?? "", Alias = _setting.ServerName ?? "" }, token);
        }

        private static void ParseHostPort(string addr, out string host, out int port)
        {
            addr = (addr ?? "").Trim();
            if (string.IsNullOrEmpty(addr)) { host = "127.0.0.1"; port = 25565; return; }
            var parts = addr.Split(':');
            host = parts[0];
            port = 25565;
            if (parts.Length > 1 && int.TryParse(parts[1], out var p) && p > 0 && p <= 65535) port = p;
        }

        // 判断是否为基岩版服务器：优先 server.Product 指定，其次常见端口 19132
        private static bool IsBedrockServer(ServerConfig s, int port)
        {
            var p = (s?.Product ?? "").Trim().ToLowerInvariant();
            if (p == "bedrock" || p == "be") return true;
            return port == 19132;
        }

        // 基岩版 UDP 状态查询（RakNet Unconnected Ping/Pong）
        private async Task QueryBedrockStatusAndDiffAsync(ServerConfig server, string host, int port, CancellationToken token)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.ReceiveTimeout = 4000;
                udp.Client.SendTimeout = 4000;

                // 构造 Unconnected Ping 包：ID(0x01) + 8字节时间戳 + MAGIC + 8字节客户端GUID
                var magic = new byte[] { 0x00, 0xFF, 0xFF, 0x00, 0xFE, 0xFE, 0xFE, 0xFE, 0xFD, 0xFD, 0xFD, 0xFD, 0x12, 0x34, 0x56, 0x78 };
                var guid = BitConverter.GetBytes((long)DateTime.UtcNow.Ticks);
                if (BitConverter.IsLittleEndian) Array.Reverse(guid); // 使用大端
                var ts = BitConverter.GetBytes((long)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (BitConverter.IsLittleEndian) Array.Reverse(ts);

                using var ms = new MemoryStream();
                ms.WriteByte(0x01);               // Unconnected Ping
                ms.Write(ts, 0, ts.Length);       // 8 bytes
                ms.Write(magic, 0, magic.Length); // MAGIC
                ms.Write(guid, 0, guid.Length);   // 8 bytes
                var packet = ms.ToArray();

                await udp.SendAsync(packet, packet.Length, host, port);

                var receiveTask = udp.ReceiveAsync();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(4000, token));
                if (completed != receiveTask) throw new IOException("Bedrock status timeout");
                var result = receiveTask.Result;

                var buf = result.Buffer ?? Array.Empty<byte>();
                var text = Encoding.UTF8.GetString(buf);

                // 解析 MCPE;MOTD;Protocol;Version;Online;Max;...
                int idx = text.IndexOf("MCPE;", StringComparison.OrdinalIgnoreCase);
                string versionName = "";
                int online = 0;
                string motd = "";
                if (idx >= 0)
                {
                    var payload = text.Substring(idx);
                    var parts = payload.Split(';');
                    if (parts.Length >= 6)
                    {
                        motd = parts[1];
                        versionName = parts[3];
                        int.TryParse(parts[4], out online);
                    }
                }

                var serverAddress = (server.Address ?? "").Trim();
                lock (_onlineLock)
                {
                    _versionByServer[serverAddress] = versionName ?? "";
                }

                string cleanedDesc = RemoveColorAndFormatting(motd);
                string displayName = !string.IsNullOrWhiteSpace(server.Alias)
                    ? server.Alias.Trim()
                    : (!string.IsNullOrWhiteSpace(cleanedDesc) ? cleanedDesc : serverAddress);
                if (displayName.Length > 40) displayName = displayName.Substring(0, 40);

                _vpetLLM?.Log($"Bedrock SLP: {displayName} Online={online}, Version={versionName}");

                if (online == 0)
                {
                    HashSet<string>? snapshot = null;
                    lock (_onlineLock)
                    {
                        if (_onlineByServer.TryGetValue(serverAddress, out var onlineSet))
                        {
                            snapshot = new HashSet<string>(onlineSet, StringComparer.OrdinalIgnoreCase);
                            onlineSet.Clear();
                        }
                        _hasBaselineServers.Add(serverAddress);
                    }
                    if (snapshot != null && snapshot.Count > 0)
                    {
                        foreach (var p in snapshot)
                        {
                            var msg = $"[{displayName}] 玩家退出: {p}。在线: ";
                            _vpetLLM?.Log(msg);
                            VPetLLM.Handlers.PluginHandler.SendPluginMessage("MinecraftVersionWatcher", $"[{displayName}] left play: {p}。playl_ist: ");
                        }
                    }
                }
                else
                {
                    // 非零在线但无玩家名单：建立空基线，避免误触发
                    lock (_onlineLock)
                    {
                        if (!_onlineByServer.ContainsKey(serverAddress))
                            _onlineByServer[serverAddress] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _hasBaselineServers.Add(serverAddress);
                    }
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"QueryBedrockStatus error: {ex.Message}");
            }
        }

        private static byte[] BuildHandshakePayload(string host, int port)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x00);
            WriteVarInt(ms, 759); // protocol
            WriteString(ms, host);
            ms.WriteByte((byte)((port >> 8) & 0xFF));
            ms.WriteByte((byte)(port & 0xFF));
            WriteVarInt(ms, 1);
            return ms.ToArray();
        }

        private static byte[] PrependVarInt(byte[] payload)
        {
            using var ms = new MemoryStream();
            WriteVarInt(ms, payload.Length);
            ms.Write(payload, 0, payload.Length);
            return ms.ToArray();
        }

        private static void WriteVarInt(Stream s, int value)
        {
            uint u = (uint)value;
            do
            {
                byte temp = (byte)(u & 0b0111_1111);
                u >>= 7;
                if (u != 0) temp |= 0b1000_0000;
                s.WriteByte(temp);
            } while (u != 0);
        }

        private static void WriteString(Stream s, string text)
        {
            var data = Encoding.UTF8.GetBytes(text ?? "");
            WriteVarInt(s, data.Length);
            s.Write(data, 0, data.Length);
        }

        private static async Task<string> ReadStatusJsonAsync(NetworkStream stream, CancellationToken token)
        {
            int length = await ReadVarIntAsync(stream, token);
            if (length <= 0) return "{}";
            int packetId = await ReadVarIntAsync(stream, token);
            if (packetId != 0x00) return "{}";
            int jsonLen = await ReadVarIntAsync(stream, token);
            var buf = new byte[jsonLen];
            int read = 0;
            while (read < jsonLen)
            {
                int r = await stream.ReadAsync(buf, read, jsonLen - read, token);
                if (r == 0) break;
                read += r;
            }
            return Encoding.UTF8.GetString(buf, 0, read);
        }

        private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken token)
        {
            int numRead = 0, result = 0; byte read;
            do
            {
                var b = new byte[1];
                int r = await stream.ReadAsync(b, 0, 1, token);
                if (r == 0) throw new IOException("Stream closed");
                read = b[0];
                int value = (read & 0b0111_1111);
                result |= (value << (7 * numRead));
                numRead++;
                if (numRead > 5) throw new IOException("VarInt too big");
            } while ((read & 0b1000_0000) != 0);
            return result;
        }

        // UI 交互 Getter/Setter（保持兼容）
        public int GetJitterDelay() => _setting.JitterDelay;
        public void SetJitterDelay(int ms) { _setting.JitterDelay = ms; SaveSetting(); }
        public string GetServerName() => _setting.ServerName;
        public void SetServerName(string n) { _setting.ServerName = n ?? ""; SaveSetting(); }
        public string GetDetectedNamePreview()
        {
            // 预览名称：优先别名（新 servers）、否则旧别名、再否则旧地址
            var s = _setting.Servers?.FirstOrDefault(x => x != null);
            if (s != null)
            {
                if (!string.IsNullOrWhiteSpace(s.Alias)) return s.Alias.Trim();
                if (!string.IsNullOrWhiteSpace(s.Address)) return s.Address.Trim();
            }
            if (!string.IsNullOrWhiteSpace(_setting.ServerName)) return _setting.ServerName.Trim();
            return string.IsNullOrWhiteSpace(_setting.ServerAddress) ? "Minecraft服务器" : _setting.ServerAddress.Trim();
        }

        public bool GetEnableLocalStatusQuery() => _setting.EnableLocalStatusQuery;
        public void SetEnableLocalStatusQuery(bool v) { _setting.EnableLocalStatusQuery = v; SaveSetting(); }
        public string GetServerAddress() => _setting.ServerAddress;
        public void SetServerAddress(string s) { _setting.ServerAddress = string.IsNullOrWhiteSpace(s) ? "127.0.0.1:25565" : s.Trim(); SaveSetting(); }

        // 多服务器 UI 扩展接口
        public List<ServerConfig> GetServers()
        {
            return _setting.Servers?.Select(s => new ServerConfig
            {
                Enabled = s.Enabled,
                Address = s.Address ?? "",
                Alias = s.Alias ?? "",
                Product = string.IsNullOrWhiteSpace(s.Product) ? "java" : s.Product
            }).ToList()
                   ?? new List<ServerConfig>();
        }
        public void SetServers(List<ServerConfig> servers)
        {
            _setting.Servers = servers ?? new List<ServerConfig>();
            SaveSetting();
        }

        public string[] GetOnlinePlayersForServer(string serverAddress)
        {
            if (string.IsNullOrWhiteSpace(serverAddress)) return Array.Empty<string>();
            lock (_onlineLock)
            {
                if (_onlineByServer.TryGetValue(serverAddress, out var onlineSet))
                {
                    return onlineSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }
            return Array.Empty<string>();
        }

        public string[] GetOnlinePlayersSnapshot()
        {
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_onlineLock)
            {
                foreach (var kv in _onlineByServer)
                    foreach (var p in kv.Value) all.Add(p);
            }
            return all.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
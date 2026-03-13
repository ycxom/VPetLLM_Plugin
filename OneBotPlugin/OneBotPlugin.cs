using System.Windows;
using Newtonsoft.Json;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Infrastructure.Configuration;

namespace OneBotPlugin
{
    public class OneBotPlugin : IActionPlugin, IDynamicInfoPlugin, IPluginWithData
    {
        public string Name => "onebot";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM is null) return "OneBot QQ消息插件。收到[OneBot ...]前缀消息时，用{\"group_id\":群号,\"message\":\"回复内容\"}回复QQ。";
                var masterHint = !string.IsNullOrEmpty(_settings.MasterQQ) ? " IsMaster:TRUE=主人" : "";
                return (_vpetLLM.Settings.Language) switch
                {
                    "ja" => $"QQメッセージプラグイン。[OneBot ...]で始まるメッセージはQQからです。{{\"group_id\":ID,\"message\":\"返信\"}}で返信。VPetバブルとQQ返信は独立（ささやき可能）。{masterHint}",
                    "zh-hant" => $"QQ訊息插件。[OneBot ...]開頭的訊息來自QQ，用{{\"group_id\":群號,\"message\":\"回覆\"}}回覆。VPet氣泡與QQ回覆獨立（可悄悄話）。{masterHint}",
                    "en" => $"QQ message plugin. [OneBot ...] messages are from QQ. Reply with {{\"group_id\":id,\"message\":\"text\"}}. VPet bubble and QQ reply are independent (whisper capable).{masterHint}",
                    _ => $"QQ消息插件。[OneBot ...]开头的消息来自QQ，用{{\"group_id\":群号,\"message\":\"回复内容\"}}回复QQ。VPet气泡与QQ回复独立（可实现悄悄话）。{masterHint}",
                };
            }
        }
        public string Parameters => "JSON: {\"group_id\":群号,\"message\":\"文本\"} 回复群聊 | {\"user_id\":QQ号,\"message\":\"文本\"} 回复私聊 | 可选\"image\":\"URL\"发图 | action(setting/status/reconnect)";
        public string Examples => "收到[OneBot Group:123456 User:小明(789)]后回复: `<|plugin_onebot_begin|> {\"group_id\":123456,\"message\":\"你好小明！\"} <|plugin_onebot_end|>` message字段为QQ收到的内容，say为桌宠气泡";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string PluginDataDir { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;
        private OneBotSettings _settings = new();
        private OneBotClient? _wsClient;
        private OneBotServer? _wsServer;
        private OneBotHttpClient? _httpClient;
        private MessageProcessor? _messageProcessor;
        private CancellationTokenSource? _pluginCts;
        private string _connectionStatus = "Disconnected";

        // Deduplication: prevent Function() from re-sending messages that MessageProcessor already sent
        private readonly HashSet<string> _recentSendKeys = new();
        private readonly object _dedupLock = new();

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            LoadSettings();
            _pluginCts = new CancellationTokenSource();

            _ = StartConnectionsAsync();

            Log("OneBot Plugin Initialized!");
        }

        #region Deduplication

        /// <summary>
        /// Mark a message as sent. Returns true if it's new (not a duplicate).
        /// </summary>
        internal bool TryMarkSent(long? groupId, long? userId, string message)
        {
            var key = $"{groupId}:{userId}:{message.GetHashCode()}";
            lock (_dedupLock)
            {
                if (!_recentSendKeys.Add(key))
                    return false; // Already sent
            }
            // Auto-expire after 30 seconds
            _ = Task.Delay(30000).ContinueWith(_ =>
            {
                lock (_dedupLock) { _recentSendKeys.Remove(key); }
            });
            return true;
        }

        /// <summary>
        /// Send a message to QQ with deduplication. Used by both MessageProcessor and Function().
        /// </summary>
        internal async Task<bool> SendMessageDeduplicatedAsync(long? userId, long? groupId, string? text, string? image = null)
        {
            if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(image))
                return false;

            if (!TryMarkSent(groupId, userId, text ?? image ?? ""))
            {
                Log($"OneBot: Skipping duplicate send to {(groupId.HasValue ? $"group {groupId}" : $"user {userId}")}");
                return true; // Already sent, consider it success
            }

            bool sent = false;
            if (_wsClient?.IsConnected == true)
                sent = await _wsClient.SendMessageAsync(userId, groupId, text ?? "", image);
            if (!sent && _httpClient is not null)
                sent = await _httpClient.SendMessageAsync(userId, groupId, text ?? "", image);

            if (!sent)
                Log($"OneBot: Failed to send to {(groupId.HasValue ? $"group {groupId}" : $"user {userId}")}");
            return sent;
        }

        #endregion

        #region Connection Management

        private async Task StartConnectionsAsync()
        {
            try
            {
                if (_settings.EnableForwardWS && !string.IsNullOrWhiteSpace(_settings.ForwardWSUrl))
                {
                    _wsClient = new OneBotClient(_settings.ForwardWSUrl, _settings.AccessToken, Log);
                    _wsClient.OnEvent += OnOneBotEvent;
                    await _wsClient.ConnectAsync(_pluginCts!.Token);

                    if (_wsClient.IsConnected && string.IsNullOrEmpty(_settings.BotQQ))
                    {
                        var info = await _wsClient.GetLoginInfoAsync();
                        if (info is not null)
                        {
                            _settings.BotQQ = info.UserId.ToString();
                            SaveSettings();
                            Log($"OneBot: Auto-detected BotQQ = {_settings.BotQQ}");
                        }
                    }
                }

                if (_settings.EnableHttpApi && !string.IsNullOrWhiteSpace(_settings.HttpApiUrl))
                {
                    _httpClient = new OneBotHttpClient(_settings.HttpApiUrl, _settings.AccessToken, Log);
                    Log("OneBot: HTTP API client ready");

                    if (string.IsNullOrEmpty(_settings.BotQQ))
                    {
                        var info = await _httpClient.GetLoginInfoAsync();
                        if (info is not null)
                        {
                            _settings.BotQQ = info.UserId.ToString();
                            SaveSettings();
                            Log($"OneBot: Auto-detected BotQQ = {_settings.BotQQ}");
                        }
                    }
                }

                if (_settings.EnableReverseWS)
                {
                    _wsServer = new OneBotServer(_settings.ReverseWSHost, _settings.ReverseWSPort, _settings.AccessToken, Log);
                    _wsServer.OnEvent += OnOneBotEvent;
                    try
                    {
                        _wsServer.Start();
                        Log($"OneBot: Reverse WS server started on port {_settings.ReverseWSPort}");
                    }
                    catch (Exception ex)
                    {
                        Log($"OneBot: Reverse WS server failed: {ex.Message}");
                    }
                }

                UpdateConnectionStatus();

                _messageProcessor = new MessageProcessor(_vpetLLM!, this, _settings, Log, SendReplyAsync);
                _messageProcessor.Start();
            }
            catch (Exception ex)
            {
                Log($"OneBot: Startup error: {ex.Message}");
            }
        }

        private async Task StopConnectionsAsync()
        {
            _pluginCts?.Cancel();

            if (_wsClient is not null)
            {
                _wsClient.OnEvent -= OnOneBotEvent;
                await _wsClient.DisconnectAsync();
                _wsClient.Dispose();
                _wsClient = null;
            }

            if (_wsServer is not null)
            {
                _wsServer.OnEvent -= OnOneBotEvent;
                await _wsServer.StopAsync();
                _wsServer.Dispose();
                _wsServer = null;
            }

            _httpClient?.Dispose();
            _httpClient = null;

            _messageProcessor?.Dispose();
            _messageProcessor = null;

            _connectionStatus = "Disconnected";
        }

        public async Task RestartConnectionsAsync()
        {
            await StopConnectionsAsync();
            _pluginCts = new CancellationTokenSource();
            await StartConnectionsAsync();
        }

        private void UpdateConnectionStatus()
        {
            var parts = new List<string>();
            if (_wsClient?.IsConnected == true)
                parts.Add("ForwardWS:OK");
            else if (_settings.EnableForwardWS)
                parts.Add("ForwardWS:Disconnected");

            if (_wsServer?.IsRunning == true)
                parts.Add($"ReverseWS:OK({_wsServer.ClientCount} clients)");
            else if (_settings.EnableReverseWS)
                parts.Add("ReverseWS:Stopped");

            if (_httpClient is not null)
                parts.Add("HTTP:Ready");

            _connectionStatus = parts.Count > 0 ? string.Join(", ", parts) : "No connection configured";
        }

        #endregion

        #region Message Handling

        private void OnOneBotEvent(object? sender, OneBotEvent evt)
        {
            try
            {
                if (evt.PostType == "meta_event") return;

                if (evt.PostType == "message")
                    HandleMessageEvent(evt);
            }
            catch (Exception ex)
            {
                Log($"OneBot: Event handling error: {ex.Message}");
            }
        }

        private void HandleMessageEvent(OneBotEvent evt)
        {
            if (evt.MessageType == "group")
            {
                if (!_settings.AllowAllGroup && evt.GroupId.HasValue && !_settings.AllowedGroups.Contains(evt.GroupId.Value))
                    return;
                if (_settings.AtTriggerOnly && !CQCodeParser.ContainsAt(evt.Message, _settings.BotQQ))
                    return;
            }
            else if (evt.MessageType == "private")
            {
                if (!_settings.AllowAllPrivate && !_settings.AllowedUsers.Contains(evt.UserId))
                    return;
            }

            var text = CQCodeParser.ExtractPlainText(evt.Message);
            if (string.IsNullOrWhiteSpace(text)) return;

            var senderName = evt.Sender?.Card;
            if (string.IsNullOrEmpty(senderName))
                senderName = evt.Sender?.Nickname ?? evt.UserId.ToString();

            var context = new OneBotMessageContext
            {
                UserId = evt.UserId,
                GroupId = evt.GroupId,
                SenderName = senderName,
                Text = text,
                MessageId = evt.MessageId,
            };

            _messageProcessor?.EnqueueMessage(context);
        }

        private async Task SendReplyAsync(OneBotMessageContext context, string text)
        {
            bool sent = false;

            if (_wsClient?.IsConnected == true)
                sent = await _wsClient.SendReplyAsync(context, text, _settings.ReplyWithAt);

            if (!sent && _httpClient is not null)
                sent = await _httpClient.SendReplyAsync(context, text, _settings.ReplyWithAt);

            if (!sent)
                Log($"OneBot: Failed to send reply to {(context.IsGroup ? $"group {context.GroupId}" : $"user {context.UserId}")}");
        }

        #endregion

        #region IActionPlugin

        public async Task<string> Function(string arguments)
        {
            if (_vpetLLM is null) return "VPetLLM not initialized.";

            try
            {
                var trimmed = arguments.Trim();

                // Handle action commands
                if (trimmed.StartsWith("action("))
                {
                    var action = trimmed.Replace("action(", "").TrimEnd(')').ToLower();
                    return HandleAction(action);
                }

                // Parse JSON
                var args = JsonConvert.DeserializeObject<OneBotSendArgs>(trimmed);
                if (args is null)
                    return GetLocalizedError("invalid_args");

                bool hasMessage = !string.IsNullOrWhiteSpace(args.Message);
                bool hasImage = !string.IsNullOrWhiteSpace(args.Image);
                bool hasTarget = args.GroupId.HasValue || args.UserId.HasValue;

                if (!hasTarget)
                    return GetLocalizedError("invalid_args");

                if (!hasMessage && !hasImage)
                    return GetLocalizedError("invalid_args");

                // Send message/image to QQ (with dedup)
                var sent = await SendMessageDeduplicatedAsync(args.UserId, args.GroupId, args.Message, args.Image);

                if (sent)
                    return ""; // No bubble
                return GetLocalizedError("send_failed");
            }
            catch (JsonException)
            {
                return GetLocalizedError("invalid_args");
            }
            catch (Exception ex)
            {
                Log($"OneBot: Function error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private string HandleAction(string action)
        {
            switch (action)
            {
                case "setting":
                case "settings":
                    OpenSettings();
                    return GetLocalizedMessage("settings_opened");
                case "status":
                case "info":
                    UpdateConnectionStatus();
                    return $"OneBot Status: {_connectionStatus}, BotQQ: {_settings.BotQQ}";
                case "reconnect":
                    _ = RestartConnectionsAsync();
                    return GetLocalizedMessage("reconnecting");
                default:
                    return GetLocalizedError("invalid_action");
            }
        }

        private void OpenSettings()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var lang = _vpetLLM?.Settings.Language ?? "en";
                    var window = new winOneBotSetting(this, _settings, lang, OnSettingsSaved);
                    window.Show();
                });
            }
            catch (Exception ex)
            {
                Log($"OneBot: Error opening settings: {ex.Message}");
            }
        }

        private void OnSettingsSaved(OneBotSettings newSettings)
        {
            _settings = newSettings;
            SaveSettings();
            _ = RestartConnectionsAsync();
            Log("OneBot: Settings saved and connections restarted");
        }

        #endregion

        #region IDynamicInfoPlugin

        public string GetDynamicInfo()
        {
            UpdateConnectionStatus();
            var lang = _vpetLLM?.Settings.Language ?? "en";
            var aiName = _vpetLLM?.Settings.AiName ?? "AI";

            bool connected = _wsClient?.IsConnected == true || _wsServer?.IsRunning == true || _httpClient is not null;
            Log($"OneBot: GetDynamicInfo called — connected={connected}, lang={lang}");

            if (!connected)
            {
                return lang switch
                {
                    "zh-hans" => "OneBot 插件：未连接。使用 `<|plugin_onebot_begin|> action(setting) <|plugin_onebot_end|>` 打开设置配置连接。",
                    "zh-hant" => "OneBot 插件：未連接。使用 `<|plugin_onebot_begin|> action(setting) <|plugin_onebot_end|>` 打開設置配置連接。",
                    "ja" => "OneBotプラグイン：未接続。`<|plugin_onebot_begin|> action(setting) <|plugin_onebot_end|>` で設定を開いてください。",
                    _ => "OneBot Plugin: Not connected. Use `<|plugin_onebot_begin|> action(setting) <|plugin_onebot_end|>` to configure.",
                };
            }

            return lang switch
            {
                "zh-hans" => $@"[OneBot QQ 消息通道]
已连接（{_connectionStatus}）。来自 QQ 的消息以 [OneBot Group/User:昵称(QQ号) IsMaster:TRUE/FALSE] 前缀出现。IsMaster:TRUE 表示主人。
重要规则：收到 QQ 消息时必须回复，群聊回群聊，私聊回私聊。
回复方式：在回复中包含 `<|plugin_onebot_begin|> {{""group_id"":群号,""message"":""你要发到QQ的内容""}} <|plugin_onebot_end|>`
- message 字段 = QQ 收到的文本（必填）；say 气泡 = 桌宠显示的文本（独立）
- 悄悄话：不包含 onebot 标记 = 只在桌宠气泡显示，QQ 看不到
- 回复 QQ 用户时，桌宠气泡应写：`<|say_begin|> ""{aiName}正在回复{{用户名}}：{{回复内容}}"", happy <|say_end|>`
- 可选发图片：加 ""image"":""URL""
- 可自由配合其他插件使用",
                "zh-hant" => $@"[OneBot QQ 訊息通道]
已連接（{_connectionStatus}）。來自 QQ 的訊息以 [OneBot Group/User:暱稱(QQ號) IsMaster:TRUE/FALSE] 前綴出現。IsMaster:TRUE 表示主人。
重要規則：收到 QQ 訊息時必須回覆，群聊回群聊，私聊回私聊。
回覆方式：`<|plugin_onebot_begin|> {{""group_id"":群號,""message"":""回覆內容""}} <|plugin_onebot_end|>`
- message = QQ 收到的文本（必填）；say 氣泡 = 桌寵顯示（獨立）
- 悄悄話：不含 onebot 標記 = 只在桌寵氣泡顯示
- 回覆 QQ 用戶時，桌寵氣泡：`<|say_begin|> ""{aiName}正在回覆{{用戶名}}：{{回覆內容}}"", happy <|say_end|>`",
                "ja" => $@"[OneBot QQ メッセージチャンネル]
接続済み（{_connectionStatus}）。QQからのメッセージは [OneBot Group/User:ニックネーム(QQ番号) IsMaster:TRUE/FALSE] で表示。IsMaster:TRUE はマスター。
重要ルール：QQメッセージ受信時は必ず返信。グループにはグループ、プライベートにはプライベート。
返信方法：`<|plugin_onebot_begin|> {{""group_id"":ID,""message"":""返信内容""}} <|plugin_onebot_end|>`
- message = QQに送信するテキスト（必須）；say バブル = デスクトップペット表示（独立）
- ささやき：onebot マーカーなし = ペットバブルのみ
- QQユーザーに返信時、バブル：`<|say_begin|> ""{aiName}が{{ユーザー名}}に返信中：{{返信内容}}"", happy <|say_end|>`",
                _ => $@"[OneBot QQ Message Channel]
Connected ({_connectionStatus}). QQ messages appear with [OneBot Group/User:Nickname(QQ) IsMaster:TRUE/FALSE] prefix. IsMaster:TRUE = owner.
Rule: You MUST reply to QQ messages. Group→group, private→private.
Reply format: `<|plugin_onebot_begin|> {{""group_id"":id,""message"":""your reply""}} <|plugin_onebot_end|>`
- message = text sent to QQ (required); say bubble = desktop pet display (independent)
- Whisper: no onebot marker = only pet bubble sees it
- When replying to QQ users, pet bubble: `<|say_begin|> ""{aiName} replying to {{username}}: {{reply}}"", happy <|say_end|>`",
            };
        }

        #endregion

        #region Settings Persistence

        private void LoadSettings()
        {
            try
            {
                _settings = PluginConfigHelper.Load<OneBotSettings>("onebot");
            }
            catch (Exception ex)
            {
                Log($"OneBot: Error loading settings: {ex.Message}");
                _settings = new OneBotSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                PluginConfigHelper.Save("onebot", _settings);
            }
            catch (Exception ex)
            {
                Log($"OneBot: Error saving settings: {ex.Message}");
            }
        }

        #endregion

        #region Localization

        private string GetLocalizedError(string errorType)
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";
            return (errorType, lang) switch
            {
                ("invalid_args", "zh-hans") => "错误：参数无效。",
                ("invalid_args", "zh-hant") => "錯誤：參數無效。",
                ("invalid_args", "ja") => "エラー：パラメータが無効です。",
                ("invalid_args", _) => "Error: Invalid arguments.",

                ("send_failed", "zh-hans") => "错误：消息发送失败。",
                ("send_failed", "zh-hant") => "錯誤：訊息傳送失敗。",
                ("send_failed", "ja") => "エラー：送信失敗。",
                ("send_failed", _) => "Error: Failed to send message.",

                ("invalid_action", "zh-hans") => "错误：无效操作。可用：setting, status, reconnect",
                ("invalid_action", "zh-hant") => "錯誤：無效操作。可用：setting, status, reconnect",
                ("invalid_action", "ja") => "エラー：無効。使用可能：setting, status, reconnect",
                ("invalid_action", _) => "Error: Invalid action. Available: setting, status, reconnect",

                _ => $"Error: {errorType}"
            };
        }

        private string GetLocalizedMessage(string messageType)
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";
            return (messageType, lang) switch
            {
                ("settings_opened", "zh-hans") => "设置窗口已打开。",
                ("settings_opened", "zh-hant") => "設置窗口已打開。",
                ("settings_opened", "ja") => "設定ウィンドウが開きました。",
                ("settings_opened", _) => "Settings window opened.",

                ("reconnecting", "zh-hans") => "正在重新连接...",
                ("reconnecting", "zh-hant") => "正在重新連接...",
                ("reconnecting", "ja") => "再接続中...",
                ("reconnecting", _) => "Reconnecting...",

                _ => messageType
            };
        }

        #endregion

        public void Unload()
        {
            _ = StopConnectionsAsync();
            _messageProcessor?.Dispose();
            _pluginCts?.Dispose();
            Log("OneBot Plugin Unloaded!");
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }
    }
}

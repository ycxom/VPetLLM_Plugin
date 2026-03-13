using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace OneBotPlugin
{
    public class MessageProcessor : IDisposable
    {
        private readonly VPetLLM.VPetLLM _vpetLLM;
        private readonly OneBotPlugin _plugin;
        private readonly OneBotSettings _settings;
        private readonly Action<string> _log;
        private readonly Func<OneBotMessageContext, string, Task> _sendReply;
        private readonly ConcurrentQueue<OneBotMessageContext> _messageQueue = new();
        private readonly SemaphoreSlim _processingLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private Task? _processingTask;
        private bool _disposed;

        public MessageProcessor(
            VPetLLM.VPetLLM vpetLLM,
            OneBotPlugin plugin,
            OneBotSettings settings,
            Action<string> log,
            Func<OneBotMessageContext, string, Task> sendReply)
        {
            _vpetLLM = vpetLLM;
            _plugin = plugin;
            _settings = settings;
            _log = log;
            _sendReply = sendReply;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _processingTask = ProcessQueueAsync(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        public void EnqueueMessage(OneBotMessageContext context)
        {
            _messageQueue.Enqueue(context);
            _log($"OneBot: Queued message from {context.SenderName}({context.UserId}){(context.IsGroup ? $" in group {context.GroupId}" : "")}: {Truncate(context.Text, 50)}");
        }

        private async Task ProcessQueueAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_messageQueue.TryDequeue(out var context))
                    {
                        await _processingLock.WaitAsync(ct);
                        try
                        {
                            await ProcessSingleMessageAsync(context);
                        }
                        finally
                        {
                            _processingLock.Release();
                        }
                    }
                    else
                    {
                        await Task.Delay(_settings.MessageQueueInterval, ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log($"OneBot: Queue processing error: {ex.Message}");
                    try { await Task.Delay(1000, ct); } catch { break; }
                }
            }
        }

        private async Task ProcessSingleMessageAsync(OneBotMessageContext context)
        {
            try
            {
                var prompt = context.Text;
                if (string.IsNullOrWhiteSpace(prompt))
                    return;

                // Prepend sender info so the LLM knows who's talking
                var isMaster = !string.IsNullOrEmpty(_settings.MasterQQ) && context.UserId.ToString() == _settings.MasterQQ;
                var contextPrefix = context.IsGroup
                    ? $"[OneBot Group:{context.GroupId} User:{context.SenderName}({context.UserId}) IsMaster:{isMaster.ToString().ToUpper()}] "
                    : $"[OneBot User:{context.SenderName}({context.UserId}) IsMaster:{isMaster.ToString().ToUpper()}] ";

                _log($"OneBot: Processing: {contextPrefix}{Truncate(prompt, 80)}");

                // SendChat triggers the full pipeline: LLM → ResponseHandler → TalkBox → ActionProcessor → plugins
                // NOTE: ChatCore.Chat() always returns "" — the actual response goes through ResponseHandler.
                // We read the response from chat history instead.
                await _vpetLLM.SendChat(contextPrefix + prompt);

                // Read the actual LLM response from chat history
                var history = _vpetLLM.GetChatHistory();
                var lastAssistant = history.LastOrDefault(m => m.Role == "assistant");
                var response = lastAssistant?.Content ?? "";

                if (string.IsNullOrWhiteSpace(response))
                {
                    _log("OneBot: No assistant response found in history (KeepContext may be disabled)");
                    return;
                }

                // Find ALL onebot plugin calls in the response and send immediately.
                // This avoids waiting for VPetLLM's sequential command queue which may be blocked
                // by other commands (say, Terminal, etc.)
                var onebotMatches = Regex.Matches(response,
                    @"<\|plugin_onebot_begin\|>\s*(\{[^}]*\})\s*<\|plugin_onebot_end\|>",
                    RegexOptions.Singleline);

                if (onebotMatches.Count == 0)
                {
                    _log("OneBot: LLM response does not contain onebot plugin call, not sending to QQ");
                    return;
                }

                foreach (Match match in onebotMatches)
                {
                    try
                    {
                        var args = JsonConvert.DeserializeObject<OneBotSendArgs>(match.Groups[1].Value);
                        if (args is null || (!args.GroupId.HasValue && !args.UserId.HasValue))
                            continue;

                        // Explicit message mode only: message field = what QQ receives
                        if (string.IsNullOrWhiteSpace(args.Message) && string.IsNullOrWhiteSpace(args.Image))
                            continue;

                        var textToSend = args.Message;

                        // Truncate
                        if (textToSend is not null && textToSend.Length > _settings.MaxMessageLength)
                            textToSend = textToSend[.._settings.MaxMessageLength] + "...";

                        // Send with dedup (Function() will skip if already sent)
                        var sent = await _plugin.SendMessageDeduplicatedAsync(
                            args.UserId ?? (args.GroupId.HasValue ? null : context.UserId),
                            args.GroupId ?? context.GroupId,
                            textToSend,
                            args.Image);

                        if (sent)
                            _log($"OneBot: Sent to QQ immediately: {Truncate(textToSend ?? "[image]", 80)}");
                    }
                    catch (JsonException ex)
                    {
                        _log($"OneBot: Failed to parse onebot plugin call: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"OneBot: Error processing message from {context.UserId}: {ex.Message}");
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            if (text.Length <= maxLength) return text;
            return text[..maxLength] + "...";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _processingLock.Dispose();
            _cts?.Dispose();
        }
    }
}

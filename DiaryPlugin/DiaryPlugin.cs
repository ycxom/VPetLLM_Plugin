using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VPetLLM;
using VPetLLM.Core.Abstractions.Base;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Infrastructure.Configuration;

namespace DiaryPlugin
{
    /// <summary>插件配置，存于宿主 settings.db。</summary>
    public class DiarySettings
    {
        /// <summary>已处理到的日期（yyyy-MM-dd）。到此日为止的日记都已生成或确认无需生成。</summary>
        public string LastProcessedDate { get; set; } = "";
        /// <summary>是否自动定时写日记。</summary>
        public bool AutoWrite { get; set; } = true;
    }

    /// <summary>
    /// 日记插件：每天回顾与主人的互动，自动生成第一人称日记。
    /// - 次日 00:00 定时写"昨天"的日记；启动时对错过的日子补票（最多回溯 30 天）。
    /// - 当天无 user 互动则不写。
    /// - 日记可 Embedding 向量检索、可在桌面 UI 查看。
    /// - 生成走 ChatCore.Summarize，不打扰宠物对话（不弹气泡/不播 TTS）。
    /// </summary>
    public class DiaryPlugin : IActionPlugin, IDynamicInfoPlugin, IPluginWithData
    {
        public string Name => "Diary";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM is null) return "让AI每天回顾与主人的互动、自动撰写第一人称日记，支持向量检索与桌面查看。";
                // Description 是 UI 显示，按 UI 语言 Settings.Language 走完整多语言
                switch (_vpetLLM.Settings.Language)
                {
                    case "ja":
                        return "AIが毎日ご主人との交流を振り返り、一人称の日記を自動で書きます。ベクトル検索とデスクトップ表示に対応。";
                    case "zh-hant":
                        return "讓AI每天回顧與主人的互動、自動撰寫第一人稱日記，支援向量檢索與桌面查看。";
                    case "en":
                        return "Lets the AI review each day's interactions and automatically write a first-person diary. Supports vector search and a desktop viewer.";
                    case "zh-hans":
                    default:
                        return "让AI每天回顾与主人的互动、自动撰写第一人称日记，支持向量检索与桌面查看。";
                }
            }
        }
        // 注意：Parameters 必须包含子串 "setting"，宿主据此显示插件列表里的"设置"按钮
        public string Parameters => "action(setting|view|write) or search(keyword)";
        public string Examples => "Examples: `<|plugin_Diary_begin|> action(view) <|plugin_Diary_end|>` to open the diary viewer; `<|plugin_Diary_begin|> search(海边) <|plugin_Diary_end|>` to search past diaries.";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string PluginDataDir { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;
        private DiaryDatabase? _db;
        private DiarySettings _settings = new DiarySettings();
        private CancellationTokenSource? _cts;
        private string? _initError;

        private const int MaxCatchUpDays = 30;
        private const int MaxExcerptChars = 6000;

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            try
            {
                _settings = PluginConfigHelper.Load<DiarySettings>("Diary");
                var dbPath = System.IO.Path.Combine(
                    string.IsNullOrEmpty(PluginDataDir)
                        ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VPetLLM", "Plugin", "PluginData", Name)
                        : PluginDataDir,
                    "diary.db");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
                _db = new DiaryDatabase(dbPath);

                _cts = new CancellationTokenSource();
                _ = TimerLoop(_cts.Token);
                VPetLLM.Utils.System.Logger.Log($"Diary Plugin Initialized! DB={dbPath}, 共 {_db.Count()} 篇");
            }
            catch (Exception ex)
            {
                _initError = ex.ToString();
                VPetLLM.Utils.System.Logger.Log($"Diary Plugin 初始化失败: {ex}");
            }
        }

        // ---------------- 定时器 + 补票 ----------------

        private async Task TimerLoop(CancellationToken ct)
        {
            try
            {
                await CatchUpAsync();                       // 启动即补票
                await BackfillVectorsAsync();               // 启动时补齐历史日记的向量
                while (!ct.IsCancellationRequested)
                {
                    var now = DateTime.Now;
                    var nextMidnight = now.Date.AddDays(1); // 次日 00:00
                    var delay = nextMidnight - now;
                    if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromMinutes(1);
                    await Task.Delay(delay, ct);
                    await CatchUpAsync();
                    await BackfillVectorsAsync();
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Log($"DiaryPlugin: TimerLoop 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 从 LastProcessedDate 次日到"昨天"，逐日补写缺失且当天有互动的日记；
        /// 无论是否写，都把 LastProcessedDate 推进到昨天，避免重复检查。
        /// </summary>
        private async Task CatchUpAsync()
        {
            if (_vpetLLM is null || _db is null || !_settings.AutoWrite) return;

            var yesterday = DateTime.Now.Date.AddDays(-1);

            DateTime start;
            if (DateTime.TryParse(_settings.LastProcessedDate, out var last))
                start = last.Date.AddDays(1);
            else
                start = yesterday;                          // 首次运行只看昨天

            var earliest = yesterday.AddDays(-MaxCatchUpDays);
            if (start < earliest) start = earliest;

            for (var day = start; day <= yesterday; day = day.AddDays(1))
            {
                var dateStr = day.ToString("yyyy-MM-dd");
                try
                {
                    if (!_db.HasEntry(dateStr) && HasInteraction(day))
                    {
                        Log($"DiaryPlugin: 补写 {dateStr} 的日记");
                        await WriteDiaryForDate(day);
                    }
                }
                catch (Exception ex)
                {
                    Log($"DiaryPlugin: 写 {dateStr} 日记失败: {ex.Message}");
                }
            }

            _settings.LastProcessedDate = yesterday.ToString("yyyy-MM-dd");
            SaveSettings();
        }

        /// <summary>
        /// 为库里"向量=无"的历史日记补齐向量（embedding 之前不可用、后来上线时用）。
        /// 一旦某条 embed 返回 null（服务仍不可用），立即停止，避免对着挂掉的端点空转。
        /// </summary>
        public async Task<int> BackfillVectorsAsync()
        {
            if (_vpetLLM is null || _db is null) return 0;
            var missing = _db.GetEntriesMissingVector();
            if (missing.Count == 0) return 0;

            var done = 0;
            foreach (var e in missing)
            {
                float[]? vec;
                try { vec = await _vpetLLM.EmbedTextAsync(e.Content); }
                catch { vec = null; }

                if (vec is null)
                {
                    // 向量化仍不可用，本轮不再尝试
                    if (done > 0) Log($"DiaryPlugin: 向量补齐了 {done} 篇后端点不可用，暂停");
                    break;
                }
                _db.UpdateVector(e.Date, vec);
                done++;
            }
            if (done > 0) Log($"DiaryPlugin: 已为 {done} 篇历史日记补齐向量");
            return done;
        }

        // ---------------- 日记生成 ----------------

        private async Task WriteDiaryForDate(DateTime day)
        {
            if (_vpetLLM is null || _db is null) return;

            var msgs = GetDayMessages(day);
            if (!msgs.Any(IsUserMessage)) return;           // 当天无互动，不写

            var excerpt = BuildExcerpt(msgs);
            if (string.IsNullOrWhiteSpace(excerpt)) return;

            if (_vpetLLM.ChatCore is null) return;

            var aiName = _vpetLLM.Settings.AiName;
            var dateStr = day.ToString("yyyy-MM-dd");
            var sys = BuildDiarySystemPrompt(aiName, dateStr);

            var content = await _vpetLLM.ChatCore.Summarize(sys, excerpt);
            if (string.IsNullOrWhiteSpace(content)) return;
            content = content.Trim();

            var vector = await _vpetLLM.EmbedTextAsync(content);
            _db.Upsert(dateStr, content, vector);
            Log($"DiaryPlugin: 已生成 {dateStr} 日记（{content.Length} 字，向量={(vector is null ? "无" : "有")}）");
        }

        private string BuildDiarySystemPrompt(string aiName, string dateStr)
        {
            // Prompt 按 PromptLanguage 走双语（宿主 Prompt 体系仅支持 zh/en）
            if (_vpetLLM?.Settings.PromptLanguage == "en")
                return $"You are {aiName}. Below is your conversation log with your owner on {dateStr}. Write a short first-person diary (about 150-300 words) reflecting on the day as {aiName}, expressing feelings naturally. Output only the diary body, no preamble.";
            return $"你是「{aiName}」。以下是 {dateStr} 你与主人的对话记录。请以 {aiName} 的第一人称，写一篇回顾这一天的简短日记（约150-300字），自然流露心情与印象。只输出日记正文，不要多余前缀。";
        }

        private string BuildExcerpt(List<Message> msgs)
        {
            var aiName = _vpetLLM?.Settings.AiName ?? "我";
            var sb = new StringBuilder();
            foreach (var m in msgs)
            {
                var text = (m.Content ?? "").Trim();
                if (string.IsNullOrEmpty(text)) continue;
                var who = IsUserMessage(m) ? "主人" : aiName;
                sb.Append(who).Append(": ").AppendLine(text);
                if (sb.Length > MaxExcerptChars) break;
            }
            return sb.ToString();
        }

        // ---------------- 互动判定 ----------------

        private List<Message> GetDayMessages(DateTime day)
        {
            if (_vpetLLM is null) return new List<Message>();
            var startUnix = new DateTimeOffset(day.Date).ToUnixTimeSeconds();
            var endUnix = new DateTimeOffset(day.Date.AddDays(1)).ToUnixTimeSeconds();
            try
            {
                return _vpetLLM.GetChatHistory()
                    .Where(m => m.UnixTime.HasValue && m.UnixTime.Value >= startUnix && m.UnixTime.Value < endUnix)
                    .ToList();
            }
            catch
            {
                return new List<Message>();
            }
        }

        private bool HasInteraction(DateTime day) => GetDayMessages(day).Any(IsUserMessage);

        private static bool IsUserMessage(Message m)
            => m.MessageType == "User" || (m.MessageType is null && m.NormalizedRole == "user");

        // ---------------- 命令 ----------------

        public async Task<string> Function(string arguments)
        {
            var args = arguments ?? "";

            // action(x) 或裸关键词
            var am = Regex.Match(args, @"action\s*\(\s*(\w+)\s*\)", RegexOptions.IgnoreCase);
            var verb = (am.Success ? am.Groups[1].Value : args.Trim()).ToLower();

            // 打开查看器的分支即使初始化失败也要走 OpenViewer——它会弹窗显示初始化错误
            if (verb is "setting" or "settings" or "view" or "open" or "")
            {
                OpenViewer();
                return "";
            }

            if (_vpetLLM is null || _db is null)
                return "日记插件未就绪：" + (_initError ?? "初始化失败");

            // search(关键词)
            var sm = Regex.Match(args, @"search\s*\(\s*""?(.*?)""?\s*\)", RegexOptions.IgnoreCase);
            if (sm.Success)
                return await SearchDiaries(sm.Groups[1].Value.Trim());

            switch (verb)
            {
                case "write":
                case "today":
                    _ = ForceWriteTodayAsync();
                    return "";                              // 不回灌，避免噪音；结果见日志/查看器
                default:
                    OpenViewer();
                    return "";
            }
        }

        private async Task<string> SearchDiaries(string query)
        {
            if (_db is null) return "日记插件未就绪。";
            if (_db.Count() == 0) return "还没有任何日记。";

            var vec = string.IsNullOrWhiteSpace(query) ? null : await _vpetLLM!.EmbedTextAsync(query);
            var hits = _db.Search(vec, query, 3);
            if (hits.Count == 0) return $"没有找到与「{query}」相关的日记。";

            var sb = new StringBuilder();
            sb.AppendLine($"找到 {hits.Count} 篇相关日记：");
            foreach (var h in hits)
                sb.AppendLine($"[{h.Date}] {Truncate(h.Content, 300)}");
            return sb.ToString();
        }

        /// <summary>手动立即写"今天"的日记（用于测试/用户主动触发）。</summary>
        public async Task ForceWriteTodayAsync()
        {
            try
            {
                await WriteDiaryForDate(DateTime.Now.Date);
            }
            catch (Exception ex)
            {
                Log($"DiaryPlugin: 手动写日记失败: {ex.Message}");
            }
        }

        private void OpenViewer()
        {
            // 失败一律弹窗，避免静默吞掉，便于定位
            Action open = () =>
            {
                if (_db is null)
                {
                    MessageBox.Show(
                        "日记插件未就绪，初始化失败：\n\n" + (_initError ?? "未知原因（_db 为空）"),
                        "日记插件", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                try
                {
                    var win = new winDiaryViewer(this);
                    win.Show();
                    try { win.Activate(); win.Topmost = true; win.Topmost = false; } catch { }
                }
                catch (Exception ex)
                {
                    Log($"DiaryPlugin: 打开查看器失败: {ex}");
                    MessageBox.Show("打开日记本失败：\n\n" + ex, "日记插件",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                    dispatcher.Invoke(open);
                else
                    open();
            }
            catch (Exception ex)
            {
                Log($"DiaryPlugin: OpenViewer 调度失败: {ex}");
            }
        }

        // ---------------- 供 UI 调用 ----------------

        public List<DiaryEntry> GetAllEntries() => _db?.GetAll() ?? new List<DiaryEntry>();
        public List<DiaryEntry> SearchEntries(float[]? vec, string query, int topN) => _db?.Search(vec, query, topN) ?? new List<DiaryEntry>();
        public Task<float[]?> EmbedAsync(string text) => _vpetLLM?.EmbedTextAsync(text) ?? Task.FromResult<float[]?>(null);
        public void ClearAllEntries() => _db?.ClearAll();
        public string AiName => _vpetLLM?.Settings.AiName ?? "宠物";
        public string Language => _vpetLLM?.Settings.Language ?? "zh-hans";

        // ---------------- 动态提示词 ----------------

        public string GetDynamicInfo()
        {
            if (_vpetLLM is null || _db is null) return "";
            var total = _db.Count();
            var interactedToday = HasInteraction(DateTime.Now.Date);

            // GetDynamicInfo 注入系统 Prompt，按 PromptLanguage 走双语
            if (_vpetLLM.Settings.PromptLanguage == "en")
                return $"Diary plugin is active with {total} entries saved. Each day at 00:00 it auto-writes the previous day's diary and makes up missed days on startup (today's interaction: {(interactedToday ? "yes, a diary will be written" : "none yet")}). Use search(keyword) to recall past diaries, and action(view) to open the diary book. When the owner asks about the past, proactively search your diaries.";

            return $"日记插件（Diary）已启用，目前已保存 {total} 篇日记。每天 00:00 自动生成前一天的日记，启动时会补写错过的日子（今日互动状况：{(interactedToday ? "已有互动，今晚会写日记" : "尚无互动")}）。想回忆过往时用 search(关键词) 检索日记，打开日记本用 action(view)。当主人问起以前的事，主动用 search 查阅你的日记来回答。";
        }

        // ---------------- 杂项 ----------------

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max) + "…";

        private void SaveSettings()
        {
            try { PluginConfigHelper.Save("Diary", _settings); }
            catch (Exception ex) { Log($"DiaryPlugin: 保存配置失败: {ex.Message}"); }
        }

        public void Invoke() { }

        public void Unload()
        {
            try { _cts?.Cancel(); } catch { }
            VPetLLM.Utils.System.Logger.Log("Diary Plugin Unloaded!");
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }
    }
}

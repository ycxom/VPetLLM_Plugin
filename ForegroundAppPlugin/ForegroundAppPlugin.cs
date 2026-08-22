using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Infrastructure.Configuration;

namespace ForegroundAppPlugin
{
    public partial class ForegroundAppPlugin : IPluginTab, IVPetLLMPlugin, IDynamicInfoPlugin, IActionPlugin, IPluginWithData, ILocalizedDescription
    {
        public string Name => "ForegroundAppWatcher";
        public string Author => "ycxom";
        public string Description => Lang.T(CurrentLanguage, "plugin_desc");

        /// <summary>未初始化时也能取描述，宿主就不必为了显示一行字临时启停插件。</summary>
        public string GetLocalizedDescription(VPetLLM.VPetLLM host)
            => Lang.T(host?.Settings?.Language, "plugin_desc");

        internal string CurrentLanguage => _vpetLLM?.Settings?.Language ?? Lang.Hans;

        public string Parameters => "setting";
        public string Examples => "";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string PluginDataDir { get; set; } = "";

        private string _currentForegroundAppName = "Unknown";
        private VPetLLM.VPetLLM? _vpetLLM;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _monitoringTask;
        private Setting _setting = new Setting();

        // SMTC 跑在自己的专用线程上：原始 COM 接口指针不能跨单元漂移，
        // 而 async/await 会在线程池线程之间跳，所以这里不用 Task。
        private readonly object _mediaLock = new object();
        private Thread? _mediaThread;
        private ManualResetEventSlim? _mediaStopSignal;
        private volatile MediaSnapshot? _currentMedia;
        private string _lastNotifiedTrackKey = string.Empty;
        private volatile FilterDecision _lastFilterDecision = FilterDecision.Allow;
        private string _lastLoggedZombie = string.Empty;

        // 「声称在播、进度却不动」的连续观测计数
        private string _staleTrackKey = string.Empty;
        private long _stalePositionTicks = -1;
        private int _staleHits;
        private bool _staleLogged;

        /// <summary>连续多少轮进度纹丝不动才判定为残影。默认 5 秒一轮，3 次即约 15 秒。</summary>
        private const int StaleHitThreshold = 3;

        public class Setting
        {
            [JsonProperty("jitter_delay")]
            public int JitterDelay { get; set; } = 30;

            [JsonProperty("enable_media")]
            public bool EnableMedia { get; set; } = true;

            [JsonProperty("media_poll_interval")]
            public int MediaPollInterval { get; set; } = 5;

            [JsonProperty("media_in_dynamic_info")]
            public bool MediaInDynamicInfo { get; set; } = true;

            [JsonProperty("notify_on_track_change")]
            public bool NotifyOnTrackChange { get; set; } = false;

            /// <summary>命中即整条静默：不发消息、也不进上下文。隐私闸门，优先于名单。</summary>
            [JsonProperty("privacy_keywords")]
            public List<string> PrivacyKeywords { get; set; } = new List<string>();

            /// <summary>0=关闭 1=白名单 2=黑名单</summary>
            [JsonProperty("filter_mode")]
            public int FilterMode { get; set; } = (int)AppFilterMode.Off;

            /// <summary>名单内容，按进程名匹配（带不带 .exe 都认）。</summary>
            [JsonProperty("filter_list")]
            public List<string> FilterList { get; set; } = new List<string>();

            /// <summary>关键词是否也用于过滤 SMTC 曲目信息。</summary>
            [JsonProperty("apply_keywords_to_media")]
            public bool ApplyKeywordsToMedia { get; set; } = true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                int length = GetWindowTextLength(hWnd);
                if (length == 0) return string.Empty;

                var sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            LoadSetting();
            _cancellationTokenSource = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitorForegroundApp(_cancellationTokenSource.Token));
            StartMediaMonitor();
            VPetLLM.Utils.System.Logger.Log("Foreground App Plugin Initialized and monitoring started!");
        }

        private async Task MonitorForegroundApp(CancellationToken token)
        {
            string lastAppInfo = string.Empty;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    IntPtr hWnd = GetForegroundWindow();
                    if (hWnd != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(hWnd, out uint processId);
                        using var proc = Process.GetProcessById((int)processId);
                        string processName = proc.ProcessName;
                        string windowTitle = GetWindowTitle(hWnd);

                        // 桌宠自己被点到前台不算切换，忽略。
                        if (processName != "VPet-Simulator.Windows")
                        {
                            string currentAppInfo = string.IsNullOrWhiteSpace(windowTitle)
                                ? processName
                                : $"{processName}: {windowTitle}";

                            var decision = EvaluateForeground(processName, windowTitle);
                            if (decision != FilterDecision.Allow)
                            {
                                // 被拦下的应用不仅不发消息，还要把已记录的前台应用清掉：
                                // 否则 AI 仍会从上下文里读到用户上一个应用，等于没拦住。
                                _currentForegroundAppName = "Unknown";
                                _lastFilterDecision = decision;
                                if (currentAppInfo != lastAppInfo)
                                {
                                    lastAppInfo = currentAppInfo;
                                    // 只记进程名，不把被拦下的窗口标题写进日志——标题本身可能就是隐私内容。
                                    _vpetLLM?.Log($"Foreground app suppressed by {decision}: {processName}");
                                }
                            }
                            else if (currentAppInfo != lastAppInfo)
                            {
                                lastAppInfo = currentAppInfo;
                                _currentForegroundAppName = currentAppInfo;
                                _lastFilterDecision = FilterDecision.Allow;
                                _vpetLLM?.Log($"New foreground app detected: {currentAppInfo}");

                                string chatMessage = string.IsNullOrWhiteSpace(windowTitle)
                                    ? $"The user is now using the application: {processName}, Time: {DateTime.Now}"
                                    : $"The user is now using the application: {processName} with window title: \"{windowTitle}\", Time: {DateTime.Now}";
                                VPetLLM.Handlers.Actions.PluginHandler.SendPluginMessage("ForegroundAppWatcher", chatMessage);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _vpetLLM?.Log($"ForegroundAppPlugin: Error monitoring foreground app: {ex.Message}");
                    _currentForegroundAppName = "Unknown";
                }

                try
                {
                    await Task.Delay(Math.Max(1, _setting.JitterDelay) * 1000, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        // ---- 隐私过滤 ----

        private AppFilterMode CurrentFilterMode
        {
            get
            {
                var raw = _setting.FilterMode;
                return Enum.IsDefined(typeof(AppFilterMode), raw) ? (AppFilterMode)raw : AppFilterMode.Off;
            }
        }

        private FilterDecision EvaluateForeground(string processName, string windowTitle)
            => AppFilter.Evaluate(processName, windowTitle, _setting.PrivacyKeywords, CurrentFilterMode, _setting.FilterList);

        /// <summary>曲目信息也可能带隐私（播客标题、私人歌单名），命中关键词就整条丢掉。</summary>
        private bool IsMediaSuppressed(MediaSnapshot snapshot)
            => _setting.ApplyKeywordsToMedia
               && AppFilter.MatchesAnyKeyword(_setting.PrivacyKeywords,
                                              snapshot.Title, snapshot.Artist, snapshot.Album, snapshot.AppName, snapshot.AppId);

        // ---- SMTC 媒体监视 ----

        private void StartMediaMonitor()
        {
            lock (_mediaLock)
            {
                // 宿主停用再启用会在同一个实例上走 Unload -> Initialize，
                // 先确认上一个监视线程真的退干净了，再起新的。
                StopMediaMonitorLocked(joinTimeoutMs: 5000);

                var stopSignal = new ManualResetEventSlim(false);
                var thread = new Thread(() => MonitorMedia(stopSignal))
                {
                    IsBackground = true,
                    Name = "ForegroundAppPlugin.Smtc"
                };
                _mediaStopSignal = stopSignal;
                _mediaThread = thread;
                thread.Start();
            }
        }

        private void StopMediaMonitor(int joinTimeoutMs)
        {
            lock (_mediaLock)
            {
                StopMediaMonitorLocked(joinTimeoutMs);
            }
        }

        private void StopMediaMonitorLocked(int joinTimeoutMs)
        {
            var thread = _mediaThread;
            var stopSignal = _mediaStopSignal;
            _mediaThread = null;
            _mediaStopSignal = null;
            if (stopSignal == null) return;

            stopSignal.Set();

            bool exited = thread == null
                          || !thread.IsAlive
                          || ReferenceEquals(thread, Thread.CurrentThread)
                          || thread.Join(joinTimeoutMs);

            // 没等到退出就不释放事件对象：宁可漏一个 ManualResetEventSlim，
            // 也不能让还在 Wait 的后台线程撞上 ObjectDisposedException。
            if (exited) stopSignal.Dispose();
        }

        private void MonitorMedia(ManualResetEventSlim stopSignal)
        {
            SmtcInterop.Initialize();
            try
            {
                while (!stopSignal.IsSet)
                {
                    try
                    {
                        if (_setting.EnableMedia)
                        {
                            var snapshot = SmtcInterop.TryGetSnapshot();
                            if (snapshot == null)
                            {
                                ResetStaleTracking();
                            }
                            else if (IsStalePlayback(snapshot) || (snapshot.IsActive && IsMediaSuppressed(snapshot)))
                            {
                                snapshot = null;
                            }
                            _currentMedia = snapshot != null && snapshot.IsActive ? snapshot : null;
                            NotifyTrackChangeIfNeeded(_currentMedia);
                            LogZombieOnce();
                        }
                        else
                        {
                            _currentMedia = null;
                            _lastNotifiedTrackKey = string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        _vpetLLM?.Log($"ForegroundAppPlugin: Error reading SMTC session: {ex.Message}");
                        _currentMedia = null;
                    }

                    stopSignal.Wait(Math.Max(1, _setting.MediaPollInterval) * 1000);
                }
            }
            finally
            {
                SmtcInterop.Shutdown();
            }
        }

        /// <summary>
        /// 会话声称在播，进度却纹丝不动——这是播放器没正确注销 SMTC 留下的残影。
        ///
        /// 网易云音乐属于这一类：关掉播放后进程常驻托盘，SMTC 会话仍挂在 Playing 上，
        /// 曲目停在最后一首。因为进程确实还活着，靠"源进程还在吗"是抓不到的，
        /// 只能看进度有没有往前走。实测正常播放时进度稳定推进，所以停滞是个可靠信号。
        /// </summary>
        private bool IsStalePlayback(MediaSnapshot snapshot)
        {
            if (snapshot.Status != SmtcPlaybackStatus.Playing)
            {
                ResetStaleTracking();
                return false;
            }

            // 直播流没有总时长，进度可能恒为 0，不参与判定，免得误杀。
            if (snapshot.Duration <= TimeSpan.Zero)
            {
                ResetStaleTracking();
                return false;
            }

            // 进度恒为 0 的多半是压根不上报 timeline 的播放器（实测 mpv 就是这样，
            // 明明在放，Position 一直是 0）。残影则会停在用户上次听到的那个非 0 位置，
            // 所以把 0 排除掉，既保住这类播放器，又几乎不影响残影识别。
            if (snapshot.Position <= TimeSpan.Zero)
            {
                ResetStaleTracking();
                return false;
            }

            var ticks = snapshot.Position.Ticks;
            if (snapshot.TrackKey == _staleTrackKey && ticks == _stalePositionTicks)
            {
                _staleHits++;
            }
            else
            {
                _staleTrackKey = snapshot.TrackKey;
                _stalePositionTicks = ticks;
                _staleHits = 0;
                _staleLogged = false;
            }

            if (_staleHits < StaleHitThreshold) return false;

            if (!_staleLogged)
            {
                _staleLogged = true;
                _vpetLLM?.Log($"ForegroundAppPlugin: Ignoring stale SMTC session from '{snapshot.AppId}' "
                              + $"- reports Playing but position has not advanced for {_staleHits} polls.");
            }
            return true;
        }

        private void ResetStaleTracking()
        {
            _staleTrackKey = string.Empty;
            _stalePositionTicks = -1;
            _staleHits = 0;
            _staleLogged = false;
        }

        /// <summary>
        /// 被判定为僵尸而丢弃的会话记一行日志，方便排查"明明关了还在报歌"。
        /// 同一个来源只记一次——轮询每几秒一轮，不加这层去重会把日志刷满。
        /// </summary>
        private void LogZombieOnce()
        {
            var zombie = SmtcInterop.LastSuppressedZombie;
            if (string.IsNullOrEmpty(zombie) || zombie == _lastLoggedZombie) return;
            _lastLoggedZombie = zombie;
            _vpetLLM?.Log($"ForegroundAppPlugin: Ignored stale SMTC session from '{zombie}' (its process is gone).");
        }

        private void NotifyTrackChangeIfNeeded(MediaSnapshot? snapshot)
        {
            if (!_setting.NotifyOnTrackChange) return;

            if (snapshot == null || snapshot.Status != SmtcPlaybackStatus.Playing)
            {
                // 停下来不清除记录：暂停再继续同一首歌不应该重复播报。
                return;
            }

            if (snapshot.TrackKey == _lastNotifiedTrackKey) return;
            _lastNotifiedTrackKey = snapshot.TrackKey;

            var message = $"The user is now listening to {DescribeTrack(snapshot)}, Time: {DateTime.Now}";
            _vpetLLM?.Log($"New track detected: {DescribeTrack(snapshot)}");
            VPetLLM.Handlers.Actions.PluginHandler.SendPluginMessage("ForegroundAppWatcher", message);
        }

        /// <summary>给 AI 看的曲目描述（英文，与本插件其他上下文文本保持一致）。</summary>
        private static string DescribeTrack(MediaSnapshot snapshot)
        {
            var text = new StringBuilder();
            text.Append('"').Append(snapshot.Title).Append('"');
            if (!string.IsNullOrWhiteSpace(snapshot.Artist)) text.Append(" by ").Append(snapshot.Artist);
            if (!string.IsNullOrWhiteSpace(snapshot.Album)) text.Append(" from the album \"").Append(snapshot.Album).Append('"');
            if (!string.IsNullOrWhiteSpace(snapshot.AppName)) text.Append(" in ").Append(snapshot.AppName);
            return text.ToString();
        }

        public string GetDynamicInfo()
        {
            var parts = new StringBuilder();

            if (!string.IsNullOrEmpty(_currentForegroundAppName) && _currentForegroundAppName != "Unknown")
            {
                if (_currentForegroundAppName.Contains(": "))
                {
                    var split = _currentForegroundAppName.Split(new[] { ": " }, 2, StringSplitOptions.None);
                    parts.Append($"The user is currently using the application: {split[0]} with window title: \"{split[1]}\"");
                }
                else
                {
                    parts.Append($"The user is currently using the application: {_currentForegroundAppName}");
                }
            }

            var media = _currentMedia;
            if (_setting.MediaInDynamicInfo && media != null && media.IsActive)
            {
                if (parts.Length > 0) parts.Append('\n');
                var verb = media.Status == SmtcPlaybackStatus.Playing ? "is listening to" : "has paused";
                parts.Append($"The user {verb} {DescribeTrack(media)}");
                if (media.Duration > TimeSpan.Zero)
                {
                    parts.Append($" ({FormatTime(media.Position)}/{FormatTime(media.Duration)})");
                }
            }

            return parts.ToString();
        }

        private static string FormatTime(TimeSpan value)
            => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

        /// <summary>设置面板的"当前识别结果"预览。只读缓存，不在 UI 线程碰 COM。</summary>
        internal string GetForegroundAppPreview()
        {
            switch (_lastFilterDecision)
            {
                case FilterDecision.BlockedByKeyword:
                    return Lang.T(CurrentLanguage, "preview_blocked_keyword");
                case FilterDecision.BlockedByList:
                    return Lang.T(CurrentLanguage, "preview_blocked_list");
            }

            return string.IsNullOrEmpty(_currentForegroundAppName) || _currentForegroundAppName == "Unknown"
                ? Lang.T(CurrentLanguage, "preview_no_app")
                : _currentForegroundAppName;
        }

        internal string GetMediaPreview()
        {
            if (!_setting.EnableMedia) return Lang.T(CurrentLanguage, "preview_media_off");

            var media = _currentMedia;
            if (media == null || !media.IsActive) return Lang.T(CurrentLanguage, "preview_no_media");

            var statusKey = media.Status switch
            {
                SmtcPlaybackStatus.Playing => "status_playing",
                SmtcPlaybackStatus.Paused => "status_paused",
                _ => "status_stopped"
            };

            var text = new StringBuilder();
            text.Append(media.Title);
            if (!string.IsNullOrWhiteSpace(media.Artist)) text.Append(" — ").Append(media.Artist);
            text.Append('\n').Append(Lang.T(CurrentLanguage, statusKey));
            if (!string.IsNullOrWhiteSpace(media.AppName)) text.Append(" · ").Append(media.AppName);
            if (media.Duration > TimeSpan.Zero)
                text.Append(" · ").Append(FormatTime(media.Position)).Append('/').Append(FormatTime(media.Duration));
            return text.ToString();
        }

        public void Unload()
        {
            _cancellationTokenSource?.Cancel();
            // 宿主给 Unload 的预算是 5 秒，这里只花一小部分等 SMTC 线程收尾。
            StopMediaMonitor(joinTimeoutMs: 1500);
            VPetLLM.Utils.System.Logger.Log("Foreground App Plugin Unload signal sent.");
        }

        public void Log(string message)
        {
            if (_vpetLLM is null) return;
            _vpetLLM.Log(message);
        }

        public Task<string> Function(string arguments)
        {
            var actionMatch = new Regex(@"action\((\w+)\)").Match(arguments);
            if (actionMatch.Success)
            {
                var action = actionMatch.Groups[1].Value.ToLower();
                if (action == "setting")
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        new Window
                        {
                            Title = TabTitle,
                            Width = 440,
                            Height = 620,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Content = CreatePanel()
                        }.Show();
                    });
                    return Task.FromResult(Lang.T(CurrentLanguage, "msg_settings_opened"));
                }
            }
            return Task.FromResult(Lang.T(CurrentLanguage, "msg_invalid_action"));
        }

        public void SaveSetting()
        {
            PluginConfigHelper.Save("ForegroundAppWatcher", _setting);
        }

        private void LoadSetting()
        {
            _setting = PluginConfigHelper.Load<Setting>("ForegroundAppWatcher");
        }

        internal Setting CurrentSetting => _setting;

        /// <summary>面板保存入口：一次写入所有设置，避免逐项保存反复落盘。</summary>
        internal void ApplySetting(Setting updated)
        {
            bool mediaWasEnabled = _setting.EnableMedia;

            _setting.JitterDelay = Math.Max(1, updated.JitterDelay);
            _setting.EnableMedia = updated.EnableMedia;
            _setting.MediaPollInterval = Math.Max(1, updated.MediaPollInterval);
            _setting.MediaInDynamicInfo = updated.MediaInDynamicInfo;
            _setting.NotifyOnTrackChange = updated.NotifyOnTrackChange;
            _setting.PrivacyKeywords = updated.PrivacyKeywords ?? new List<string>();
            _setting.FilterMode = updated.FilterMode;
            _setting.FilterList = updated.FilterList ?? new List<string>();
            _setting.ApplyKeywordsToMedia = updated.ApplyKeywordsToMedia;

            // 过滤规则一改，之前基于旧规则记下来的状态就不作数了，清掉重新判定。
            _currentForegroundAppName = "Unknown";
            _lastFilterDecision = FilterDecision.Allow;

            if (mediaWasEnabled && !_setting.EnableMedia)
            {
                _currentMedia = null;
                _lastNotifiedTrackKey = string.Empty;
            }

            SaveSetting();
        }

        public int GetJitterDelay() => _setting.JitterDelay;

        public void SetJitterDelay(int delay)
        {
            _setting.JitterDelay = Math.Max(1, delay);
            SaveSetting();
        }
    }
}

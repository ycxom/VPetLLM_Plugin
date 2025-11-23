using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using VPetLLM.Core;

namespace ForegroundAppPlugin
{
    public class ForegroundAppPlugin : IVPetLLMPlugin, IDynamicInfoPlugin, IActionPlugin, IPluginWithData
    {
        public string Name => "ForegroundAppWatcher";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM == null) return "监视前台应用程序并将其详细信息（包括窗口标题）提供给 AI。";
                switch (_vpetLLM.Settings.Language)
                {
                    case "ja":
                        return "フォアグラウンドアプリケーションを監視し、その詳細情報（ウィンドウタイトルを含む）をAIに提供します。";
                    case "zh-hans":
                        return "监视前台应用程序并将其详细信息（包括窗口标题）提供给 AI。";
                    case "zh-hant":
                        return "監視前臺應用程序並將其詳細信息（包括窗口標題）提供給 AI。";
                    case "en":
                    default:
                        return "Monitors the foreground application and provides detailed information (including window title) to the AI.";
                }
            }
        }
        public string Parameters => "setting";
        public string Examples => "Example: `<|plugin_ForegroundAppWatcher_begin|> action(setting) <|plugin_ForegroundAppWatcher_end|>`";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string PluginDataDir { get; set; } = "";

        private string _currentForegroundAppName = "Unknown";
        private VPetLLM.VPetLLM? _vpetLLM;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _monitoringTask;
        private Setting _setting = new Setting();
        private const string SettingFileName = "ForegroundAppPlugin.json";

        public class Setting
        {
            [JsonProperty("jitter_delay")]
            public int JitterDelay { get; set; } = 30;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                int length = GetWindowTextLength(hWnd);
                if (length == 0) return string.Empty;

                var sb = new System.Text.StringBuilder(length + 1);
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
            VPetLLM.Utils.Logger.Log("Foreground App Plugin Initialized and monitoring started!");
        }

        private async Task MonitorForegroundApp(CancellationToken token)
        {
            string lastAppInfo = string.Empty;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    IntPtr hWnd = GetForegroundWindow();
                    if (hWnd == IntPtr.Zero) continue;

                    GetWindowThreadProcessId(hWnd, out uint processId);
                    Process proc = Process.GetProcessById((int)processId);
                    string processName = proc.ProcessName;
                    string windowTitle = GetWindowTitle(hWnd);

                    // 构建详细的应用信息
                    string currentAppInfo;
                    if (!string.IsNullOrWhiteSpace(windowTitle))
                    {
                        currentAppInfo = $"{processName}: {windowTitle}";
                    }
                    else
                    {
                        currentAppInfo = processName;
                    }

                    if (currentAppInfo != lastAppInfo)
                    {
                        if (processName == "VPet-Simulator.Windows")
                        {
                            continue;
                        }
                        lastAppInfo = currentAppInfo;
                        _currentForegroundAppName = currentAppInfo;
                        _vpetLLM?.Log($"New foreground app detected: {currentAppInfo}");
                        
                        // 发送更详细的信息给AI
                        string chatMessage;
                        if (!string.IsNullOrWhiteSpace(windowTitle))
                        {
                            chatMessage = $"The user is now using the application: {processName} with window title: \"{windowTitle}\", Time: {DateTime.Now}";
                        }
                        else
                        {
                            chatMessage = $"The user is now using the application: {processName}, Time: {DateTime.Now}";
                        }
                        VPetLLM.Handlers.PluginHandler.SendPluginMessage("ForegroundAppWatcher", chatMessage);
                    }
                }
                catch (Exception ex)
                {
                    _vpetLLM?.Log($"ForegroundAppPlugin: Error monitoring foreground app: {ex.Message}");
                    _currentForegroundAppName = "Unknown";
                }
                await Task.Delay(_setting.JitterDelay * 1000, token);
            }
        }

        public string GetDynamicInfo()
        {
            if (!string.IsNullOrEmpty(_currentForegroundAppName) && _currentForegroundAppName != "Unknown")
            {
                // 如果包含窗口标题信息，提供更详细的描述
                if (_currentForegroundAppName.Contains(": "))
                {
                    var parts = _currentForegroundAppName.Split(new[] { ": " }, 2, StringSplitOptions.None);
                    return $"The user is currently using the application: {parts[0]} with window title: \"{parts[1]}\"";
                }
                else
                {
                    return $"The user is currently using the application: {_currentForegroundAppName}";
                }
            }
            return string.Empty;
        }

        public void Unload()
        {
            _cancellationTokenSource?.Cancel();
            VPetLLM.Utils.Logger.Log("Foreground App Plugin Unload signal sent.");
        }

        public void Log(string message)
        {
            if (_vpetLLM == null) return;
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
                        var settingWindow = new winForegroundAppSetting(this);
                        settingWindow.Show();
                    });
                    return Task.FromResult("Setting window opened.");
                }
            }
            return Task.FromResult("Invalid action.");
        }

        public void SaveSetting()
        {
            if (string.IsNullOrEmpty(PluginDataDir)) return;
            var path = Path.Combine(PluginDataDir, SettingFileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(_setting, Formatting.Indented));
        }

        private void LoadSetting()
        {
            if (string.IsNullOrEmpty(PluginDataDir)) return;
            var path = Path.Combine(PluginDataDir, SettingFileName);
            if (File.Exists(path))
            {
                _setting = JsonConvert.DeserializeObject<Setting>(File.ReadAllText(path)) ?? new Setting();
            }
            else
            {
                _setting = new Setting();
            }
        }

        public int GetJitterDelay() => _setting.JitterDelay;

        public void SetJitterDelay(int delay)
        {
            _setting.JitterDelay = delay;
            SaveSetting();
        }
    }
}
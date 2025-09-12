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
                if (_vpetLLM == null) return "监视前台应用程序并将其名称提供给 AI。";
                switch (_vpetLLM.Settings.Language)
                {
                    case "ja":
                        return "フォアグラウンドアプリケーションを監視し、その名前をAIに提供します。";
                    case "zh-hans":
                        return "监视前台应用程序并将其名称提供给 AI。";
                    case "zh-hant":
                        return "監視前臺應用程序並將其名稱提供給 AI。";
                    case "en":
                    default:
                        return "Monitors the foreground application and provides the name to the AI.";
                }
            }
        }
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
        private const string SettingFileName = "ForegroundAppPlugin.json";

        public class Setting
        {
            [JsonProperty("jitter_delay")]
            public int JitterDelay { get; set; } = 3000;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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
            string lastAppName = string.Empty;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    IntPtr hWnd = GetForegroundWindow();
                    GetWindowThreadProcessId(hWnd, out uint processId);
                    Process proc = Process.GetProcessById((int)processId);
                    string currentAppName = proc.ProcessName;

                    if (currentAppName != lastAppName)
                    {
                        lastAppName = currentAppName;
                        _currentForegroundAppName = currentAppName;
                        _vpetLLM?.Log($"New foreground app detected: {currentAppName}");
                    }
                }
                catch (Exception ex)
                {
                    _vpetLLM?.Log($"ForegroundAppPlugin: Error monitoring foreground app: {ex.Message}");
                    _currentForegroundAppName = "Unknown";
                }
                await Task.Delay(_setting.JitterDelay, token);
            }
        }

        public string GetDynamicInfo()
        {
            if (!string.IsNullOrEmpty(_currentForegroundAppName) && _currentForegroundAppName != "Unknown")
            {
                return $"The user is currently using the application: {_currentForegroundAppName}";
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
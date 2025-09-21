using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VPetLLM.Core;

namespace AppLauncherPlugin
{
    public class AppLauncherPlugin : IVPetLLMPlugin, IActionPlugin, IPluginWithData
    {
        public string Name => "AppLauncher";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM == null) return "允许AI启动应用程序，支持自定义应用路径和自动识别系统应用。";
                switch (_vpetLLM.Settings.Language)
                {
                    case "ja":
                        return "AIがアプリケーションを起動できるようにし、カスタムアプリパスとシステムアプリの自動認識をサポートします。";
                    case "zh-hans":
                        return "允许AI启动应用程序，支持自定义应用路径和自动识别系统应用。";
                    case "zh-hant":
                        return "允許AI啟動應用程序，支持自定義應用路徑和自動識別系統應用。";
                    case "en":
                    default:
                        return "Allows AI to launch applications with support for custom app paths and automatic system app recognition.";
                }
            }
        }
        public string Parameters => "app_name|action(setting)";
        public string Examples => "Examples: `[:plugin(AppLauncher(notepad))]`, `[:plugin(AppLauncher(action(setting)))]`";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string PluginDataDir { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;
        private Setting _setting = new Setting();
        private const string SettingFileName = "AppLauncherPlugin.json";
        private Dictionary<string, string> _systemApps = new Dictionary<string, string>();
        private Dictionary<string, string> _startMenuApps = new Dictionary<string, string>();
        private Dictionary<string, SteamGame> _steamGames = new Dictionary<string, SteamGame>();

        public class CustomApp
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string Arguments { get; set; } = "";
            public AppType Type { get; set; } = AppType.Application;
        }

        public enum AppType
        {
            Application,
            SteamGame,
            UriProtocol
        }

        public class SteamGame
        {
            public string Name { get; set; } = "";
            public string GameId { get; set; } = "";
            public string InstallPath { get; set; } = "";
        }

        public class Setting
        {
            public List<CustomApp> CustomApps { get; set; } = new List<CustomApp>();
            public List<SteamGame> SteamGames { get; set; } = new List<SteamGame>();
            public bool EnableStartMenuScan { get; set; } = true;
            public bool EnableSystemApps { get; set; } = true;
            public bool EnableSteamGames { get; set; } = true;
            public bool LogLaunches { get; set; } = true;
            public string SteamPath { get; set; } = "";
        }

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            LoadSetting();
            InitializeSystemApps();
            if (_setting.EnableStartMenuScan)
            {
                ScanStartMenuApps();
            }
            if (_setting.EnableSteamGames)
            {
                ScanSteamGames();
            }
            VPetLLM.Utils.Logger.Log("App Launcher Plugin Initialized!");
        }

        private void InitializeSystemApps()
        {
            _systemApps.Clear();
            
            // 常见的系统应用
            var commonApps = new Dictionary<string, string>
            {
                {"notepad", "notepad.exe"},
                {"calculator", "calc.exe"},
                {"paint", "mspaint.exe"},
                {"cmd", "cmd.exe"},
                {"powershell", "powershell.exe"},
                {"explorer", "explorer.exe"},
                {"taskmgr", "taskmgr.exe"},
                {"regedit", "regedit.exe"},
                {"msconfig", "msconfig.exe"},
                {"control", "control.exe"},
                {"winver", "winver.exe"},
                {"charmap", "charmap.exe"},
                {"magnify", "magnify.exe"},
                {"osk", "osk.exe"},
                {"snip", "ms-screenclip:"},
                {"settings", "ms-settings:"}
            };

            foreach (var app in commonApps)
            {
                _systemApps[app.Key.ToLower()] = app.Value;
            }
        }

        private void ScanStartMenuApps()
        {
            _startMenuApps.Clear();
            
            try
            {
                string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                    "Microsoft", "Windows", "Start Menu", "Programs");
                
                if (Directory.Exists(startMenuPath))
                {
                    ScanDirectory(startMenuPath);
                }

                // 也扫描公共开始菜单
                string commonStartMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs");
                
                if (Directory.Exists(commonStartMenuPath))
                {
                    ScanDirectory(commonStartMenuPath);
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error scanning start menu: {ex.Message}");
            }
        }

        private void ScanDirectory(string directory)
        {
            try
            {
                foreach (string file in Directory.GetFiles(directory, "*.lnk", SearchOption.AllDirectories))
                {
                    try
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            _startMenuApps[fileName.ToLower()] = file;
                        }
                    }
                    catch
                    {
                        // 忽略单个文件的错误
                    }
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error scanning directory {directory}: {ex.Message}");
            }
        }

        public Task<string> Function(string arguments)
        {
            try
            {
                // 检查是否是action(setting)格式的设置命令
                var actionMatch = new Regex(@"action\((\w+)\)").Match(arguments);
                if (actionMatch.Success)
                {
                    var action = actionMatch.Groups[1].Value.ToLower();
                    if (action == "setting")
                    {
                        try
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var settingWindow = new winAppLauncherSetting(this);
                                settingWindow.Show();
                            });
                            return Task.FromResult("设置窗口已打开。");
                        }
                        catch (Exception ex)
                        {
                            _vpetLLM?.Log($"AppLauncher: Error opening settings: {ex.Message}");
                            return Task.FromResult($"打开设置窗口失败: {ex.Message}");
                        }
                    }
                    return Task.FromResult("无效的操作。");
                }

                // 检查是否是直接的setting命令（向后兼容）
                if (arguments.Trim().ToLower() == "setting")
                {
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var settingWindow = new winAppLauncherSetting(this);
                            settingWindow.Show();
                        });
                        return Task.FromResult("设置窗口已打开。");
                    }
                    catch (Exception ex)
                    {
                        _vpetLLM?.Log($"AppLauncher: Error opening settings: {ex.Message}");
                        return Task.FromResult($"打开设置窗口失败: {ex.Message}");
                    }
                }

                // 解析应用名称
                string appName = arguments.Trim().ToLower();
                if (string.IsNullOrEmpty(appName))
                {
                    return Task.FromResult("请指定要启动的应用程序名称。");
                }

                return Task.FromResult(LaunchApplication(appName));
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error in Function: {ex.Message}");
                return Task.FromResult($"启动应用时发生错误: {ex.Message}");
            }
        }

        private string LaunchApplication(string appName)
        {
            try
            {
                // 1. 首先检查自定义应用
                var customApp = _setting.CustomApps.FirstOrDefault(app => 
                    app.Name.Equals(appName, StringComparison.OrdinalIgnoreCase));
                
                if (customApp != null)
                {
                    return LaunchCustomApp(customApp);
                }

                // 2. 检查系统应用
                if (_setting.EnableSystemApps && _systemApps.ContainsKey(appName))
                {
                    return LaunchSystemApp(appName, _systemApps[appName]);
                }

                // 3. 检查Steam游戏
                if (_setting.EnableSteamGames && _steamGames.ContainsKey(appName))
                {
                    return LaunchSteamGame(appName, _steamGames[appName]);
                }

                // 4. 检查开始菜单应用
                if (_setting.EnableStartMenuScan && _startMenuApps.ContainsKey(appName))
                {
                    return LaunchStartMenuApp(appName, _startMenuApps[appName]);
                }

                // 5. 尝试直接启动（可能是完整路径或系统PATH中的程序）
                return LaunchDirectly(appName);
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error launching {appName}: {ex.Message}");
                return $"无法启动应用程序 '{appName}': {ex.Message}";
            }
        }

        private string LaunchCustomApp(CustomApp app)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                
                if (_setting.LogLaunches)
                {
                    _vpetLLM?.Log($"AppLauncher: Launched custom app '{app.Name}' from '{app.Path}'");
                }
                
                return $"已成功启动自定义应用程序: {app.Name}";
            }
            catch (Exception ex)
            {
                return $"启动自定义应用程序 '{app.Name}' 失败: {ex.Message}";
            }
        }

        private string LaunchSystemApp(string appName, string command)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                
                if (_setting.LogLaunches)
                {
                    _vpetLLM?.Log($"AppLauncher: Launched system app '{appName}' with command '{command}'");
                }
                
                return $"已成功启动系统应用程序: {appName}";
            }
            catch (Exception ex)
            {
                return $"启动系统应用程序 '{appName}' 失败: {ex.Message}";
            }
        }

        private string LaunchStartMenuApp(string appName, string shortcutPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = shortcutPath,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                
                if (_setting.LogLaunches)
                {
                    _vpetLLM?.Log($"AppLauncher: Launched start menu app '{appName}' from '{shortcutPath}'");
                }
                
                return $"已成功启动应用程序: {appName}";
            }
            catch (Exception ex)
            {
                return $"启动应用程序 '{appName}' 失败: {ex.Message}";
            }
        }

        private string LaunchDirectly(string appName)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = appName,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                
                if (_setting.LogLaunches)
                {
                    _vpetLLM?.Log($"AppLauncher: Launched directly '{appName}'");
                }
                
                return $"已成功启动应用程序: {appName}";
            }
            catch (Exception ex)
            {
                return $"未找到应用程序 '{appName}' 或启动失败: {ex.Message}";
            }
        }

        public void SaveSetting()
        {
            if (string.IsNullOrEmpty(PluginDataDir)) return;
            
            try
            {
                var path = Path.Combine(PluginDataDir, SettingFileName);
                var json = JsonSerializer.Serialize(_setting, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error saving settings: {ex.Message}");
            }
        }

        private void LoadSetting()
        {
            if (string.IsNullOrEmpty(PluginDataDir)) return;
            
            try
            {
                var path = Path.Combine(PluginDataDir, SettingFileName);
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    _setting = JsonSerializer.Deserialize<Setting>(json) ?? new Setting();
                }
                else
                {
                    _setting = new Setting();
                    // 添加一些默认的自定义应用示例
                    _setting.CustomApps.Add(new CustomApp 
                    { 
                        Name = "chrome", 
                        Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        Arguments = ""
                    });
                    _setting.CustomApps.Add(new CustomApp 
                    { 
                        Name = "vscode", 
                        Path = @"C:\Users\%USERNAME%\AppData\Local\Programs\Microsoft VS Code\Code.exe",
                        Arguments = ""
                    });
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error loading settings: {ex.Message}");
                _setting = new Setting();
            }
        }

        public Setting GetSetting() => _setting;

        public void RefreshApps()
        {
            InitializeSystemApps();
            if (_setting.EnableStartMenuScan)
            {
                ScanStartMenuApps();
            }
            if (_setting.EnableSteamGames)
            {
                ScanSteamGames();
            }
        }

        public List<string> GetAvailableApps()
        {
            var apps = new List<string>();
            
            // 添加自定义应用
            apps.AddRange(_setting.CustomApps.Select(app => $"[自定义] {app.Name}"));
            
            // 添加系统应用
            if (_setting.EnableSystemApps)
            {
                apps.AddRange(_systemApps.Keys.Select(key => $"[系统] {key}"));
            }
            
            // 添加Steam游戏
            if (_setting.EnableSteamGames)
            {
                apps.AddRange(_steamGames.Keys.Select(key => $"[Steam] {key}"));
            }

            // 添加开始菜单应用
            if (_setting.EnableStartMenuScan)
            {
                apps.AddRange(_startMenuApps.Keys.Select(key => $"[开始菜单] {key}"));
            }
            
            return apps.OrderBy(x => x).ToList();
        }

        public void Unload()
        {
            VPetLLM.Utils.Logger.Log("App Launcher Plugin Unloaded!");
        }



        private void ScanSteamGames()
        {
            _steamGames.Clear();
            
            try
            {
                // 尝试找到Steam安装路径
                string steamPath = FindSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    _vpetLLM?.Log("AppLauncher: Steam not found");
                    return;
                }

                _setting.SteamPath = steamPath;

                // 扫描Steam游戏库
                string steamAppsPath = Path.Combine(steamPath, "steamapps");
                if (Directory.Exists(steamAppsPath))
                {
                    // 扫描common文件夹中的游戏
                    string commonPath = Path.Combine(steamAppsPath, "common");
                    if (Directory.Exists(commonPath))
                    {
                        foreach (string gameDir in Directory.GetDirectories(commonPath))
                        {
                            string gameName = Path.GetFileName(gameDir).ToLower();
                            if (!string.IsNullOrEmpty(gameName))
                            {
                                // 尝试从ACF文件获取游戏ID
                                string gameId = GetGameIdFromAcf(steamAppsPath, gameName);
                                
                                var steamGame = new SteamGame
                                {
                                    Name = gameName,
                                    GameId = gameId,
                                    InstallPath = gameDir
                                };
                                
                                _steamGames[gameName] = steamGame;
                            }
                        }
                    }
                }

                // 添加一些常见的Steam游戏（如果用户有的话）
                AddCommonSteamGames();

                _vpetLLM?.Log($"AppLauncher: Found {_steamGames.Count} Steam games");
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error scanning Steam games: {ex.Message}");
            }
        }

        private string FindSteamPath()
        {
            try
            {
                // 常见的Steam安装路径
                string[] commonPaths = {
                    @"C:\Program Files (x86)\Steam",
                    @"C:\Program Files\Steam",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
                };

                foreach (string path in commonPaths)
                {
                    if (Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                    {
                        return path;
                    }
                }

                // 尝试从注册表获取Steam路径
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        if (key != null)
                        {
                            string steamPath = key.GetValue("SteamPath")?.ToString();
                            if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(steamPath))
                            {
                                return steamPath;
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略注册表访问错误
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error finding Steam path: {ex.Message}");
            }

            return "";
        }

        private string GetGameIdFromAcf(string steamAppsPath, string gameName)
        {
            try
            {
                foreach (string acfFile in Directory.GetFiles(steamAppsPath, "appmanifest_*.acf"))
                {
                    string content = File.ReadAllText(acfFile);
                    if (content.Contains($"\"{gameName}\"", StringComparison.OrdinalIgnoreCase))
                    {
                        // 提取游戏ID
                        var match = Regex.Match(content, @"""appid""\s*""(\d+)""");
                        if (match.Success)
                        {
                            return match.Groups[1].Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"AppLauncher: Error reading ACF files: {ex.Message}");
            }

            return "";
        }

        private void AddCommonSteamGames()
        {
            // 添加一些常见游戏的ID映射
            var commonGames = new Dictionary<string, string>
            {
                {"counter-strike 2", "730"},
                {"cs2", "730"},
                {"dota 2", "570"},
                {"dota2", "570"},
                {"team fortress 2", "440"},
                {"tf2", "440"},
                {"left 4 dead 2", "550"},
                {"l4d2", "550"},
                {"portal 2", "620"},
                {"half-life 2", "220"},
                {"garry's mod", "4000"},
                {"gmod", "4000"},
                {"rust", "252490"},
                {"grand theft auto v", "271590"},
                {"gtav", "271590"},
                {"gta5", "271590"},
                {"cyberpunk 2077", "1091500"},
                {"the witcher 3", "292030"},
                {"witcher3", "292030"},
                {"steam", "steam://open/main"}
            };

            foreach (var game in commonGames)
            {
                if (!_steamGames.ContainsKey(game.Key))
                {
                    _steamGames[game.Key] = new SteamGame
                    {
                        Name = game.Key,
                        GameId = game.Value,
                        InstallPath = ""
                    };
                }
            }
        }

        private string LaunchSteamGame(string gameName, SteamGame steamGame)
        {
            try
            {
                string steamUri;
                
                if (steamGame.GameId == "steam://open/main")
                {
                    // 特殊处理：打开Steam客户端
                    steamUri = steamGame.GameId;
                }
                else if (!string.IsNullOrEmpty(steamGame.GameId))
                {
                    // 使用Steam协议启动游戏
                    steamUri = $"steam://rungameid/{steamGame.GameId}";
                }
                else
                {
                    // 如果没有游戏ID，尝试直接启动可执行文件
                    if (!string.IsNullOrEmpty(steamGame.InstallPath) && Directory.Exists(steamGame.InstallPath))
                    {
                        var exeFiles = Directory.GetFiles(steamGame.InstallPath, "*.exe", SearchOption.TopDirectoryOnly);
                        if (exeFiles.Length > 0)
                        {
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = exeFiles[0],
                                UseShellExecute = true
                            };
                            Process.Start(startInfo);
                            
                            if (_setting.LogLaunches)
                            {
                                _vpetLLM?.Log($"AppLauncher: Launched Steam game '{gameName}' from '{exeFiles[0]}'");
                            }
                            
                            return $"已成功启动Steam游戏: {gameName}";
                        }
                    }
                    
                    return $"无法找到Steam游戏 '{gameName}' 的启动方式";
                }

                var startInfo2 = new ProcessStartInfo
                {
                    FileName = steamUri,
                    UseShellExecute = true
                };

                Process.Start(startInfo2);
                
                if (_setting.LogLaunches)
                {
                    _vpetLLM?.Log($"AppLauncher: Launched Steam game '{gameName}' with URI '{steamUri}'");
                }
                
                return $"已成功启动Steam游戏: {gameName}";
            }
            catch (Exception ex)
            {
                return $"启动Steam游戏 '{gameName}' 失败: {ex.Message}";
            }
        }

        public void Log(string message)
        {
            if (_vpetLLM == null) return;
            _vpetLLM.Log(message);
        }
    }
}
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;

namespace TerminalPlugin
{
    /// <summary>
    /// Terminal Plugin Settings
    /// </summary>
    public class TerminalSettings
    {
        public bool HasConfirmedWarning { get; set; } = false;
        public bool IsAuthorized { get; set; } = false;
    }

    /// <summary>
    /// Shell information for UI display
    /// </summary>
    public class ShellInfo
    {
        public string CurrentShell { get; set; } = "";
        public bool HasPwsh { get; set; }
        public bool HasPowerShell { get; set; }
    }

    public class TerminalPlugin : IActionPlugin, IDynamicInfoPlugin, IPluginWithData
    {
        public string Name => "Terminal";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM is null) return "允许AI执行终端命令，支持PowerShell和CMD。";
                switch (_vpetLLM.Settings.Language)
                {
                    case "ja":
                        return "AIがターミナルコマンドを実行できるようにし、PowerShellとCMDをサポートします。";
                    case "zh-hans":
                        return "允许AI执行终端命令，支持PowerShell和CMD。";
                    case "zh-hant":
                        return "允許AI執行終端命令，支持PowerShell和CMD。";
                    case "en":
                    default:
                        return "Allows AI to execute terminal commands, supporting PowerShell and CMD.";
                }
            }
        }
        public string Parameters => "command|action(setting|info)";
        public string Examples
        {
            get
            {
                if (!_settings.IsAuthorized)
                {
                    return "Examples: `<|plugin_Terminal_begin|> action(setting) <|plugin_Terminal_end|>` to open settings and authorize terminal access.";
                }
                if (_hasPowerShell)
                {
                    return "Examples: `<|plugin_Terminal_begin|> Get-Process <|plugin_Terminal_end|>`, `<|plugin_Terminal_begin|> dir <|plugin_Terminal_end|>`";
                }
                else
                {
                    return "Examples: `<|plugin_Terminal_begin|> dir <|plugin_Terminal_end|>`, `<|plugin_Terminal_begin|> ipconfig <|plugin_Terminal_end|>`";
                }
            }
        }
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string PluginDataDir { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;
        private bool _hasPowerShell = false;
        private bool _hasPwsh = false;
        private string _shellPath = "";
        private ShellType _currentShell = ShellType.Cmd;
        private TerminalSettings _settings = new TerminalSettings();
        private const int DefaultTimeoutSeconds = 30;
        private const int MaxOutputLength = 4000;
        private const string SettingFileName = "TerminalPlugin.json";

        public enum ShellType
        {
            Cmd,
            PowerShell,
            Pwsh
        }

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            LoadSettings();
            DetectAvailableShells();
            VPetLLM.Utils.System.Logger.Log($"Terminal Plugin Initialized! Using: {_currentShell} ({_shellPath}), Authorized: {_settings.IsAuthorized}");
        }

        /// <summary>
        /// Get current language setting
        /// </summary>
        public string GetLanguage()
        {
            return _vpetLLM?.Settings.Language ?? "en";
        }

        /// <summary>
        /// Get shell information for UI
        /// </summary>
        public ShellInfo GetShellInfo()
        {
            return new ShellInfo
            {
                CurrentShell = _currentShell.ToString(),
                HasPwsh = _hasPwsh,
                HasPowerShell = _hasPowerShell
            };
        }

        /// <summary>
        /// 检测系统中可用的终端
        /// </summary>
        private void DetectAvailableShells()
        {
            // 检测 pwsh (PowerShell Core / PowerShell 7+)
            var pwshPath = FindExecutableInPath("pwsh.exe");
            _hasPwsh = !string.IsNullOrEmpty(pwshPath);

            // 检测 powershell (Windows PowerShell 5.1)
            var psPath = FindExecutableInPath("powershell.exe");
            _hasPowerShell = !string.IsNullOrEmpty(psPath);

            // 优先级: pwsh > powershell > cmd
            if (_hasPwsh)
            {
                _currentShell = ShellType.Pwsh;
                _shellPath = pwshPath!;
            }
            else if (_hasPowerShell)
            {
                _currentShell = ShellType.PowerShell;
                _shellPath = psPath!;
            }
            else
            {
                _currentShell = ShellType.Cmd;
                _shellPath = FindExecutableInPath("cmd.exe") ?? "cmd.exe";
            }

            _vpetLLM?.Log($"Terminal: Detected shells - pwsh: {_hasPwsh} ({pwshPath ?? "N/A"}), powershell: {_hasPowerShell} ({psPath ?? "N/A"}), using: {_currentShell} ({_shellPath})");
        }

        /// <summary>
        /// 通过环境变量 PATH 查找可执行文件
        /// </summary>
        /// <param name="executableName">可执行文件名（如 pwsh.exe）</param>
        /// <returns>找到的完整路径，未找到返回 null</returns>
        private string? FindExecutableInPath(string executableName)
        {
            // 1. 首先检查已知的固定路径（更快）
            var knownPaths = GetKnownPaths(executableName);
            foreach (var path in knownPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // 2. 从环境变量 PATH 中查找
            try
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(pathEnv))
                {
                    return null;
                }

                // PATH 使用分号分隔
                var paths = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);

                foreach (var dir in paths)
                {
                    try
                    {
                        var trimmedDir = dir.Trim();
                        if (string.IsNullOrEmpty(trimmedDir))
                        {
                            continue;
                        }

                        var fullPath = Path.Combine(trimmedDir, executableName);
                        if (File.Exists(fullPath))
                        {
                            return fullPath;
                        }
                    }
                    catch
                    {
                        // 忽略无效路径
                    }
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error reading PATH environment: {ex.Message}");
            }

            // 3. 检查 PATHEXT 支持的扩展名（如果传入的名称没有扩展名）
            if (!executableName.Contains('.'))
            {
                return FindExecutableWithExtensions(executableName);
            }

            return null;
        }

        /// <summary>
        /// 获取已知的固定路径
        /// </summary>
        private string[] GetKnownPaths(string executableName)
        {
            var lowerName = executableName.ToLower();
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return lowerName switch
            {
                "pwsh.exe" => new[]
                {
                    // PowerShell 7+ 标准安装路径
                    Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
                    Path.Combine(programFilesX86, "PowerShell", "7", "pwsh.exe"),
                    // PowerShell 7 预览版
                    Path.Combine(programFiles, "PowerShell", "7-preview", "pwsh.exe"),
                    // 用户安装路径
                    Path.Combine(userProfile, ".dotnet", "tools", "pwsh.exe"),
                    // Scoop 安装
                    Path.Combine(userProfile, "scoop", "shims", "pwsh.exe"),
                    // Chocolatey 安装
                    Path.Combine(programFiles, "PowerShell", "pwsh.exe"),
                },
                "powershell.exe" => new[]
                {
                    // Windows PowerShell 5.1 标准路径
                    Path.Combine(winDir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                    Path.Combine(winDir, "SysWOW64", "WindowsPowerShell", "v1.0", "powershell.exe"),
                },
                "cmd.exe" => new[]
                {
                    // CMD 标准路径
                    Path.Combine(winDir, "System32", "cmd.exe"),
                    Path.Combine(winDir, "SysWOW64", "cmd.exe"),
                },
                _ => Array.Empty<string>()
            };
        }

        /// <summary>
        /// 使用 PATHEXT 环境变量中的扩展名查找可执行文件
        /// </summary>
        private string? FindExecutableWithExtensions(string executableName)
        {
            try
            {
                var pathExtEnv = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
                var extensions = pathExtEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                var paths = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);

                foreach (var dir in paths)
                {
                    foreach (var ext in extensions)
                    {
                        try
                        {
                            var fullPath = Path.Combine(dir.Trim(), executableName + ext);
                            if (File.Exists(fullPath))
                            {
                                return fullPath;
                            }
                        }
                        catch
                        {
                            // 忽略无效路径
                        }
                    }
                }
            }
            catch
            {
                // 忽略错误
            }

            return null;
        }

        /// <summary>
        /// 根据当前可用的Shell和授权状态返回动态Prompt信息
        /// </summary>
        public string GetDynamicInfo()
        {
            // 如果未授权，返回提示用户需要授权的信息
            if (!_settings.IsAuthorized)
            {
                return GetNotAuthorizedPrompt();
            }

            var lang = _vpetLLM?.Settings.Language ?? "en";

            if (_hasPwsh || _hasPowerShell)
            {
                // PowerShell 可用时的 Prompt
                return lang switch
                {
                    "zh-hans" => GetPowerShellPromptZhHans(),
                    "zh-hant" => GetPowerShellPromptZhHant(),
                    "ja" => GetPowerShellPromptJa(),
                    _ => GetPowerShellPromptEn()
                };
            }
            else
            {
                // 仅 CMD 可用时的 Prompt
                return lang switch
                {
                    "zh-hans" => GetCmdPromptZhHans(),
                    "zh-hant" => GetCmdPromptZhHant(),
                    "ja" => GetCmdPromptJa(),
                    _ => GetCmdPromptEn()
                };
            }
        }

        #region Not Authorized Prompt

        private string GetNotAuthorizedPrompt()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";
            return lang switch
            {
                "zh-hans" => "终端插件：用户尚未授权 AI 使用终端。如需使用终端功能，请使用 `<|plugin_Terminal_begin|> action(setting) <|plugin_Terminal_end|>` 打开设置进行授权。",
                "zh-hant" => "終端插件：用戶尚未授權 AI 使用終端。如需使用終端功能，請使用 `<|plugin_Terminal_begin|> action(setting) <|plugin_Terminal_end|>` 打開設置進行授權。",
                "ja" => "ターミナルプラグイン：ユーザーはまだAIにターミナルの使用を許可していません。ターミナル機能を使用するには、`<|plugin_Terminal_begin|> action(setting) <|plugin_Terminal_end|>` を使用して設定を開き、許可してください。",
                _ => "Terminal Plugin: User has not authorized AI to use terminal. To use terminal features, use `<|plugin_Terminal_begin|> action(setting) <|plugin_Terminal_end|>` to open settings and authorize."
            };
        }

        #endregion

        #region PowerShell Prompts

        private string GetPowerShellPromptEn()
        {
            var shellName = _hasPwsh ? "PowerShell 7 (pwsh)" : "Windows PowerShell";
            return $@"Terminal Plugin: {shellName} is available and authorized.
You can execute PowerShell commands to help the user. Use PowerShell syntax for commands.
Common PowerShell commands:
- Get-Process: List running processes
- Get-Service: List services
- Get-ChildItem (dir/ls): List files and directories
- Get-Content (cat/type): Read file content
- Set-Location (cd): Change directory
- Get-Date: Get current date/time
- Get-ComputerInfo: Get system information
- Invoke-WebRequest: Make web requests
Note: Commands are executed with current user privileges. Avoid destructive commands.";
        }

        private string GetPowerShellPromptZhHans()
        {
            var shellName = _hasPwsh ? "PowerShell 7 (pwsh)" : "Windows PowerShell";
            return $@"终端插件：{shellName} 可用且已授权。
你可以执行PowerShell命令来帮助用户。请使用PowerShell语法。
常用PowerShell命令：
- Get-Process: 列出运行中的进程
- Get-Service: 列出服务
- Get-ChildItem (dir/ls): 列出文件和目录
- Get-Content (cat/type): 读取文件内容
- Set-Location (cd): 切换目录
- Get-Date: 获取当前日期时间
- Get-ComputerInfo: 获取系统信息
- Invoke-WebRequest: 发送网络请求
注意：命令以当前用户权限执行，避免使用破坏性命令。";
        }

        private string GetPowerShellPromptZhHant()
        {
            var shellName = _hasPwsh ? "PowerShell 7 (pwsh)" : "Windows PowerShell";
            return $@"終端插件：{shellName} 可用且已授權。
你可以執行PowerShell命令來幫助用戶。請使用PowerShell語法。
常用PowerShell命令：
- Get-Process: 列出運行中的進程
- Get-Service: 列出服務
- Get-ChildItem (dir/ls): 列出文件和目錄
- Get-Content (cat/type): 讀取文件內容
- Set-Location (cd): 切換目錄
- Get-Date: 獲取當前日期時間
- Get-ComputerInfo: 獲取系統信息
- Invoke-WebRequest: 發送網路請求
注意：命令以當前用戶權限執行，避免使用破壞性命令。";
        }

        private string GetPowerShellPromptJa()
        {
            var shellName = _hasPwsh ? "PowerShell 7 (pwsh)" : "Windows PowerShell";
            return $@"ターミナルプラグイン：{shellName} が利用可能で許可されています。
PowerShellコマンドを実行してユーザーを支援できます。PowerShell構文を使用してください。
よく使うPowerShellコマンド：
- Get-Process: 実行中のプロセスを一覧表示
- Get-Service: サービスを一覧表示
- Get-ChildItem (dir/ls): ファイルとディレクトリを一覧表示
- Get-Content (cat/type): ファイル内容を読み取る
- Set-Location (cd): ディレクトリを変更
- Get-Date: 現在の日時を取得
- Get-ComputerInfo: システム情報を取得
- Invoke-WebRequest: Webリクエストを送信
注意：コマンドは現在のユーザー権限で実行されます。破壊的なコマンドは避けてください。";
        }

        #endregion

        #region CMD Prompts

        private string GetCmdPromptEn()
        {
            return @"Terminal Plugin: Only CMD is available (PowerShell not found) and authorized.
You can execute CMD commands to help the user. Use CMD syntax for commands.
Common CMD commands:
- dir: List files and directories
- type: Display file content
- cd: Change directory
- copy/move: Copy or move files
- del: Delete files
- mkdir: Create directory
- ipconfig: Show network configuration
- systeminfo: Show system information
- tasklist: List running processes
Note: Commands are executed with current user privileges. Avoid destructive commands.";
        }

        private string GetCmdPromptZhHans()
        {
            return @"终端插件：仅CMD可用（未检测到PowerShell）且已授权。
你可以执行CMD命令来帮助用户。请使用CMD语法。
常用CMD命令：
- dir: 列出文件和目录
- type: 显示文件内容
- cd: 切换目录
- copy/move: 复制或移动文件
- del: 删除文件
- mkdir: 创建目录
- ipconfig: 显示网络配置
- systeminfo: 显示系统信息
- tasklist: 列出运行中的进程
注意：命令以当前用户权限执行，避免使用破坏性命令。";
        }

        private string GetCmdPromptZhHant()
        {
            return @"終端插件：僅CMD可用（未檢測到PowerShell）且已授權。
你可以執行CMD命令來幫助用戶。請使用CMD語法。
常用CMD命令：
- dir: 列出文件和目錄
- type: 顯示文件內容
- cd: 切換目錄
- copy/move: 複製或移動文件
- del: 刪除文件
- mkdir: 創建目錄
- ipconfig: 顯示網路配置
- systeminfo: 顯示系統信息
- tasklist: 列出運行中的進程
注意：命令以當前用戶權限執行，避免使用破壞性命令。";
        }

        private string GetCmdPromptJa()
        {
            return @"ターミナルプラグイン：CMDのみ利用可能です（PowerShellが見つかりません）、許可されています。
CMDコマンドを実行してユーザーを支援できます。CMD構文を使用してください。
よく使うCMDコマンド：
- dir: ファイルとディレクトリを一覧表示
- type: ファイル内容を表示
- cd: ディレクトリを変更
- copy/move: ファイルをコピーまたは移動
- del: ファイルを削除
- mkdir: ディレクトリを作成
- ipconfig: ネットワーク構成を表示
- systeminfo: システム情報を表示
- tasklist: 実行中のプロセスを一覧表示
注意：コマンドは現在のユーザー権限で実行されます。破壊的なコマンドは避けてください。";
        }

        #endregion

        public async Task<string> Function(string arguments)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(arguments))
                {
                    return GetLocalizedError("empty_command");
                }

                // 检查是否是设置命令
                var actionMatch = Regex.Match(arguments, @"action\((\w+)\)");
                if (actionMatch.Success)
                {
                    var action = actionMatch.Groups[1].Value.ToLower();
                    return HandleAction(action);
                }

                // 如果未授权，阻止命令执行
                if (!_settings.IsAuthorized)
                {
                    return GetLocalizedError("not_authorized");
                }

                // 安全检查：阻止危险命令
                var dangerCheck = CheckDangerousCommand(arguments);
                if (!string.IsNullOrEmpty(dangerCheck))
                {
                    return dangerCheck;
                }

                // 显示命令确认弹窗
                var confirmResult = await ShowCommandConfirmDialogAsync(arguments);
                if (!confirmResult.Confirmed)
                {
                    return GetLocalizedMessage("command_cancelled");
                }

                // 使用用户可能修改过的命令
                var commandToExecute = confirmResult.Command;

                // 再次检查修改后的命令是否危险
                if (commandToExecute != arguments)
                {
                    var dangerCheckModified = CheckDangerousCommand(commandToExecute);
                    if (!string.IsNullOrEmpty(dangerCheckModified))
                    {
                        return dangerCheckModified;
                    }
                }

                // 执行命令
                return await ExecuteCommandAsync(commandToExecute);
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error in Function: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// 显示命令确认弹窗
        /// </summary>
        private Task<(bool Confirmed, string Command)> ShowCommandConfirmDialogAsync(string command)
        {
            var tcs = new TaskCompletionSource<(bool, string)>();

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var language = GetLanguage();
                    var shellName = _currentShell.ToString();

                    var confirmWindow = new winCommandConfirm(command, shellName, language);
                    var result = confirmWindow.ShowDialog();

                    if (result == true && confirmWindow.IsConfirmed)
                    {
                        tcs.SetResult((true, confirmWindow.Command));
                    }
                    else
                    {
                        tcs.SetResult((false, command));
                    }
                });
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error showing confirm dialog: {ex.Message}");
                tcs.SetResult((false, command));
            }

            return tcs.Task;
        }

        private string HandleAction(string action)
        {
            switch (action)
            {
                case "setting":
                case "settings":
                    OpenSettings();
                    return GetLocalizedMessage("settings_opened");
                case "info":
                case "status":
                    return GetShellInfoString();
                case "refresh":
                    DetectAvailableShells();
                    return GetShellInfoString();
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
                    var language = GetLanguage();

                    // 如果用户还没有确认过警告，先显示警告弹窗
                    if (!_settings.HasConfirmedWarning)
                    {
                        var warningWindow = new winTerminalWarning(language);
                        var result = warningWindow.ShowDialog();

                        if (result != true || !warningWindow.IsConfirmed)
                        {
                            // 用户取消或未确认
                            return;
                        }

                        // 用户确认了警告
                        _settings.HasConfirmedWarning = true;
                        SaveSettings();
                    }

                    // 显示设置窗口
                    var settingWindow = new winTerminalSetting(this, _settings, OnSettingsSaved);
                    settingWindow.Show();
                });
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error opening settings: {ex.Message}");
            }
        }

        private void OnSettingsSaved(TerminalSettings newSettings)
        {
            _settings = newSettings;
            SaveSettings();
            _vpetLLM?.Log($"Terminal: Settings saved. Authorized: {_settings.IsAuthorized}");
        }

        private string GetShellInfoString()
        {
            var lang = GetLanguage();
            var sb = new StringBuilder();

            var currentShellLabel = lang switch
            {
                "zh-hans" => "当前 Shell",
                "zh-hant" => "當前 Shell",
                "ja" => "現在のシェル",
                _ => "Current Shell"
            };

            var available = lang switch
            {
                "zh-hans" => "可用",
                "zh-hant" => "可用",
                "ja" => "利用可能",
                _ => "Available"
            };

            var notFound = lang switch
            {
                "zh-hans" => "未找到",
                "zh-hant" => "未找到",
                "ja" => "見つかりません",
                _ => "Not Found"
            };

            var authorized = lang switch
            {
                "zh-hans" => "已授权",
                "zh-hant" => "已授權",
                "ja" => "許可済み",
                _ => "Authorized"
            };

            var notAuthorized = lang switch
            {
                "zh-hans" => "未授权",
                "zh-hant" => "未授權",
                "ja" => "未許可",
                _ => "Not Authorized"
            };

            sb.AppendLine($"{currentShellLabel}: {_currentShell} ({_shellPath})");
            sb.AppendLine($"PowerShell 7 (pwsh): {(_hasPwsh ? available : notFound)}");
            sb.AppendLine($"Windows PowerShell: {(_hasPowerShell ? available : notFound)}");
            sb.AppendLine($"CMD: {available}");
            sb.AppendLine($"Status: {(_settings.IsAuthorized ? authorized : notAuthorized)}");

            return sb.ToString();
        }

        /// <summary>
        /// 检查危险命令
        /// </summary>
        private string CheckDangerousCommand(string command)
        {
            var lowerCommand = command.ToLower().Trim();

            // 危险命令模式列表
            string[] dangerousPatterns = {
                @"\bformat\s+[a-z]:",           // format drive
                @"\brd\s+/s",                   // rd /s (recursive delete)
                @"\brmdir\s+/s",                // rmdir /s
                @"\bdel\s+/[sfq]",              // del with dangerous flags
                @"\berase\s+/",                 // erase with flags
                @"\brm\s+-rf",                  // rm -rf
                @"\bRemove-Item\s+.*-Recurse",  // Remove-Item -Recurse
                @":\\\s*$",                     // root directory operations
                @"\breg\s+delete",              // registry delete
                @"\bshutdown",                  // shutdown command
                @"\brestart-computer",          // PowerShell restart
                @"\bstop-computer",             // PowerShell shutdown
                @"\bnet\s+user",                // user management
                @"\bnet\s+localgroup",          // group management
                @"\bcacls\s+.*\s+/[egt]",       // permission changes
                @"\bicacls\s+.*\s+/grant",      // permission grants
                @"\btakeown",                   // take ownership
                @"\bformat-volume",             // PowerShell format
                @"\bclear-disk",                // PowerShell clear disk
                @"\binitialize-disk",           // PowerShell initialize disk
            };

            foreach (var pattern in dangerousPatterns)
            {
                if (Regex.IsMatch(lowerCommand, pattern, RegexOptions.IgnoreCase))
                {
                    _vpetLLM?.Log($"Terminal: Blocked dangerous command: {command}");
                    return GetLocalizedError("dangerous_command");
                }
            }

            return "";
        }

        private async Task<string> ExecuteCommandAsync(string command)
        {
            _vpetLLM?.Log($"Terminal: Executing command: {command}");

            try
            {
                ProcessStartInfo startInfo;

                if (_currentShell == ShellType.Cmd)
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {command}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                }
                else
                {
                    // PowerShell (pwsh or powershell)
                    startInfo = new ProcessStartInfo
                    {
                        FileName = _shellPath,
                        Arguments = $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                }

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data is not null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data is not null)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 使用超时等待
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch { }
                    return GetLocalizedError("timeout");
                }

                var output = outputBuilder.ToString().Trim();
                var error = errorBuilder.ToString().Trim();

                // 构建结果
                var result = new StringBuilder();

                if (!string.IsNullOrEmpty(output))
                {
                    result.AppendLine(TruncateOutput(output));
                }

                if (!string.IsNullOrEmpty(error))
                {
                    result.AppendLine($"[Error]: {TruncateOutput(error)}");
                }

                if (result.Length == 0)
                {
                    result.AppendLine(GetLocalizedMessage("command_completed"));
                }

                result.AppendLine($"[Exit Code: {process.ExitCode}]");

                _vpetLLM?.Log($"Terminal: Command completed with exit code {process.ExitCode}");

                return result.ToString();
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error executing command: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private string TruncateOutput(string output)
        {
            if (output.Length > MaxOutputLength)
            {
                return output.Substring(0, MaxOutputLength) + "\n... (output truncated)";
            }
            return output;
        }

        #region Settings Persistence

        private void LoadSettings()
        {
            if (string.IsNullOrEmpty(PluginDataDir)) return;

            try
            {
                var path = Path.Combine(PluginDataDir, SettingFileName);
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    _settings = JsonSerializer.Deserialize<TerminalSettings>(json) ?? new TerminalSettings();
                }
                else
                {
                    _settings = new TerminalSettings();
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error loading settings: {ex.Message}");
                _settings = new TerminalSettings();
            }
        }

        private void SaveSettings()
        {
            if (string.IsNullOrEmpty(PluginDataDir)) return;

            try
            {
                if (!Directory.Exists(PluginDataDir))
                {
                    Directory.CreateDirectory(PluginDataDir);
                }

                var path = Path.Combine(PluginDataDir, SettingFileName);
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error saving settings: {ex.Message}");
            }
        }

        #endregion

        #region Localization

        private string GetLocalizedError(string errorType)
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            return (errorType, lang) switch
            {
                ("empty_command", "zh-hans") => "错误：请提供要执行的命令。",
                ("empty_command", "zh-hant") => "錯誤：請提供要執行的命令。",
                ("empty_command", "ja") => "エラー：実行するコマンドを指定してください。",
                ("empty_command", _) => "Error: Please provide a command to execute.",

                ("not_authorized", "zh-hans") => "错误：用户尚未授权 AI 使用终端。请使用 `action(setting)` 打开设置进行授权。",
                ("not_authorized", "zh-hant") => "錯誤：用戶尚未授權 AI 使用終端。請使用 `action(setting)` 打開設置進行授權。",
                ("not_authorized", "ja") => "エラー：ユーザーはまだAIにターミナルの使用を許可していません。`action(setting)` を使用して設定を開き、許可してください。",
                ("not_authorized", _) => "Error: User has not authorized AI to use terminal. Use `action(setting)` to open settings and authorize.",

                ("dangerous_command", "zh-hans") => "错误：检测到危险命令，已阻止执行。出于安全考虑，某些可能造成系统损坏的命令被禁用。",
                ("dangerous_command", "zh-hant") => "錯誤：檢測到危險命令，已阻止執行。出於安全考慮，某些可能造成系統損壞的命令被禁用。",
                ("dangerous_command", "ja") => "エラー：危険なコマンドが検出されたため、実行がブロックされました。セキュリティ上の理由から、システムに損傷を与える可能性のあるコマンドは無効化されています。",
                ("dangerous_command", _) => "Error: Dangerous command detected and blocked. For security reasons, commands that may damage the system are disabled.",

                ("timeout", "zh-hans") => "错误：命令执行超时。",
                ("timeout", "zh-hant") => "錯誤：命令執行超時。",
                ("timeout", "ja") => "エラー：コマンドの実行がタイムアウトしました。",
                ("timeout", _) => "Error: Command execution timed out.",

                ("invalid_action", "zh-hans") => "错误：无效的操作。",
                ("invalid_action", "zh-hant") => "錯誤：無效的操作。",
                ("invalid_action", "ja") => "エラー：無効なアクションです。",
                ("invalid_action", _) => "Error: Invalid action.",

                _ => $"Error: {errorType}"
            };
        }

        private string GetLocalizedMessage(string messageType)
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            return (messageType, lang) switch
            {
                ("command_completed", "zh-hans") => "命令执行完成（无输出）。",
                ("command_completed", "zh-hant") => "命令執行完成（無輸出）。",
                ("command_completed", "ja") => "コマンドが完了しました（出力なし）。",
                ("command_completed", _) => "Command completed (no output).",

                ("settings_opened", "zh-hans") => "设置窗口已打开。",
                ("settings_opened", "zh-hant") => "設置窗口已打開。",
                ("settings_opened", "ja") => "設定ウィンドウが開きました。",
                ("settings_opened", _) => "Settings window opened.",

                ("command_cancelled", "zh-hans") => "用户取消了命令执行。",
                ("command_cancelled", "zh-hant") => "用戶取消了命令執行。",
                ("command_cancelled", "ja") => "ユーザーがコマンドの実行をキャンセルしました。",
                ("command_cancelled", _) => "User cancelled the command execution.",

                _ => messageType
            };
        }

        #endregion

        public void Unload()
        {
            VPetLLM.Utils.System.Logger.Log("Terminal Plugin Unloaded!");
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }
    }
}

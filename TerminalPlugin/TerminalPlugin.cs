using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;
using VPetLLM.Infrastructure.Configuration;

namespace TerminalPlugin
{
    /// <summary>
    /// Terminal Plugin Settings
    /// </summary>
    public class TerminalSettings
    {
        public bool HasConfirmedWarning { get; set; } = false;
        public bool IsAuthorized { get; set; } = false;

        /// <summary>是否允许远端浏览器批准终端命令。高危，默认关闭：命令确认强制在桌面本机进行。</summary>
        public bool AllowRemoteApproval { get; set; } = false;

        /// <summary>只读命令（Get-*、dir、git status 等）是否跳过确认弹窗直接执行。</summary>
        public bool AutoApproveReadOnly { get; set; } = true;

        /// <summary>用户显式信任的命令前缀，token 级前缀匹配，命中即免确认。</summary>
        public List<string> AllowedPrefixes { get; set; } = new();

        /// <summary>单条命令的超时秒数。</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>返回给模型的输出字符预算（stdout 与 stderr 共享）。</summary>
        public int MaxOutputChars { get; set; } = 4000;

        /// <summary>命令的初始工作目录；留空表示用户主目录。</summary>
        public string WorkingDirectory { get; set; } = "";

        /// <summary>是否在多次调用之间保留 cd 的结果（仅 PowerShell 支持）。</summary>
        public bool PersistWorkingDirectory { get; set; } = true;

        /// <summary>是否把名字里带 KEY/SECRET/TOKEN/PASSWORD 的环境变量从子进程里摘掉。</summary>
        public bool FilterSecretEnvironment { get; set; } = true;

        /// <summary>首选 shell：auto / pwsh / powershell / cmd。</summary>
        public string PreferredShell { get; set; } = "auto";
    }

    /// <summary>
    /// Shell information for UI display
    /// </summary>
    public class ShellInfo
    {
        public string CurrentShell { get; set; } = "";
        public bool HasPwsh { get; set; }
        public bool HasPowerShell { get; set; }
        public string WorkingDirectory { get; set; } = "";
    }

    public class TerminalPlugin : IActionPlugin, IDynamicInfoPlugin, IPluginWithData, IPluginTab, IToolSchemaPlugin
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
                if (_hasPowerShell || _hasPwsh)
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

        /// <summary>当前工作目录。PowerShell 下 cd 的结果会写回这里，跨调用保留。</summary>
        private string _workingDirectory = "";

        /// <summary>外部执行进程的完整路径，与插件 DLL 同目录。</summary>
        private string _hostPath = "";

        /// <summary>执行进程是否就位。为 false 时任何命令都会以错误返回。</summary>
        private bool _hostAvailable;

        /// <summary>只有 action(xxx) 单独构成整条参数时才当作动作，否则一律按命令执行。</summary>
        private static readonly Regex ActionPattern =
            new(@"^\s*action\s*\(\s*(\w+)\s*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>会被从子进程环境里摘掉的变量名模式。</summary>
        private const int MinTimeoutSeconds = 5;
        private const int MaxTimeoutSeconds = 600;

        /// <summary>杀掉进程后等待输出管道排空的时间。孙进程可能仍持有管道，不能无限等。</summary>
        private const int IoDrainTimeoutMs = 2000;

        public enum ShellType
        {
            Cmd,
            PowerShell,
            Pwsh
        }

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;

            // 安全规则来自 DLL 同目录的 TerminalPlugin.rules.json。这里必须用 FilePath
            // 而不是 Assembly.Location：宿主把插件影子拷贝到 PluginCache 后再加载，
            // Assembly.Location 指向的是那份拷贝，旁边并没有规则文件。
            var rulesPath = SafetyRuleLoader.ResolvePath(FilePath);
            CommandSafety.LoadRules(rulesPath);
            if (!CommandSafety.RulesLoaded)
            {
                VPetLLM.Utils.System.Logger.Log(
                    $"Terminal: 安全规则加载失败（{CommandSafety.RuleLoadError}），" +
                    $"已进入保护模式：所有命令都需要高危确认，自动放行全部关闭。路径: {rulesPath}");
            }

            LoadSettings();

            // 命令执行已经拆到独立进程，这里定位它并做一次 shell 探测。
            // Task.Run 是为了摆脱可能存在的 UI 同步上下文，避免在插件加载阶段死锁。
            _hostPath = TerminalHostClient.ResolvePath(FilePath);
            ProbeShells();

            _workingDirectory = ResolveInitialWorkingDirectory();
            VPetLLM.Utils.System.Logger.Log(
                $"Terminal Plugin Initialized! Using: {_currentShell} ({_shellPath}), " +
                $"Authorized: {_settings.IsAuthorized}, Cwd: {_workingDirectory}, " +
                $"Rules: {(CommandSafety.RulesLoaded ? "loaded" : "FAILED")}, " +
                $"Host: {(_hostAvailable ? "ready" : "MISSING")}");
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
                HasPowerShell = _hasPowerShell,
                WorkingDirectory = _workingDirectory
            };
        }

        #region Shell 探测

        /// <summary>
        /// 问执行进程机器上有哪些 shell 可用。
        ///
        /// 检测逻辑连同 pwsh.exe / powershell.exe / cmd.exe 这些名字已经随执行进程
        /// 一起搬出插件，这里只留一份供界面和提示词使用的结果快照。
        /// </summary>
        private async Task ProbeShellsAsync()
        {
            var response = await TerminalHostClient.InvokeAsync(_hostPath, new HostRequest
            {
                Op = "probe",
                Shell = _settings.PreferredShell ?? "auto",
                ParentPid = Environment.ProcessId
            });

            if (!response.Ok)
            {
                _hostAvailable = false;
                VPetLLM.Utils.System.Logger.Log(
                    $"Terminal: 执行进程不可用（{response.Error}），所有命令都将无法执行。路径: {_hostPath}");
                return;
            }

            _hostAvailable = true;
            ApplyShellSnapshot(response);

            _vpetLLM?.Log($"Terminal: Detected shells - pwsh: {_hasPwsh}, powershell: {_hasPowerShell}, " +
                          $"using: {_currentShell} ({_shellPath})");
        }

        /// <summary>
        /// 同步跑一次探测，供插件加载和设置保存这些同步路径使用。
        /// Task.Run 是为了摆脱可能存在的 UI 同步上下文，避免死锁。
        /// </summary>
        private void ProbeShells() => Task.Run(ProbeShellsAsync).GetAwaiter().GetResult();

        /// <summary>把执行进程带回来的 shell 信息记下来，仅用于显示。</summary>
        private void ApplyShellSnapshot(HostResponse response)
        {
            _hasPwsh = response.HasPwsh;
            _hasPowerShell = response.HasPowerShell;

            if (!string.IsNullOrEmpty(response.ShellPath)) _shellPath = response.ShellPath;
            if (!string.IsNullOrEmpty(response.SelectedShell))
            {
                _currentShell = response.SelectedShell switch
                {
                    "Pwsh" => ShellType.Pwsh,
                    "PowerShell" => ShellType.PowerShell,
                    _ => ShellType.Cmd
                };
            }
        }

        #endregion

        #region 工作目录

        /// <summary>
        /// 解析初始工作目录。
        ///
        /// 默认落到用户主目录，而不是继承 VPet 进程的当前目录 —— 后者通常是 VPet 的
        /// 安装目录，让 AI 在那里跑相对路径命令既没用也危险。
        /// </summary>
        private string ResolveInitialWorkingDirectory()
        {
            var configured = _settings.WorkingDirectory?.Trim();
            if (!string.IsNullOrEmpty(configured))
            {
                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(configured);
                    if (Directory.Exists(expanded)) return expanded;
                    _vpetLLM?.Log($"Terminal: Configured working directory not found, falling back: {expanded}");
                }
                catch (Exception ex)
                {
                    _vpetLLM?.Log($"Terminal: Invalid working directory '{configured}': {ex.Message}");
                }
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        #endregion

        #region 调用契约（IToolSchemaPlugin）

        /// <summary>
        /// 结构化声明本插件的两种调用形态。渲染成签名后模型不用再从散文里猜参数格式。
        /// </summary>
        public ToolSchema? GetToolSchema()
        {
            var lang = GetLanguage();
            var shellName = _currentShell switch
            {
                ShellType.Pwsh => "PowerShell 7",
                ShellType.PowerShell => "Windows PowerShell",
                _ => "CMD"
            };

            var (summary, cmdForm, cmdDesc, actionForm, remarks) = lang switch
            {
                "zh-hans" => (
                    "在用户电脑上执行终端命令",
                    "执行一条终端命令，返回其输出与退出码",
                    $"要执行的命令，使用 {shellName} 语法",
                    "打开设置窗口 / 查看当前 shell 与工作目录 / 重新检测可用 shell",
                    $"命令以当前用户权限执行；高危命令会要求用户确认，破坏性命令会被直接拒绝。当前工作目录 {_workingDirectory}，超时 {_settings.TimeoutSeconds} 秒。"),
                "zh-hant" => (
                    "在用戶電腦上執行終端命令",
                    "執行一條終端命令，返回其輸出與退出碼",
                    $"要執行的命令，使用 {shellName} 語法",
                    "打開設置窗口 / 查看當前 shell 與工作目錄 / 重新檢測可用 shell",
                    $"命令以當前用戶權限執行；高危命令會要求用戶確認，破壞性命令會被直接拒絕。當前工作目錄 {_workingDirectory}，超時 {_settings.TimeoutSeconds} 秒。"),
                "ja" => (
                    "ユーザーのPCでターミナルコマンドを実行する",
                    "コマンドを1つ実行し、その出力と終了コードを返す",
                    $"実行するコマンド（{shellName} の構文）",
                    "設定を開く / 現在のシェルと作業ディレクトリを表示 / シェルを再検出",
                    $"コマンドは現在のユーザー権限で実行されます。危険なコマンドは確認が必要で、破壊的なコマンドは拒否されます。作業ディレクトリ {_workingDirectory}、タイムアウト {_settings.TimeoutSeconds} 秒。"),
                _ => (
                    "Run terminal commands on the user's machine",
                    "Run one command and return its output and exit code",
                    $"The command to run, in {shellName} syntax",
                    "Open settings / show current shell and working directory / re-detect shells",
                    $"Commands run with the current user's privileges. Risky commands require user confirmation; destructive ones are rejected outright. Working directory {_workingDirectory}, timeout {_settings.TimeoutSeconds}s.")
            };

            return new ToolSchema
            {
                Summary = summary,
                Remarks = remarks,
                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = cmdForm,
                        // 整段标记内容就是命令本身，不用 name(value) 包装
                        Style = ToolCallStyle.RawText,
                        Parameters = new[] { ToolParameter.Str("command", cmdDesc) },
                        Example = (_hasPwsh || _hasPowerShell) ? "Get-ChildItem -Path . -Recurse -Filter *.log" : "dir /s *.log"
                    },
                    new ToolCallForm
                    {
                        Summary = actionForm,
                        Parameters = new[]
                        {
                            ToolParameter.Choice("action", "", new[] { "setting", "info", "refresh" })
                        },
                        Example = "action(setting)"
                    }
                }
            };
        }

        #endregion

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

            var basePrompt = (_hasPwsh || _hasPowerShell)
                ? lang switch
                {
                    "zh-hans" => GetPowerShellPromptZhHans(),
                    "zh-hant" => GetPowerShellPromptZhHant(),
                    "ja" => GetPowerShellPromptJa(),
                    _ => GetPowerShellPromptEn()
                }
                : lang switch
                {
                    "zh-hans" => GetCmdPromptZhHans(),
                    "zh-hant" => GetCmdPromptZhHant(),
                    "ja" => GetCmdPromptJa(),
                    _ => GetCmdPromptEn()
                };

            return basePrompt + "\n" + GetRuntimeContextPrompt(lang);
        }

        /// <summary>把工作目录、超时、自动放行状态告诉模型，省得它反复试错。</summary>
        private string GetRuntimeContextPrompt(string lang)
        {
            var persist = _settings.PersistWorkingDirectory && _currentShell != ShellType.Cmd;
            return lang switch
            {
                "zh-hans" =>
                    $"当前工作目录：{_workingDirectory}" +
                    (persist ? "（cd 的结果会在后续命令中保留）" : "（每条命令都从该目录开始，cd 不会保留）") +
                    $"\n单条命令超时 {_settings.TimeoutSeconds} 秒，输出超过 {_settings.MaxOutputChars} 字符会保留首尾、省略中间。" +
                    (_settings.AutoApproveReadOnly ? "\n只读命令会直接执行；其他命令需要用户在弹窗中确认，高危命令会被拦截。" : "\n所有命令都需要用户在弹窗中确认，高危命令会被拦截。"),
                "zh-hant" =>
                    $"當前工作目錄：{_workingDirectory}" +
                    (persist ? "（cd 的結果會在後續命令中保留）" : "（每條命令都從該目錄開始，cd 不會保留）") +
                    $"\n單條命令超時 {_settings.TimeoutSeconds} 秒，輸出超過 {_settings.MaxOutputChars} 字符會保留首尾、省略中間。" +
                    (_settings.AutoApproveReadOnly ? "\n唯讀命令會直接執行；其他命令需要用戶在彈窗中確認，高危命令會被攔截。" : "\n所有命令都需要用戶在彈窗中確認，高危命令會被攔截。"),
                "ja" =>
                    $"現在の作業ディレクトリ：{_workingDirectory}" +
                    (persist ? "（cd の結果は後続のコマンドに引き継がれます）" : "（各コマンドはこのディレクトリから開始し、cd は保持されません）") +
                    $"\nコマンドのタイムアウトは {_settings.TimeoutSeconds} 秒。出力が {_settings.MaxOutputChars} 文字を超える場合は先頭と末尾を残して中間を省略します。" +
                    (_settings.AutoApproveReadOnly ? "\n読み取り専用コマンドは即座に実行されます。その他はユーザーの確認が必要で、危険なコマンドはブロックされます。" : "\nすべてのコマンドにユーザーの確認が必要で、危険なコマンドはブロックされます。"),
                _ =>
                    $"Current working directory: {_workingDirectory}" +
                    (persist ? " (cd persists across commands)" : " (each command starts here; cd does not persist)") +
                    $"\nPer-command timeout: {_settings.TimeoutSeconds}s. Output over {_settings.MaxOutputChars} chars keeps the head and tail and omits the middle." +
                    (_settings.AutoApproveReadOnly ? "\nRead-only commands run immediately; others require user confirmation, and dangerous ones are blocked." : "\nAll commands require user confirmation, and dangerous ones are blocked.")
            };
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

                // 只有整条参数就是 action(xxx) 时才当动作处理。
                // 从前这里用的是无锚定匹配，导致 `echo "action(info)"` 这类命令被劫持。
                var actionMatch = ActionPattern.Match(arguments);
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

                var command = arguments.Trim();
                var verdict = CommandSafety.Evaluate(command);

                // 不可挽回 / 无法审查：直接拒绝，不提供放行入口
                if (verdict.Decision == SafetyDecision.Forbidden)
                {
                    _vpetLLM?.Log($"Terminal: Forbidden command blocked [{verdict.ReasonKey}]: {command}");
                    return GetLocalizedError("dangerous_command") + " " + GetSafetyReason(verdict);
                }

                // 只读命令 / 命中用户前缀规则：跳过确认，直接执行
                if (CommandSafety.IsAutoApprovable(command, verdict, _settings.AllowedPrefixes, _settings.AutoApproveReadOnly))
                {
                    _vpetLLM?.Log($"Terminal: Auto-approved (read-only or allow-listed): {command}");
                    return await ExecuteCommandAsync(command);
                }

                // 显示命令确认弹窗
                var confirmResult = await ShowCommandConfirmDialogAsync(command, verdict);
                if (!confirmResult.Confirmed)
                {
                    return GetLocalizedMessage("command_cancelled");
                }

                // 使用用户可能修改过的命令
                var commandToExecute = confirmResult.Command;

                // 用户改写后重新评估：改出来的硬拒绝项仍然拦下
                if (commandToExecute != command)
                {
                    var reVerdict = CommandSafety.Evaluate(commandToExecute);
                    if (reVerdict.Decision == SafetyDecision.Forbidden)
                    {
                        _vpetLLM?.Log($"Terminal: Forbidden after edit [{reVerdict.ReasonKey}]: {commandToExecute}");
                        return GetLocalizedError("dangerous_command") + " " + GetSafetyReason(reVerdict);
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
        private async Task<(bool Confirmed, string Command)> ShowCommandConfirmDialogAsync(
            string command, SafetyVerdict verdict)
        {
            var language = GetLanguage();
            var shellName = _currentShell.ToString();
            var isDangerous = verdict.Decision == SafetyDecision.Dangerous;
            var dangerReason = isDangerous ? GetSafetyReason(verdict) : "";

            // 通过 VPetLLM 的统一交互入口发起确认：本地聊天会弹出桌面窗口，
            // 远端聊天会把请求推送到 WebUI 并等待回执。
            if (_vpetLLM?.Interaction is { } interaction)
            {
                var (title, message, confirmText, cancelText) = language switch
                {
                    "zh-hans" => ($"终端 - 命令确认（{shellName}）", "AI 请求执行以下命令，请在执行前核对，可修改：", "执行", "取消"),
                    "zh-hant" => ($"終端 - 命令確認（{shellName}）", "AI 請求執行以下命令，請在執行前核對，可修改：", "執行", "取消"),
                    "ja" => ($"ターミナル - コマンド確認（{shellName}）", "AIが以下のコマンドの実行を要求しています。実行前に確認し、必要なら変更してください：", "実行", "キャンセル"),
                    _ => ($"Terminal - Command Confirmation ({shellName})", "AI requests to execute the following command. Verify before running; you may edit it:", "Execute", "Cancel")
                };

                // 高危命令：把理由顶到标题和正文最前面，远端 WebUI 也能看到同样的告警
                if (isDangerous)
                {
                    title = "⚠ " + title;
                    message = "⚠ " + dangerReason + "\n\n" + message;
                }

                try
                {
                    var result = await interaction.RequestAsync(new VPetLLM.Core.Interaction.InteractionRequest
                    {
                        Kind = VPetLLM.Core.Interaction.InteractionKind.Confirm,
                        Source = Name,
                        Title = title,
                        Message = message,
                        DefaultValue = command,
                        ConfirmText = confirmText,
                        CancelText = cancelText,
                        Timeout = TimeSpan.FromMinutes(2),
                        AllowRemote = _settings.AllowRemoteApproval
                    });
                    return (result.Confirmed, string.IsNullOrWhiteSpace(result.Value) ? command : result.Value.Trim());
                }
                catch (Exception ex)
                {
                    _vpetLLM?.Log($"Terminal: Error requesting confirmation: {ex.Message}");
                    return (false, command);
                }
            }

            // 兜底：无法访问交互服务时退回原有本地弹窗。
            var tcs = new TaskCompletionSource<(bool, string)>();
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var confirmWindow = new winCommandConfirm(command, shellName, language, dangerReason);
                    var dialog = confirmWindow.ShowDialog();
                    tcs.SetResult(dialog == true && confirmWindow.IsConfirmed ? (true, confirmWindow.Command) : (false, command));
                });
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error showing confirm dialog: {ex.Message}");
                tcs.SetResult((false, command));
            }
            return await tcs.Task;
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
                    ProbeShells();
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

                    // 薄壳窗口包住设置面板 UserControl
                    var win = new Window
                    {
                        Title = "Terminal Plugin - Settings",
                        Width = 480,
                        Height = 640,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Content = new winTerminalSetting(this, _settings, OnSettingsSaved)
                    };
                    win.Show();
                });
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error opening settings: {ex.Message}");
            }
        }

        // ---------------- IPluginTab（设置窗口内嵌面板）----------------

        public string TabTitle => GetLanguage() switch
        {
            "ja" => "ターミナル",
            "zh-hant" => "終端",
            "en" => "Terminal",
            _ => "终端"
        };

        public System.Windows.FrameworkElement CreatePanel()
            => new winTerminalSetting(this, _settings, OnSettingsSaved);

        private void OnSettingsSaved(TerminalSettings newSettings)
        {
            _settings = newSettings;
            _settings.TimeoutSeconds = Math.Clamp(_settings.TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
            _settings.MaxOutputChars = Math.Clamp(_settings.MaxOutputChars, 500, 100_000);
            SaveSettings();

            // shell 偏好和工作目录可能变了，重新解析一次
            ProbeShells();
            _workingDirectory = ResolveInitialWorkingDirectory();

            _vpetLLM?.Log($"Terminal: Settings saved. Authorized: {_settings.IsAuthorized}, " +
                          $"Shell: {_currentShell}, Cwd: {_workingDirectory}, " +
                          $"AutoApproveReadOnly: {_settings.AutoApproveReadOnly}, " +
                          $"Prefixes: {_settings.AllowedPrefixes.Count}");
        }

        private string GetShellInfoString()
        {
            var lang = GetLanguage();
            var sb = new StringBuilder();

            var (currentShellLabel, available, notFound, authorized, notAuthorized, cwdLabel, autoLabel, timeoutLabel, prefixLabel, on, off) = lang switch
            {
                "zh-hans" => ("当前 Shell", "可用", "未找到", "已授权", "未授权", "工作目录", "只读命令免确认", "超时", "信任前缀", "开", "关"),
                "zh-hant" => ("當前 Shell", "可用", "未找到", "已授權", "未授權", "工作目錄", "唯讀命令免確認", "超時", "信任前綴", "開", "關"),
                "ja" => ("現在のシェル", "利用可能", "見つかりません", "許可済み", "未許可", "作業ディレクトリ", "読み取り専用の自動許可", "タイムアウト", "信頼するプレフィックス", "オン", "オフ"),
                _ => ("Current Shell", "Available", "Not Found", "Authorized", "Not Authorized", "Working Directory", "Auto-approve read-only", "Timeout", "Allowed prefixes", "On", "Off")
            };

            sb.AppendLine($"{currentShellLabel}: {_currentShell} ({_shellPath})");
            sb.AppendLine($"PowerShell 7 (pwsh): {(_hasPwsh ? available : notFound)}");
            sb.AppendLine($"Windows PowerShell: {(_hasPowerShell ? available : notFound)}");
            sb.AppendLine($"CMD: {available}");
            sb.AppendLine($"Status: {(_settings.IsAuthorized ? authorized : notAuthorized)}");
            sb.AppendLine($"{cwdLabel}: {_workingDirectory}");
            sb.AppendLine($"{autoLabel}: {(_settings.AutoApproveReadOnly ? on : off)}");
            sb.AppendLine($"{timeoutLabel}: {_settings.TimeoutSeconds}s");
            sb.AppendLine($"{prefixLabel}: {_settings.AllowedPrefixes.Count}");

            return sb.ToString();
        }

        #region 执行

        /// <summary>
        /// 执行一条命令。
        ///
        /// 这里已经不碰 shell 了：进程创建、参数拼装、Job Object、编码处理全部发生在
        /// VPetLLM.TerminalHost.exe 里。VPet 进程只启动一个用途明确的工具进程并收回
        /// 一段 JSON，"桌宠隐藏窗口拉起 cmd.exe"这条行为特征不再出现在宿主身上。
        /// </summary>
        private async Task<string> ExecuteCommandAsync(string command)
        {
            _vpetLLM?.Log($"Terminal: Executing command in {_workingDirectory}: {command}");

            var response = await TerminalHostClient.InvokeAsync(_hostPath, new HostRequest
            {
                Op = "exec",
                Command = command,
                Cwd = _workingDirectory,
                Shell = _settings.PreferredShell ?? "auto",
                TimeoutSeconds = _settings.TimeoutSeconds,
                FilterSecrets = _settings.FilterSecretEnvironment,
                PersistCwd = _settings.PersistWorkingDirectory,
                ParentPid = Environment.ProcessId
            });

            if (!response.Ok)
            {
                _vpetLLM?.Log($"Terminal: Error executing command: {response.Error}");
                return $"Error: {response.Error}";
            }

            // 执行进程顺带把 shell 探测结果带回来了，刷新快照省一次往返
            ApplyShellSnapshot(response);

            // cd 跨调用保持。命令中途被掐时执行进程回传 null，此时保留原目录。
            if (response.Cwd is not null && response.Cwd != _workingDirectory)
            {
                _workingDirectory = response.Cwd;
                _vpetLLM?.Log($"Terminal: Working directory is now {_workingDirectory}");
            }

            return BuildResult(response.Stdout, response.Stderr, response.ExitCode, response.TimedOut);
        }

        private string BuildResult(string output, string error, int exitCode, bool timedOut)
        {
            // stdout / stderr 共享一份预算，避免最坏情况往上下文塞两倍的量
            var (truncatedOut, truncatedErr) =
                OutputFormatter.TruncatePair(output, error, _settings.MaxOutputChars);

            var result = new StringBuilder();

            if (timedOut)
            {
                // 超时不再丢弃已收集到的输出 —— 跑了 29 秒才被掐掉的命令，
                // 它打出来的东西往往正是模型需要的。
                result.AppendLine(GetLocalizedError("timeout"));
            }

            if (!string.IsNullOrEmpty(truncatedOut))
            {
                result.AppendLine(truncatedOut);
            }

            if (!string.IsNullOrEmpty(truncatedErr))
            {
                result.AppendLine($"[Error]: {truncatedErr}");
            }

            if (result.Length == 0)
            {
                result.AppendLine(GetLocalizedMessage("command_completed"));
            }

            result.AppendLine($"[Exit Code: {exitCode}]");
            result.AppendLine($"[Cwd: {_workingDirectory}]");

            _vpetLLM?.Log($"Terminal: Command completed with exit code {exitCode}{(timedOut ? " (timed out)" : "")}");

            return result.ToString();
        }

        #endregion

        #region Settings Persistence

        private void LoadSettings()
        {
            try
            {
                _settings = PluginConfigHelper.Load<TerminalSettings>("Terminal");
                _settings.AllowedPrefixes ??= new List<string>();
                _settings.TimeoutSeconds = Math.Clamp(_settings.TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
                _settings.MaxOutputChars = Math.Clamp(_settings.MaxOutputChars, 500, 100_000);
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error loading settings: {ex.Message}");
                _settings = new TerminalSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                PluginConfigHelper.Save("Terminal", _settings);
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"Terminal: Error saving settings: {ex.Message}");
            }
        }

        #endregion

        #region Localization

        /// <summary>把安全裁决的理由翻译成用户语言，附上命中的具体片段。</summary>
        public string GetSafetyReason(SafetyVerdict verdict)
        {
            if (string.IsNullOrEmpty(verdict.ReasonKey)) return "";

            var lang = GetLanguage();
            var (zhHans, zhHant, ja, en) = verdict.ReasonKey switch
            {
                "rules_unavailable" => ("安全规则文件缺失或损坏，已进入保护模式：无法判断该命令的风险。", "安全規則檔案缺失或損壞，已進入保護模式：無法判斷該命令的風險。", "セキュリティルールファイルが見つからないか破損しているため、保護モードで動作しています。コマンドの危険度を判定できません。", "The safety ruleset is missing or corrupt; running in protected mode and cannot assess this command."),
                "too_nested" => ("命令嵌套层数过深，无法审查。", "命令嵌套層數過深，無法審查。", "コマンドのネストが深すぎて検査できません。", "Command is nested too deeply to inspect."),
                "download_and_execute" => ("检测到「下载即执行」：从网络取回内容并直接求值。", "偵測到「下載即執行」：從網路取回內容並直接求值。", "「ダウンロードして実行」を検出しました。", "Detected download-and-execute: content fetched from the network is evaluated directly."),
                "encoded_command" => ("命令以 Base64 编码传入，内容无法审查。", "命令以 Base64 編碼傳入，內容無法審查。", "コマンドが Base64 でエンコードされており、内容を検査できません。", "Command is Base64-encoded and cannot be inspected."),
                "opaque_script" => ("执行外部脚本文件，内容不可见。", "執行外部腳本檔案，內容不可見。", "外部スクリプトファイルを実行します。内容は確認できません。", "Runs an external script file whose contents are not visible."),
                "stop_parsing" => ("使用了 --% 停止解析符，其后内容不受审查。", "使用了 --% 停止解析符，其後內容不受審查。", "--% 以降の内容は解析されません。", "Uses the --% stop-parsing token; everything after it is unchecked."),
                "spawns_shell" => ("会拉起一个新的 shell。", "會拉起一個新的 shell。", "新しいシェルを起動します。", "Spawns a new shell."),
                "elevation" => ("请求以管理员权限运行。", "請求以管理員權限運行。", "管理者権限での実行を要求しています。", "Requests elevated (administrator) privileges."),
                "url_launch" => ("通过 ShellExecute 打开链接，可绕过命令行审查。", "透過 ShellExecute 打開連結，可繞過命令行審查。", "ShellExecute 経由で URL を開きます。", "Opens a URL via ShellExecute, which bypasses command-line inspection."),
                "disk_destruction" => ("磁盘级破坏操作，数据不可恢复。", "磁碟級破壞操作，資料不可恢復。", "ディスクレベルの破壊操作です。データは復元できません。", "Disk-level destruction; data is unrecoverable."),
                "backup_destruction" => ("删除系统备份 / 卷影副本。", "刪除系統備份 / 卷影副本。", "システムバックアップ / シャドウコピーを削除します。", "Deletes system backups or shadow copies."),
                "boot_config" => ("修改启动配置，可能导致系统无法开机。", "修改啟動配置，可能導致系統無法開機。", "起動構成を変更します。システムが起動しなくなる可能性があります。", "Modifies boot configuration; the system may fail to start."),
                "system_path_delete" => ("删除目标是盘符根目录或系统目录。", "刪除目標是磁碟根目錄或系統目錄。", "削除対象がドライブのルートまたはシステムディレクトリです。", "Delete target is a drive root or a system directory."),
                "recursive_delete" => ("递归删除目录。", "遞歸刪除目錄。", "ディレクトリを再帰的に削除します。", "Recursively deletes a directory."),
                "force_delete" => ("强制删除文件，不进入回收站。", "強制刪除檔案，不進入回收站。", "ファイルを強制削除します（ごみ箱を経由しません）。", "Force-deletes files, bypassing the Recycle Bin."),
                "registry_write" => ("写入或删除注册表项。", "寫入或刪除註冊表項。", "レジストリを書き込み / 削除します。", "Writes to or deletes registry keys."),
                "power_control" => ("关机 / 重启 / 注销。", "關機 / 重啟 / 登出。", "シャットダウン / 再起動 / ログオフ。", "Shuts down, restarts, or logs off."),
                "account_management" => ("修改用户或用户组。", "修改用戶或用戶組。", "ユーザーまたはグループを変更します。", "Modifies users or groups."),
                "permission_change" => ("修改文件权限或所有者。", "修改檔案權限或擁有者。", "ファイルのアクセス許可または所有者を変更します。", "Changes file permissions or ownership."),
                "security_policy" => ("修改系统安全策略。", "修改系統安全策略。", "システムのセキュリティポリシーを変更します。", "Changes a system security policy."),
                "force_kill" => ("强制结束进程。", "強制結束進程。", "プロセスを強制終了します。", "Force-kills processes."),
                "service_change" => ("修改或停止系统服务。", "修改或停止系統服務。", "システムサービスを変更 / 停止します。", "Modifies or stops system services."),
                "scheduled_task" => ("创建或删除计划任务（可实现持久化）。", "建立或刪除排程任務（可實現持久化）。", "スケジュールタスクを作成 / 削除します。", "Creates or deletes scheduled tasks (a persistence mechanism)."),
                "dynamic_eval" => ("动态求值字符串，实际执行内容不可预知。", "動態求值字串，實際執行內容不可預知。", "文字列を動的に評価するため、実際の処理は予測できません。", "Dynamically evaluates a string; what actually runs is unpredictable."),
                "stealth_download" => ("使用系统自带工具下载文件（常见于规避检测）。", "使用系統自帶工具下載檔案（常見於規避偵測）。", "OS 標準ツールでファイルをダウンロードします。", "Downloads files using a built-in system tool (a common evasion technique)."),
                "wmi_execution" => ("通过 WMI 执行操作，能力范围很广。", "透過 WMI 執行操作，能力範圍很廣。", "WMI 経由で操作します。影響範囲が広いです。", "Acts through WMI, which has very broad capabilities."),
                "volume_change" => ("修改卷 / 分区映射。", "修改卷 / 分區映射。", "ボリューム / パーティションのマッピングを変更します。", "Changes volume or partition mappings."),
                _ => ("", "", "", "")
            };

            var text = lang switch
            {
                "zh-hans" => zhHans,
                "zh-hant" => zhHant,
                "ja" => ja,
                _ => en
            };

            if (string.IsNullOrEmpty(text)) return "";
            return string.IsNullOrEmpty(verdict.Detail) ? text : $"{text}（{verdict.Detail}）";
        }

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

                ("dangerous_command", "zh-hans") => "错误：检测到危险命令，已阻止执行。",
                ("dangerous_command", "zh-hant") => "錯誤：檢測到危險命令，已阻止執行。",
                ("dangerous_command", "ja") => "エラー：危険なコマンドが検出されたため、実行がブロックされました。",
                ("dangerous_command", _) => "Error: Dangerous command detected and blocked.",

                ("timeout", "zh-hans") => $"[提示] 命令执行超时（{_settings.TimeoutSeconds} 秒），已终止。以下是超时前收集到的输出：",
                ("timeout", "zh-hant") => $"[提示] 命令執行超時（{_settings.TimeoutSeconds} 秒），已終止。以下是超時前收集到的輸出：",
                ("timeout", "ja") => $"[注意] コマンドがタイムアウトしました（{_settings.TimeoutSeconds} 秒）。以下はタイムアウト前に収集された出力です：",
                ("timeout", _) => $"[Notice] Command timed out after {_settings.TimeoutSeconds}s and was terminated. Output collected before the timeout:",

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TerminalPlugin
{
    public partial class winTerminalSetting : UserControl
    {
        private readonly TerminalPlugin _plugin;
        private readonly string _language;
        private readonly Action<TerminalSettings> _onSave;

        /// <summary>
        /// 当前设置对象。保存时在它上面就地改字段，而不是 new 一个新的 —— 从前那样写会
        /// 把 UI 上没有暴露的字段（AllowRemoteApproval 等）静默重置回默认值。
        /// </summary>
        private readonly TerminalSettings _settings;

        public winTerminalSetting(TerminalPlugin plugin, TerminalSettings settings, Action<TerminalSettings> onSave)
        {
            InitializeComponent();
            _plugin = plugin;
            _language = plugin.GetLanguage();
            _onSave = onSave;
            _settings = settings;

            ApplyLocalization();
            LoadSettings(settings);
            UpdateShellInfo();
        }

        private void ApplyLocalization()
        {
            switch (_language)
            {
                case "zh-hans":
                    txtTitle.Text = "终端设置";
                    txtShellInfoTitle.Text = "Shell 信息";
                    txtPreferredShellLabel.Text = "首选 Shell";
                    txtEnableTitle.Text = "授权 AI 使用终端";
                    txtEnableDesc.Text = "允许 AI 执行终端命令";
                    txtApprovalTitle.Text = "确认策略";
                    chkAutoApproveReadOnly.Content = "只读命令免确认";
                    txtAutoApproveHint.Text = "Get-*、dir、ipconfig、git status 等直接执行，不弹窗。带重定向、分号、&& 或子表达式的命令一律仍需确认。";
                    chkAllowRemoteApproval.Content = "允许远端浏览器批准";
                    txtRemoteApprovalHint.Text = "高危。关闭时，命令确认强制在本机桌面进行。";
                    txtPrefixLabel.Text = "始终允许的命令前缀（每行一条）";
                    txtPrefixHint.Text = "按 token 前缀匹配：写 `git status` 也会覆盖 `git status --short`。";
                    txtExecutionTitle.Text = "执行";
                    txtTimeoutLabel.Text = "超时（秒）";
                    txtMaxOutputLabel.Text = "输出上限（字符）";
                    txtCwdLabel.Text = "工作目录";
                    txtCwdHint.Text = "留空则使用用户主目录。";
                    chkPersistCwd.Content = "跨命令保留 cd 结果（仅 PowerShell）";
                    chkFilterEnv.Content = "隐藏 KEY / SECRET / TOKEN 类环境变量";
                    txtFilterEnvHint.Text = "防止命令把你的 API 密钥从环境变量里读走。";
                    txtWarningTitle.Text = "警告";
                    txtWarningDesc.Text = "启用后，AI 可以执行可能修改系统的命令。仅在您能够验证命令安全性时启用。";
                    btnSave.Content = "保存";
                    btnClose.Content = "关闭";
                    break;

                case "zh-hant":
                    txtTitle.Text = "終端設置";
                    txtShellInfoTitle.Text = "Shell 資訊";
                    txtPreferredShellLabel.Text = "首選 Shell";
                    txtEnableTitle.Text = "授權 AI 使用終端";
                    txtEnableDesc.Text = "允許 AI 執行終端命令";
                    txtApprovalTitle.Text = "確認策略";
                    chkAutoApproveReadOnly.Content = "唯讀命令免確認";
                    txtAutoApproveHint.Text = "Get-*、dir、ipconfig、git status 等直接執行，不彈窗。帶重定向、分號、&& 或子表達式的命令一律仍需確認。";
                    chkAllowRemoteApproval.Content = "允許遠端瀏覽器批准";
                    txtRemoteApprovalHint.Text = "高危。關閉時，命令確認強制在本機桌面進行。";
                    txtPrefixLabel.Text = "始終允許的命令前綴（每行一條）";
                    txtPrefixHint.Text = "按 token 前綴匹配：寫 `git status` 也會覆蓋 `git status --short`。";
                    txtExecutionTitle.Text = "執行";
                    txtTimeoutLabel.Text = "超時（秒）";
                    txtMaxOutputLabel.Text = "輸出上限（字元）";
                    txtCwdLabel.Text = "工作目錄";
                    txtCwdHint.Text = "留空則使用用戶主目錄。";
                    chkPersistCwd.Content = "跨命令保留 cd 結果（僅 PowerShell）";
                    chkFilterEnv.Content = "隱藏 KEY / SECRET / TOKEN 類環境變數";
                    txtFilterEnvHint.Text = "防止命令把你的 API 金鑰從環境變數裡讀走。";
                    txtWarningTitle.Text = "警告";
                    txtWarningDesc.Text = "啟用後，AI 可以執行可能修改系統的命令。僅在您能夠驗證命令安全性時啟用。";
                    btnSave.Content = "保存";
                    btnClose.Content = "關閉";
                    break;

                case "ja":
                    txtTitle.Text = "ターミナル設定";
                    txtShellInfoTitle.Text = "シェル情報";
                    txtPreferredShellLabel.Text = "優先シェル";
                    txtEnableTitle.Text = "AIにターミナルを許可";
                    txtEnableDesc.Text = "AIがターミナルコマンドを実行することを許可";
                    txtApprovalTitle.Text = "承認ポリシー";
                    chkAutoApproveReadOnly.Content = "読み取り専用コマンドを自動承認";
                    txtAutoApproveHint.Text = "Get-*、dir、ipconfig、git status などは確認なしで実行されます。リダイレクト、セミコロン、&&、部分式を含むコマンドは常に確認が必要です。";
                    chkAllowRemoteApproval.Content = "リモート（ブラウザ）承認を許可";
                    txtRemoteApprovalHint.Text = "危険。オフの場合、コマンドの確認は必ずこのデスクトップ上で行われます。";
                    txtPrefixLabel.Text = "常に許可するコマンドプレフィックス（1行に1つ）";
                    txtPrefixHint.Text = "トークン単位の前方一致：`git status` は `git status --short` も含みます。";
                    txtExecutionTitle.Text = "実行";
                    txtTimeoutLabel.Text = "タイムアウト（秒）";
                    txtMaxOutputLabel.Text = "出力の上限（文字）";
                    txtCwdLabel.Text = "作業ディレクトリ";
                    txtCwdHint.Text = "空欄の場合はユーザーのホームフォルダを使用します。";
                    chkPersistCwd.Content = "cd の結果をコマンド間で保持（PowerShellのみ）";
                    chkFilterEnv.Content = "KEY / SECRET / TOKEN 系の環境変数を隠す";
                    txtFilterEnvHint.Text = "コマンドが環境変数から API キーを読み取るのを防ぎます。";
                    txtWarningTitle.Text = "警告";
                    txtWarningDesc.Text = "有効にすると、AIはシステムを変更する可能性のあるコマンドを実行できます。コマンドの安全性を確認できる場合のみ有効にしてください。";
                    btnSave.Content = "保存";
                    btnClose.Content = "閉じる";
                    break;

                default: // English
                    break;
            }
        }

        private void LoadSettings(TerminalSettings settings)
        {
            chkEnableTerminal.IsChecked = settings.IsAuthorized;
            chkAutoApproveReadOnly.IsChecked = settings.AutoApproveReadOnly;
            chkAllowRemoteApproval.IsChecked = settings.AllowRemoteApproval;
            chkPersistCwd.IsChecked = settings.PersistWorkingDirectory;
            chkFilterEnv.IsChecked = settings.FilterSecretEnvironment;

            txtTimeout.Text = settings.TimeoutSeconds.ToString();
            txtMaxOutput.Text = settings.MaxOutputChars.ToString();
            txtWorkingDirectory.Text = settings.WorkingDirectory ?? "";
            txtAllowedPrefixes.Text = string.Join(Environment.NewLine, settings.AllowedPrefixes ?? new List<string>());

            var preferred = string.IsNullOrWhiteSpace(settings.PreferredShell) ? "auto" : settings.PreferredShell;
            cboPreferredShell.SelectedIndex = cboPreferredShell.Items
                .OfType<ComboBoxItem>()
                .Select((item, index) => (item, index))
                .FirstOrDefault(x => string.Equals(x.item.Tag as string, preferred, StringComparison.OrdinalIgnoreCase))
                .index;

            borderWarning.Visibility = settings.IsAuthorized ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateShellInfo()
        {
            var shellInfo = _plugin.GetShellInfo();

            txtCurrentShell.Text = _language switch
            {
                "zh-hans" => $"当前 Shell: {shellInfo.CurrentShell}",
                "zh-hant" => $"當前 Shell: {shellInfo.CurrentShell}",
                "ja" => $"現在のシェル: {shellInfo.CurrentShell}",
                _ => $"Current Shell: {shellInfo.CurrentShell}"
            };

            var available = _language switch
            {
                "zh-hans" => "可用",
                "zh-hant" => "可用",
                "ja" => "利用可能",
                _ => "Available"
            };

            var notFound = _language switch
            {
                "zh-hans" => "未找到",
                "zh-hant" => "未找到",
                "ja" => "見つかりません",
                _ => "Not Found"
            };

            txtPwshStatus.Text = $"PowerShell 7 (pwsh): {(shellInfo.HasPwsh ? available : notFound)}";
            txtPwshStatus.Foreground = shellInfo.HasPwsh
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Gray;

            txtPsStatus.Text = $"Windows PowerShell: {(shellInfo.HasPowerShell ? available : notFound)}";
            txtPsStatus.Foreground = shellInfo.HasPowerShell
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Gray;

            txtCmdStatus.Text = $"CMD: {available}";

            // 工作目录作为提示挂在目录输入框下面，方便看到 cd 之后的实际位置
            if (!string.IsNullOrEmpty(shellInfo.WorkingDirectory))
            {
                var prefix = _language switch
                {
                    "zh-hans" => "当前：",
                    "zh-hant" => "當前：",
                    "ja" => "現在：",
                    _ => "Currently: "
                };
                txtCwdHint.Text = prefix + shellInfo.WorkingDirectory + "\n" + txtCwdHint.Text;
            }
        }

        private void ChkEnableTerminal_Changed(object sender, RoutedEventArgs e)
        {
            borderWarning.Visibility = chkEnableTerminal.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _settings.IsAuthorized = chkEnableTerminal.IsChecked == true;
            _settings.HasConfirmedWarning = true;
            _settings.AutoApproveReadOnly = chkAutoApproveReadOnly.IsChecked == true;
            _settings.AllowRemoteApproval = chkAllowRemoteApproval.IsChecked == true;
            _settings.PersistWorkingDirectory = chkPersistCwd.IsChecked == true;
            _settings.FilterSecretEnvironment = chkFilterEnv.IsChecked == true;
            _settings.WorkingDirectory = txtWorkingDirectory.Text.Trim();

            if (int.TryParse(txtTimeout.Text.Trim(), out var timeout))
            {
                _settings.TimeoutSeconds = timeout;
            }
            if (int.TryParse(txtMaxOutput.Text.Trim(), out var maxOutput))
            {
                _settings.MaxOutputChars = maxOutput;
            }

            _settings.PreferredShell =
                (cboPreferredShell.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";

            _settings.AllowedPrefixes = txtAllowedPrefixes.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _onSave?.Invoke(_settings);

            // 插件侧会把超时/输出上限夹到合法区间，回填一次让用户看到实际生效的值
            txtTimeout.Text = _settings.TimeoutSeconds.ToString();
            txtMaxOutput.Text = _settings.MaxOutputChars.ToString();
            UpdateShellInfo();

            var message = _language switch
            {
                "zh-hans" => "设置已保存。",
                "zh-hant" => "設置已保存。",
                "ja" => "設定が保存されました。",
                _ => "Settings saved."
            };

            MessageBox.Show(message, "Terminal Plugin", MessageBoxButton.OK, MessageBoxImage.Information);
            CloseOwnerWindow();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseOwnerWindow();
        }

        /// <summary>
        /// 仅当本面板是某个薄壳窗口的直接内容时关闭该窗口（action(setting) 弹窗）；
        /// 内嵌在设置窗「插件面板」Tab 里时不动作，避免误关整个设置窗。
        /// </summary>
        private void CloseOwnerWindow()
        {
            var w = Window.GetWindow(this);
            if (w != null && ReferenceEquals(w.Content, this))
                w.Close();
        }
    }
}

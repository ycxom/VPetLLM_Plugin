using System;
using System.Windows;

namespace TerminalPlugin
{
    public partial class winTerminalSetting : Window
    {
        private readonly TerminalPlugin _plugin;
        private readonly string _language;
        private readonly Action<TerminalSettings> _onSave;

        public winTerminalSetting(TerminalPlugin plugin, TerminalSettings settings, Action<TerminalSettings> onSave)
        {
            InitializeComponent();
            _plugin = plugin;
            _language = plugin.GetLanguage();
            _onSave = onSave;

            ApplyLocalization();
            LoadSettings(settings);
            UpdateShellInfo();
        }

        private void ApplyLocalization()
        {
            switch (_language)
            {
                case "zh-hans":
                    Title = "终端插件 - 设置";
                    txtTitle.Text = "终端设置";
                    txtShellInfoTitle.Text = "Shell 信息";
                    txtEnableTitle.Text = "授权 AI 使用终端";
                    txtEnableDesc.Text = "允许 AI 执行终端命令";
                    txtWarningTitle.Text = "警告";
                    txtWarningDesc.Text = "启用后，AI 可以执行可能修改系统的命令。仅在您能够验证命令安全性时启用。";
                    btnSave.Content = "保存";
                    btnClose.Content = "关闭";
                    break;

                case "zh-hant":
                    Title = "終端插件 - 設置";
                    txtTitle.Text = "終端設置";
                    txtShellInfoTitle.Text = "Shell 資訊";
                    txtEnableTitle.Text = "授權 AI 使用終端";
                    txtEnableDesc.Text = "允許 AI 執行終端命令";
                    txtWarningTitle.Text = "警告";
                    txtWarningDesc.Text = "啟用後，AI 可以執行可能修改系統的命令。僅在您能夠驗證命令安全性時啟用。";
                    btnSave.Content = "保存";
                    btnClose.Content = "關閉";
                    break;

                case "ja":
                    Title = "ターミナルプラグイン - 設定";
                    txtTitle.Text = "ターミナル設定";
                    txtShellInfoTitle.Text = "シェル情報";
                    txtEnableTitle.Text = "AIにターミナルを許可";
                    txtEnableDesc.Text = "AIがターミナルコマンドを実行することを許可";
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
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGreen)
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);

            txtPsStatus.Text = $"Windows PowerShell: {(shellInfo.HasPowerShell ? available : notFound)}";
            txtPsStatus.Foreground = shellInfo.HasPowerShell
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGreen)
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);

            txtCmdStatus.Text = $"CMD: {available}";
        }

        private void ChkEnableTerminal_Changed(object sender, RoutedEventArgs e)
        {
            borderWarning.Visibility = chkEnableTerminal.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var settings = new TerminalSettings
            {
                IsAuthorized = chkEnableTerminal.IsChecked == true,
                HasConfirmedWarning = true
            };

            _onSave?.Invoke(settings);

            var message = _language switch
            {
                "zh-hans" => "设置已保存。",
                "zh-hant" => "設置已保存。",
                "ja" => "設定が保存されました。",
                _ => "Settings saved."
            };

            MessageBox.Show(message, "Terminal Plugin", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

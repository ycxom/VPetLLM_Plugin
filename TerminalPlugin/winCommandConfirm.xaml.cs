using System.Windows;

namespace TerminalPlugin
{
    public partial class winCommandConfirm : Window
    {
        private readonly string _language;

        public bool IsConfirmed { get; private set; } = false;
        public string Command { get; private set; } = "";

        /// <param name="dangerReason">
        /// 安全评估给出的高危理由；非空时弹窗顶部会展示红色告警条。
        /// 命令仍然可以放行 —— 判定为"高危"只是要求用户显式确认，
        /// 真正不可挽回的操作在到达这里之前就已经被拒绝了。
        /// </param>
        public winCommandConfirm(string command, string shellName, string language = "en", string dangerReason = "")
        {
            InitializeComponent();
            _language = language;
            Command = command;
            txtCommand.Text = command;

            ApplyLocalization(shellName);
            ApplyDangerState(dangerReason);
        }

        private void ApplyDangerState(string dangerReason)
        {
            if (string.IsNullOrWhiteSpace(dangerReason)) return;

            borderDanger.Visibility = Visibility.Visible;
            txtDangerReason.Text = dangerReason;
            txtDangerTitle.Text = _language switch
            {
                "zh-hans" => "高危命令 — 请确认你清楚它会做什么",
                "zh-hant" => "高危命令 — 請確認你清楚它會做什麼",
                "ja" => "危険なコマンド — 内容を理解した上で実行してください",
                _ => "High-risk command — make sure you understand what it does"
            };

            // 高危时把默认焦点放到取消按钮，避免顺手回车执行
            btnCancel.Focus();
        }

        private void ApplyLocalization(string shellName)
        {
            var shellLabel = _language switch
            {
                "zh-hans" => "终端",
                "zh-hant" => "終端",
                "ja" => "シェル",
                _ => "Shell"
            };

            txtShellInfo.Text = $"{shellLabel}: {shellName}";

            switch (_language)
            {
                case "zh-hans":
                    Title = "终端 - 命令确认";
                    txtTitle.Text = "AI 请求执行以下命令：";
                    txtWarning.Text = "请在执行前验证命令内容。如有需要，您可以修改命令。";
                    btnExecute.Content = "执行";
                    btnCancel.Content = "取消";
                    break;

                case "zh-hant":
                    Title = "終端 - 命令確認";
                    txtTitle.Text = "AI 請求執行以下命令：";
                    txtWarning.Text = "請在執行前驗證命令內容。如有需要，您可以修改命令。";
                    btnExecute.Content = "執行";
                    btnCancel.Content = "取消";
                    break;

                case "ja":
                    Title = "ターミナル - コマンド確認";
                    txtTitle.Text = "AIが以下のコマンドの実行を要求しています：";
                    txtWarning.Text = "実行前にコマンドを確認してください。必要に応じて変更できます。";
                    btnExecute.Content = "実行";
                    btnCancel.Content = "キャンセル";
                    break;

                default: // English
                    Title = "Terminal - Command Confirmation";
                    txtTitle.Text = "AI requests to execute the following command:";
                    txtWarning.Text = "Please verify the command before execution. You can modify it if needed.";
                    btnExecute.Content = "Execute";
                    btnCancel.Content = "Cancel";
                    break;
            }
        }

        private void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            Command = txtCommand.Text.Trim();
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}

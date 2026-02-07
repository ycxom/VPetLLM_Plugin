using System.Windows;

namespace TerminalPlugin
{
    public partial class winCommandConfirm : Window
    {
        private readonly string _language;

        public bool IsConfirmed { get; private set; } = false;
        public string Command { get; private set; } = "";

        public winCommandConfirm(string command, string shellName, string language = "en")
        {
            InitializeComponent();
            _language = language;
            Command = command;
            txtCommand.Text = command;

            ApplyLocalization(shellName);
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

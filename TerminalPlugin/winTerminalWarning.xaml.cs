using System;
using System.Windows;
using System.Windows.Media;

namespace TerminalPlugin
{
    public partial class winTerminalWarning : Window
    {
        private readonly string _language;
        private readonly string _confirmText;
        public bool IsConfirmed { get; private set; } = false;

        public winTerminalWarning(string language = "en")
        {
            InitializeComponent();
            _language = language;
            _confirmText = GetConfirmText();
            ApplyLocalization();
        }

        private string GetConfirmText()
        {
            return _language switch
            {
                "zh-hans" => "我知晓风险",
                "zh-hant" => "我知曉風險",
                "ja" => "リスクを理解しています",
                _ => "I understand the risks"
            };
        }

        private void ApplyLocalization()
        {
            switch (_language)
            {
                case "zh-hans":
                    Title = "终端插件 - 安全警告";
                    txtTitle.Text = "安全警告";
                    txtWarningContent.Inlines.Clear();
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("此插件允许 AI 在您的计算机上执行终端命令。") { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("在启用此插件之前，请了解："));
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("1. AI 可能执行损害系统的命令") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("2. 您必须能够在 AI 执行命令前理解并验证命令") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("3. 如果您无法判断命令是否安全，请勿启用此插件") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("如需继续，请在下方输入确认文字：") { FontWeight = FontWeights.Bold });
                    txtConfirmHint.Text = "请输入：我知晓风险";
                    btnConfirm.Content = "确认";
                    btnCancel.Content = "取消";
                    break;

                case "zh-hant":
                    Title = "終端插件 - 安全警告";
                    txtTitle.Text = "安全警告";
                    txtWarningContent.Inlines.Clear();
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("此插件允許 AI 在您的電腦上執行終端命令。") { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("在啟用此插件之前，請了解："));
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("1. AI 可能執行損害系統的命令") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("2. 您必須能夠在 AI 執行命令前理解並驗證命令") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("3. 如果您無法判斷命令是否安全，請勿啟用此插件") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("如需繼續，請在下方輸入確認文字：") { FontWeight = FontWeights.Bold });
                    txtConfirmHint.Text = "請輸入：我知曉風險";
                    btnConfirm.Content = "確認";
                    btnCancel.Content = "取消";
                    break;

                case "ja":
                    Title = "ターミナルプラグイン - セキュリティ警告";
                    txtTitle.Text = "セキュリティ警告";
                    txtWarningContent.Inlines.Clear();
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("このプラグインは、AIがコンピュータ上でターミナルコマンドを実行することを許可します。") { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("このプラグインを有効にする前に、以下をご理解ください："));
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("1. AIがシステムに損害を与えるコマンドを実行する可能性があります") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("2. AIがコマンドを実行する前に、コマンドを理解し検証できる必要があります") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("3. コマンドが安全かどうか判断できない場合は、このプラグインを有効にしないでください") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")) });
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.LineBreak());
                    txtWarningContent.Inlines.Add(new System.Windows.Documents.Run("続行するには、以下の確認テキストを入力してください：") { FontWeight = FontWeights.Bold });
                    txtConfirmHint.Text = "入力してください：リスクを理解しています";
                    btnConfirm.Content = "確認";
                    btnCancel.Content = "キャンセル";
                    break;

                default: // English
                    // Already set in XAML
                    txtConfirmHint.Text = $"Please enter: {_confirmText}";
                    break;
            }
        }

        private void TxtConfirmInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            bool isMatch = txtConfirmInput.Text.Trim().Equals(_confirmText, StringComparison.OrdinalIgnoreCase);
            btnConfirm.IsEnabled = isMatch;

            if (isMatch)
            {
                btnConfirm.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                btnConfirm.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                btnConfirm.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A4A4A"));
                btnConfirm.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
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

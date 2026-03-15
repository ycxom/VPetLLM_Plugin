using System;
using System.Windows;
using System.Windows.Threading;

namespace AppLauncherPlugin
{
    public partial class winLaunchConfirm : Window
    {
        private readonly DispatcherTimer _timer;
        private int _remainingSeconds;

        public bool IsConfirmed { get; private set; } = false;

        public winLaunchConfirm(string appName, string language = "en", int timeoutSeconds = 15)
        {
            InitializeComponent();
            _remainingSeconds = timeoutSeconds;
            txtAppName.Text = appName;

            ApplyLocalization(language);
            UpdateCountdownText();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void ApplyLocalization(string language)
        {
            switch (language)
            {
                case "zh-hans":
                    Title = "应用启动器 - 启动确认";
                    txtTitle.Text = "AI 请求启动以下应用程序：";
                    btnLaunch.Content = "启动";
                    btnCancel.Content = "取消";
                    break;
                case "zh-hant":
                    Title = "應用啟動器 - 啟動確認";
                    txtTitle.Text = "AI 請求啟動以下應用程序：";
                    btnLaunch.Content = "啟動";
                    btnCancel.Content = "取消";
                    break;
                case "ja":
                    Title = "アプリランチャー - 起動確認";
                    txtTitle.Text = "AIが以下のアプリの起動を要求しています：";
                    btnLaunch.Content = "起動";
                    btnCancel.Content = "キャンセル";
                    break;
                default:
                    Title = "AppLauncher - Launch Confirmation";
                    txtTitle.Text = "AI requests to launch the following application:";
                    btnLaunch.Content = "Launch";
                    btnCancel.Content = "Cancel";
                    break;
            }
        }

        private void UpdateCountdownText()
        {
            var template = GetLanguage() switch
            {
                "zh-hans" => "将在 {0} 秒后自动拒绝",
                "zh-hant" => "將在 {0} 秒後自動拒絕",
                "ja" => "{0} 秒後に自動的に拒否されます",
                _ => "Auto-reject in {0} seconds"
            };
            txtCountdown.Text = string.Format(template, _remainingSeconds);
        }

        private string GetLanguage()
        {
            if (Title.StartsWith("应用启动器")) return "zh-hans";
            if (Title.StartsWith("應用啟動器")) return "zh-hant";
            if (Title.StartsWith("アプリランチャー")) return "ja";
            return "en";
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                _timer.Stop();
                IsConfirmed = false;
                DialogResult = false;
                Close();
                return;
            }
            UpdateCountdownText();
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}

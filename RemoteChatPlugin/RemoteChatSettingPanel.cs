using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RemoteChatPlugin;

/// <summary>
/// RemoteChatPlugin 的设置面板（代码构建，保持单 DLL 分发）。
/// 让自托管用户直接在 VPetLLM 设置窗口里填写自己的中继/Web 地址、配对、查看状态，
/// 而不必用 LLM 聊天命令配置。
/// </summary>
internal sealed class RemoteChatSettingPanel : UserControl
{
    private readonly RemoteChatPlugin _plugin;
    private readonly RadioButton _officialRadio;
    private readonly RadioButton _selfRadio;
    private readonly TextBox _serverBox;
    private readonly TextBox _webBox;
    private readonly TextBlock _statusText;
    private readonly TextBlock _resultText;
    private readonly DispatcherTimer _statusTimer;
    private readonly bool _zh;

    internal RemoteChatSettingPanel(RemoteChatPlugin plugin)
    {
        _plugin = plugin;
        _zh = plugin.Language.StartsWith("zh");

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(Header(T("远端聊天服务器", "Remote Chat Server")));

        // 模式切换：使用 VPetLLM 官方聊天 / 使用自部署
        _officialRadio = new RadioButton
        {
            Content = T("使用 VPetLLM 聊天（官方服务器，开箱即用）", "Use VPetLLM Chat (official server)"),
            GroupName = "mode",
            Margin = new Thickness(0, 6, 0, 4)
        };
        _selfRadio = new RadioButton
        {
            Content = T("使用自部署（连接你自己的中继/网页）", "Use self-hosted (your own relay/web)"),
            GroupName = "mode",
            Margin = new Thickness(0, 0, 0, 4)
        };
        _officialRadio.IsChecked = _plugin.UseOfficialServer;
        _selfRadio.IsChecked = !_plugin.UseOfficialServer;
        _officialRadio.Checked += (_, _) => ApplyMode();
        _selfRadio.Checked += (_, _) => ApplyMode();
        root.Children.Add(_officialRadio);
        root.Children.Add(_selfRadio);

        root.Children.Add(Label(T("中继地址（WebSocket）", "Relay URL (WebSocket)")));
        _serverBox = InputBox("wss://chat.example.com/");
        root.Children.Add(_serverBox);

        root.Children.Add(Label(T("网页地址（浏览器打开）", "Web URL (open in browser)")));
        _webBox = InputBox("https://chat.example.com");
        root.Children.Add(_webBox);

        _serverBox.Text = _plugin.CurrentServerUrl;
        _webBox.Text = _plugin.CurrentWebUrl;

        var historyCheck = new CheckBox
        {
            Content = T("允许远端回看本地聊天历史（隐私）", "Let remote view local chat history (privacy)"),
            IsChecked = _plugin.ShareHistory,
            Margin = new Thickness(0, 14, 0, 0)
        };
        historyCheck.Checked += (_, _) => _plugin.ShareHistory = true;
        historyCheck.Unchecked += (_, _) => _plugin.ShareHistory = false;
        root.Children.Add(historyCheck);
        root.Children.Add(Hint(T(
            "开启后，浏览器可拉取近期用户/助手对话正文用于了解上下文（类似 Open WebUI 的会话回看）。默认关闭。",
            "When on, the browser can fetch recent user/assistant messages for context (like Open WebUI). Off by default.")));

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Button(T("保存并连接", "Save & Connect"), OnSave));
        buttons.Children.Add(Button(T("复制接入密钥", "Copy Access Key"), OnCopyKey));
        buttons.Children.Add(Button(T("复制网页链接", "Copy Web Link"), OnPair));
        buttons.Children.Add(Button(T("轮换密钥", "Rotate Key"), OnRotate));
        buttons.Children.Add(Button(T("重新连接", "Reconnect"), OnReconnect));
        root.Children.Add(buttons);

        _statusText = new TextBlock { Margin = new Thickness(0, 14, 0, 0), FontWeight = FontWeights.SemiBold };
        root.Children.Add(_statusText);

        _resultText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray
        };
        root.Children.Add(_resultText);

        root.Children.Add(Hint(T(
            "配对链接含高权限密钥，只会复制到本地剪贴板，不会发给中继或大模型。请勿分享给不信任的人。",
            "The pairing link contains a high-privilege secret, copied only to your local clipboard — never sent to the relay or LLM. Do not share it with untrusted people.")));

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

        ApplyMode();
        UpdateStatus();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        Loaded += (_, _) => _statusTimer.Start();
        Unloaded += (_, _) => _statusTimer.Stop();
    }

    private async void OnSave(object? s, RoutedEventArgs e)
        => ShowResult(await _plugin.SaveEndpointsAsync(_officialRadio.IsChecked == true, _serverBox.Text, _webBox.Text));

    private void ApplyMode()
    {
        var self = _selfRadio.IsChecked == true;
        _serverBox.IsEnabled = self;
        _webBox.IsEnabled = self;
    }

    private void OnPair(object? s, RoutedEventArgs e) => ShowResult(_plugin.CopyPairing());

    private void OnCopyKey(object? s, RoutedEventArgs e) => ShowResult(_plugin.CopyAccessKey());

    private async void OnRotate(object? s, RoutedEventArgs e)
    {
        var r = _zh ? "确认轮换配对密钥？现有已配对的浏览器将全部失效。"
                    : "Rotate the pairing key? All currently paired browsers will stop working.";
        if (MessageBox.Show(r, TabTitleText(), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        ShowResult(await _plugin.RotateAsync());
    }

    private async void OnReconnect(object? s, RoutedEventArgs e)
    {
        await _plugin.ReconnectAsync();
        ShowResult(T("已触发重新连接。", "Reconnect triggered."));
    }

    private void UpdateStatus()
    {
        var raw = _plugin.StatusText;
        var human = raw switch
        {
            "connected" => T("已连接", "Connected"),
            "connecting" => T("连接中", "Connecting"),
            "disconnected" => T("已断开", "Disconnected"),
            "not_configured" => T("未配置地址", "Not configured"),
            "stopped" => T("已停止", "Stopped"),
            _ => raw
        };
        _statusText.Text = T("状态：", "Status: ") + human;
    }

    private void ShowResult(string text) => _resultText.Text = text;

    private string TabTitleText() => T("远端聊天", "Remote Chat");
    private string T(string zh, string en) => _zh ? zh : en;

    private static TextBlock Header(string text) => new()
    { Text = text, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };

    private static TextBlock Label(string text) => new()
    { Text = text, Margin = new Thickness(0, 10, 0, 4) };

    private static TextBlock Hint(string text) => new()
    { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) };

    private static TextBox InputBox(string placeholder) => new()
    { MinWidth = 340, Padding = new Thickness(6, 4, 6, 4), ToolTip = placeholder };

    private static Button Button(string text, RoutedEventHandler onClick)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 5, 12, 5), MinWidth = 96 };
        b.Click += onClick;
        return b;
    }
}

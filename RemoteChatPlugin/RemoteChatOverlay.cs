using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RemoteChatPlugin;

/// <summary>
/// 桌面覆盖层：浏览器接入时弹出的 3 秒确认窗口，以及接入后常驻右下角的小挂件。
/// 全部操作都编排到 UI 线程，保持插件单 DLL、无 XAML。
/// </summary>
internal sealed class RemoteChatOverlay
{
    private readonly bool _zh;
    private Window? _approval;
    private Window? _widget;
    private DispatcherTimer? _widgetTimer;

    internal RemoteChatOverlay(bool zh) => _zh = zh;

    private string T(string zh, string en) => _zh ? zh : en;

    // ---------- 接入确认弹窗（3 秒自动同意）----------

    internal void ShowApproval(int seconds, Action onApprove, Action onResetKey)
    {
        Invoke(() =>
        {
            CloseApprovalCore();
            var fired = false;
            void Fire(Action a) { if (fired) return; fired = true; a(); }

            var remaining = seconds;
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock
            {
                Text = T("检测到 WebUI 接入聊天", "A WebUI client is connecting"),
                FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 6)
            });
            panel.Children.Add(new TextBlock
            {
                Text = T("有人使用你的接入密钥连接到远端聊天。", "Someone connected to remote chat with your access key."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var resetBtn = new Button
            {
                Content = T("断开并重置密钥", "Disconnect & reset key"),
                MinWidth = 130, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 5, 10, 5)
            };
            var agreeBtn = new Button
            {
                Content = $"{T("同意", "Agree")} ({remaining})",
                MinWidth = 96, Padding = new Thickness(10, 5, 10, 5), IsDefault = true
            };
            buttons.Children.Add(resetBtn);
            buttons.Children.Add(agreeBtn);
            panel.Children.Add(buttons);

            var win = NewToolWindow(T("WebUI 接入确认", "WebUI access confirmation"));
            win.Content = panel;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                remaining--;
                if (remaining <= 0)
                {
                    timer.Stop();
                    Fire(onApprove);
                    win.Close();
                }
                else agreeBtn.Content = $"{T("同意", "Agree")} ({remaining})";
            };
            agreeBtn.Click += (_, _) => { timer.Stop(); Fire(onApprove); win.Close(); };
            resetBtn.Click += (_, _) => { timer.Stop(); Fire(onResetKey); win.Close(); };
            win.Closed += (_, _) => timer.Stop();

            _approval = win;
            PositionBottomRight(win, 1);
            win.Show();
            timer.Start();
        });
    }

    internal void CloseApproval() => Invoke(CloseApprovalCore);

    private void CloseApprovalCore()
    {
        try { _approval?.Close(); } catch { /* already closing */ }
        _approval = null;
    }

    // ---------- 接入后常驻右下角挂件 ----------

    internal void ShowWidget(Func<string> statusProvider, Action onDisconnect, Action onResetKey)
    {
        Invoke(() =>
        {
            if (_widget is not null) return;

            var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            title.Children.Add(new Ellipse
            {
                Width = 9, Height = 9, Fill = Brushes.LimeGreen,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
            title.Children.Add(new TextBlock
            {
                Text = T("WebUI 接入聊天", "WebUI chat connected"),
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(title);

            var statusText = new TextBlock { FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(statusText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var disconnectBtn = new Button
            {
                Content = T("断开", "Disconnect"),
                MinWidth = 60, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 3, 8, 3)
            };
            var resetBtn = new Button
            {
                Content = T("断开并重置密钥", "Disconnect & reset key"),
                Padding = new Thickness(8, 3, 8, 3)
            };
            disconnectBtn.Click += (_, _) => onDisconnect();
            resetBtn.Click += (_, _) => onResetKey();
            buttons.Children.Add(disconnectBtn);
            buttons.Children.Add(resetBtn);
            panel.Children.Add(buttons);

            var win = NewToolWindow(T("WebUI 接入聊天", "WebUI chat"));
            win.Content = panel;
            _widget = win;
            PositionBottomRight(win, 0);
            win.Show();

            void Refresh() { try { statusText.Text = statusProvider(); } catch { } }
            Refresh();
            _widgetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _widgetTimer.Tick += (_, _) => Refresh();
            _widgetTimer.Start();
            win.Closed += (_, _) => { _widgetTimer?.Stop(); _widgetTimer = null; };
        });
    }

    internal void HideWidget() => Invoke(() =>
    {
        _widgetTimer?.Stop();
        _widgetTimer = null;
        try { _widget?.Close(); } catch { /* already closing */ }
        _widget = null;
    });

    internal void Dispose() => Invoke(() => { CloseApprovalCore(); _widgetTimer?.Stop(); try { _widget?.Close(); } catch { } _widget = null; });

    // ---------- 公共 ----------

    private static Window NewToolWindow(string title) => new()
    {
        Title = title,
        SizeToContent = SizeToContent.WidthAndHeight,
        ResizeMode = ResizeMode.NoResize,
        WindowStyle = WindowStyle.ToolWindow,
        ShowInTaskbar = false,
        Topmost = true,
        ShowActivated = false,
        MinWidth = 260
    };

    /// <summary>把窗口贴到工作区右下角；stackIndex 用于让确认弹窗叠在挂件上方。</summary>
    private static void PositionBottomRight(Window win, int stackIndex)
    {
        win.WindowStartupLocation = WindowStartupLocation.Manual;
        void Place()
        {
            var area = SystemParameters.WorkArea;
            win.Left = area.Right - win.ActualWidth - 16;
            win.Top = area.Bottom - win.ActualHeight - 16 - stackIndex * (win.ActualHeight + 12);
        }
        win.Loaded += (_, _) => Place();
        win.SizeChanged += (_, _) => Place();
    }

    private static void Invoke(Action action)
    {
        var app = Application.Current;
        if (app?.Dispatcher is null) return;
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }
}

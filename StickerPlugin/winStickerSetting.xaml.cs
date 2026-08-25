using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StickerPlugin.Services;

namespace StickerPlugin
{
    /// <summary>
    /// winStickerSetting.xaml 的交互逻辑。
    ///
    /// 本插件自身没有可配置项 —— 所有参数都归前置 MOD「LLM表情包」管。
    /// 这个面板只负责三件事：报告 MOD 的可用状态、把用户引导到该装/该开的那一步、
    /// 以及提供一个不用发消息就能验证链路的测试入口。
    /// </summary>
    public partial class winStickerSetting : UserControl
    {
        private static readonly Brush Ok = new SolidColorBrush(Color.FromRgb(16, 124, 16));
        private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(202, 80, 16));
        private static readonly Brush Error = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(96, 94, 92));

        private readonly StickerPlugin _plugin;

        public winStickerSetting(StickerPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            var bridge = _plugin.GetBridge();
            if (bridge is null)
            {
                Render(Error, "VPet 主窗口尚未就绪",
                    "插件还没拿到宿主窗口，稍后再打开本面板即可。", canOpenSettings: false, canTest: false);
                txtModParams.Text = "—";
                return;
            }

            switch (bridge.GetStatus())
            {
                case ImagePluginStatus.NotInstalled:
                    Render(Error, $"未安装前置 MOD「{ImagePluginBridge.ImagePluginName}」",
                        "表情包的显示与图库全部由该 MOD 提供。请到 Steam 创意工坊订阅后重启 VPet。\n" +
                        ImagePluginBridge.WorkshopUrl,
                        canOpenSettings: false, canTest: false);
                    break;

                case ImagePluginStatus.Incompatible:
                    Render(Error, $"「{ImagePluginBridge.ImagePluginName}」版本过旧",
                        "已找到该 MOD，但它缺少本插件需要的调用入口。请在创意工坊更新到最新版后重启 VPet。",
                        canOpenSettings: true, canTest: false);
                    break;

                case ImagePluginStatus.OnlineLibraryDisabled:
                    Render(Warn, "在线网络表情包库未启用",
                        $"「{ImagePluginBridge.ImagePluginName}」已安装，但它的「在线网络表情包库」开关是关的，" +
                        "表情包发不出来。点下面的按钮打开 MOD 设置并勾选该项。",
                        canOpenSettings: true, canTest: false);
                    break;

                case ImagePluginStatus.Ready:
                    Render(Ok, "就绪",
                        $"已连接「{ImagePluginBridge.ImagePluginName}」，在线表情包库已启用。",
                        canOpenSettings: true, canTest: true);
                    break;
            }

            txtModParams.Text = bridge.IsPluginFound
                ? $"显示时长 {bridge.DisplayDurationSeconds} 秒 · 释放给模型的标签数 {bridge.TagCount} 个\n" +
                  "（以上取自「LLM表情包」MOD 的设置，改动请在那边进行）"
                : "—";
        }

        private void Render(Brush color, string title, string detail, bool canOpenSettings, bool canTest)
        {
            txtStatusTitle.Text = title;
            txtStatusTitle.Foreground = color;
            txtStatusDetail.Text = detail;
            brdStatus.BorderBrush = color;

            btnOpenModSettings.IsEnabled = canOpenSettings;
            btnTestSticker.IsEnabled = canTest;
            btnTestConnection.IsEnabled = canTest;
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();

        private void btnOpenModSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_plugin.GetBridge()?.OpenModSettings() != true)
            {
                MessageBox.Show($"未能唤起「{ImagePluginBridge.ImagePluginName}」的设置窗口，请确认该 MOD 已安装并启用。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnCopyWorkshop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ImagePluginBridge.WorkshopUrl);
                SetResult("创意工坊链接已复制到剪贴板。", Ok);
            }
            catch (Exception ex)
            {
                SetResult($"复制失败: {ex.Message}", Error);
            }
        }

        private async void btnTestSticker_Click(object sender, RoutedEventArgs e)
        {
            var tags = txtTestTags.Text.Trim();
            if (string.IsNullOrWhiteSpace(tags))
            {
                SetResult("请输入测试标签。", Warn);
                return;
            }

            var bridge = _plugin.GetBridge();
            if (bridge is null)
                return;

            btnTestSticker.IsEnabled = false;
            btnTestSticker.Content = "搜索中…";
            SetResult("正在让 MOD 搜索并显示表情包…", Muted);

            try
            {
                var shown = await bridge.ShowStickerAsync(tags);
                SetResult(shown ? "表情包已显示。" : "没有显示出来：可能无匹配结果，或在线库未启用。", shown ? Ok : Warn);
            }
            catch (Exception ex)
            {
                SetResult($"测试失败: {ex.Message}", Error);
            }
            finally
            {
                btnTestSticker.IsEnabled = true;
                btnTestSticker.Content = "测试发送";
                RefreshStatus();
            }
        }

        private async void btnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            var bridge = _plugin.GetBridge();
            if (bridge is null)
                return;

            btnTestConnection.IsEnabled = false;
            btnTestConnection.Content = "测试中…";
            SetResult("正在测试图库服务连接…", Muted);

            try
            {
                var ok = await bridge.TestConnectionAsync();
                SetResult(ok ? "连接成功。" : "连接失败，请在 MOD 设置里检查服务地址与 API Key。", ok ? Ok : Error);
            }
            catch (Exception ex)
            {
                SetResult($"测试失败: {ex.Message}", Error);
            }
            finally
            {
                btnTestConnection.IsEnabled = true;
                btnTestConnection.Content = "测试连接";
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e) => CloseOwnerWindow();

        private void SetResult(string text, Brush color)
        {
            txtTestResult.Text = text;
            txtTestResult.Foreground = color;
        }
    }
}

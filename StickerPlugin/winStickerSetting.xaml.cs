using System.Windows;
using Microsoft.Win32;
using StickerPlugin.Models;

namespace StickerPlugin
{
    /// <summary>
    /// winStickerSetting.xaml 的交互逻辑
    /// </summary>
    public partial class winStickerSetting : Window
    {
        private readonly StickerPlugin _plugin;
        private readonly PluginSettings _settings;

        public winStickerSetting(StickerPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            _settings = plugin.GetSettings().Clone();
            LoadSettingsToUI();
        }

        private void LoadSettingsToUI()
        {
            // 内置凭证设置
            chkUseBuiltInCredentials.IsChecked = _settings.UseBuiltInCredentials;
            // 只加载自定义凭证（不显示内置凭证）
            txtServiceUrl.Text = _settings.ServiceUrl;
            txtApiKey.Password = _settings.ApiKey;
            UpdateCredentialsUI();

            txtTagCount.Text = _settings.TagCount.ToString();
            txtCacheDuration.Text = _settings.CacheDurationMinutes.ToString();
            txtDisplayDuration.Text = _settings.DisplayDurationSeconds.ToString();
            
            // 显示有效的 DLL 路径（自动查找或配置值）
            var effectivePath = _settings.GetEffectiveDllPath();
            txtDllPath.Text = effectivePath;
            
            // 如果是自动查找的路径，显示提示
            if (string.IsNullOrEmpty(_settings.ImagePluginDllPath) && !string.IsNullOrEmpty(effectivePath))
            {
                txtDllPath.ToolTip = "自动检测到的路径";
            }
        }

        private void UpdateCredentialsUI()
        {
            var useBuiltIn = chkUseBuiltInCredentials.IsChecked == true;
            // 使用内置凭证时完全隐藏自定义凭证面板
            pnlCustomCredentials.Visibility = useBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        }

        private void chkUseBuiltInCredentials_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCredentialsUI();
        }

        private bool SaveUIToSettings()
        {
            _settings.UseBuiltInCredentials = chkUseBuiltInCredentials.IsChecked == true;
            _settings.ServiceUrl = txtServiceUrl.Text.Trim();
            _settings.ApiKey = txtApiKey.Password;
            _settings.ImagePluginDllPath = txtDllPath.Text.Trim();

            // 验证自定义凭证
            if (!_settings.UseBuiltInCredentials)
            {
                if (string.IsNullOrWhiteSpace(_settings.ServiceUrl))
                {
                    MessageBox.Show("使用自定义凭证时，服务地址不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            // 解析数值
            if (!int.TryParse(txtTagCount.Text, out var tagCount))
            {
                MessageBox.Show("标签数量必须是有效的整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _settings.TagCount = tagCount;

            if (!int.TryParse(txtCacheDuration.Text, out var cacheDuration))
            {
                MessageBox.Show("缓存时长必须是有效的整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _settings.CacheDurationMinutes = cacheDuration;

            if (!int.TryParse(txtDisplayDuration.Text, out var displayDuration))
            {
                MessageBox.Show("显示时长必须是有效的整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _settings.DisplayDurationSeconds = displayDuration;

            // 验证设置
            _settings.Validate();

            return true;
        }

        private async void btnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            btnTestConnection.IsEnabled = false;
            btnTestConnection.Content = "测试中...";

            try
            {
                // 获取实际使用的凭证
                string serviceUrl, apiKey;
                if (chkUseBuiltInCredentials.IsChecked == true)
                {
                    serviceUrl = ApiCredentials.GetBuiltInServiceUrl();
                    apiKey = ApiCredentials.GetBuiltInApiKey();
                }
                else
                {
                    serviceUrl = txtServiceUrl.Text.Trim();
                    apiKey = txtApiKey.Password;
                }

                // 临时创建服务测试连接
                using var service = new Services.ImageVectorService(serviceUrl, apiKey);

                var result = await service.HealthCheckAsync();

                if (result)
                {
                    MessageBox.Show("连接成功！", "测试结果", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("连接失败，请检查服务地址和 API Key。", "测试结果", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连接失败: {ex.Message}", "测试结果", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnTestConnection.IsEnabled = true;
                btnTestConnection.Content = "测试连接";
            }
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 VPet.Plugin.Imgae.dll",
                Filter = "DLL 文件 (*.dll)|*.dll",
                FileName = "VPet.Plugin.Imgae.dll"
            };

            if (dialog.ShowDialog() == true)
            {
                txtDllPath.Text = dialog.FileName;
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveUIToSettings())
            {
                return;
            }

            _plugin.UpdateSettings(_settings);
            MessageBox.Show("设置已保存。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void btnTestSticker_Click(object sender, RoutedEventArgs e)
        {
            var tags = txtTestTags.Text.Trim();
            if (string.IsNullOrWhiteSpace(tags))
            {
                txtTestResult.Text = "请输入测试标签";
                txtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 100, 100));
                return;
            }

            btnTestSticker.IsEnabled = false;
            btnTestSticker.Content = "搜索中...";
            txtTestResult.Text = "正在搜索表情包...";
            txtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170));

            try
            {
                // 调用插件的 Function 方法测试
                var result = await _plugin.Function($"action(send), tags({tags})");
                
                if (string.IsNullOrEmpty(result))
                {
                    txtTestResult.Text = "表情包已发送（如果 DLL 已配置）";
                    txtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 200, 100));
                }
                else
                {
                    txtTestResult.Text = result;
                    txtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 180, 100));
                }
            }
            catch (Exception ex)
            {
                txtTestResult.Text = $"测试失败: {ex.Message}";
                txtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 100, 100));
            }
            finally
            {
                btnTestSticker.IsEnabled = true;
                btnTestSticker.Content = "测试发送";
            }
        }
    }
}

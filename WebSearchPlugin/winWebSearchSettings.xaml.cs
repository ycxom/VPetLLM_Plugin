using System;
using System.Windows;

namespace WebSearchPlugin
{
    public partial class winWebSearchSettings : Window
    {
        private WebSearchSettings _settings;
        private Action<WebSearchSettings>? _onSaved;

        public winWebSearchSettings(WebSearchSettings settings, Action<WebSearchSettings>? onSaved = null)
        {
            InitializeComponent();
            _settings = settings;
            _onSaved = onSaved;
            LoadSettings();
            UpdateProxyUI();
        }

        private void LoadSettings()
        {
            // 代理设置
            chkUseVPetLLMProxy.IsChecked = _settings.Proxy.UseVPetLLMProxy;
            chkEnableCustomProxy.IsChecked = _settings.Proxy.EnableCustomProxy;
            chkUseSystemProxy.IsChecked = _settings.Proxy.UseSystemProxy;
            
            // 代理协议
            switch (_settings.Proxy.Protocol.ToLower())
            {
                case "http":
                    cmbProtocol.SelectedIndex = 0;
                    break;
                case "https":
                    cmbProtocol.SelectedIndex = 1;
                    break;
                case "socks5":
                    cmbProtocol.SelectedIndex = 2;
                    break;
                default:
                    cmbProtocol.SelectedIndex = 0;
                    break;
            }
            
            txtProxyAddress.Text = _settings.Proxy.Address;

            // API 设置
            chkUseApiMode.IsChecked = _settings.Api.UseApiMode;
            chkUseBuiltInCredentials.IsChecked = _settings.Api.UseBuiltInCredentials;
            txtApiUrl.Text = _settings.Api.ApiUrl;
            txtBearerToken.Text = _settings.Api.BearerToken;
            chkEnableFallback.IsChecked = _settings.Api.EnableFallback;
            UpdateApiUI();
            UpdateBuiltInCredentialsUI();

            // 搜索设置
            txtMaxResults.Text = _settings.MaxSearchResults.ToString();
            txtMaxContentLength.Text = _settings.MaxContentLength.ToString();
        }

        private void UpdateProxyUI()
        {
            // 如果使用 VPetLLM 代理，禁用自定义代理
            if (chkUseVPetLLMProxy.IsChecked == true)
            {
                chkEnableCustomProxy.IsEnabled = false;
                chkEnableCustomProxy.IsChecked = false;
            }
            else
            {
                chkEnableCustomProxy.IsEnabled = true;
            }

            // 自定义代理面板可见性
            pnlCustomProxy.IsEnabled = chkEnableCustomProxy.IsChecked == true;

            // 代理地址配置可见性
            if (chkUseSystemProxy.IsChecked == true)
            {
                pnlProxyAddress.IsEnabled = false;
            }
            else
            {
                pnlProxyAddress.IsEnabled = true;
            }
        }

        private void OnProxyModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateProxyUI();
        }

        private void OnSystemProxyChanged(object sender, RoutedEventArgs e)
        {
            UpdateProxyUI();
        }

        private void OnApiModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateApiUI();
        }

        private void UpdateApiUI()
        {
            pnlApiSettings.IsEnabled = chkUseApiMode.IsChecked == true;
            UpdateBuiltInCredentialsUI();
        }

        private void OnBuiltInCredentialsChanged(object sender, RoutedEventArgs e)
        {
            UpdateBuiltInCredentialsUI();
        }

        private void UpdateBuiltInCredentialsUI()
        {
            if (pnlCustomCredentials != null)
            {
                pnlCustomCredentials.IsEnabled = chkUseBuiltInCredentials.IsChecked != true;
            }
        }

        private bool ValidateApiUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证 API URL（如果启用了 API 模式且使用自定义凭证）
                if (chkUseApiMode.IsChecked == true && chkUseBuiltInCredentials.IsChecked != true)
                {
                    var apiUrl = txtApiUrl.Text.Trim();
                    if (!ValidateApiUrl(apiUrl))
                    {
                        txtApiUrlError.Text = "请输入有效的 HTTP/HTTPS URL";
                        txtApiUrlError.Visibility = Visibility.Visible;
                        return;
                    }
                    txtApiUrlError.Visibility = Visibility.Collapsed;
                }

                // 保存代理设置
                _settings.Proxy.UseVPetLLMProxy = chkUseVPetLLMProxy.IsChecked == true;
                _settings.Proxy.EnableCustomProxy = chkEnableCustomProxy.IsChecked == true;
                _settings.Proxy.UseSystemProxy = chkUseSystemProxy.IsChecked == true;
                
                // 保存代理协议
                var selectedProtocol = (cmbProtocol.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(selectedProtocol))
                {
                    _settings.Proxy.Protocol = selectedProtocol.ToLower();
                }
                
                _settings.Proxy.Address = txtProxyAddress.Text.Trim();

                // 保存 API 设置
                _settings.Api.UseApiMode = chkUseApiMode.IsChecked == true;
                _settings.Api.UseBuiltInCredentials = chkUseBuiltInCredentials.IsChecked == true;
                _settings.Api.ApiUrl = txtApiUrl.Text.Trim();
                _settings.Api.BearerToken = txtBearerToken.Text.Trim();
                _settings.Api.EnableFallback = chkEnableFallback.IsChecked == true;

                // 保存搜索设置
                if (int.TryParse(txtMaxResults.Text, out int maxResults))
                {
                    _settings.MaxSearchResults = Math.Max(1, Math.Min(20, maxResults));
                }

                if (int.TryParse(txtMaxContentLength.Text, out int maxLength))
                {
                    _settings.MaxContentLength = Math.Max(1000, Math.Min(50000, maxLength));
                }

                // 保存到文件
                _settings.Save();

                // 通知保存成功
                _onSaved?.Invoke(_settings);

                var modeInfo = _settings.Api.UseApiMode ? "API 模式" : "本地模式";
                MessageBox.Show($"设置已保存！\n\n当前模式：{modeInfo}", 
                    "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置时出错：{ex.Message}", 
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

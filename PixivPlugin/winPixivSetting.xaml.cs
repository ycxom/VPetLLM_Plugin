using System.Windows;
using PixivPlugin.Models;

namespace PixivPlugin
{
    public partial class winPixivSetting : Window
    {
        private readonly PixivPlugin _plugin;
        private readonly PluginSettings _settings;

        public winPixivSetting(PixivPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            _settings = plugin.GetSettings();
            
            LoadSettings();
        }

        private void LoadSettings()
        {
            // 图片反向代理设置
            chkUseImageProxy.IsChecked = _settings.UseImageProxy;
            txtImageProxyUrl.Text = _settings.ImageProxyUrlTemplate ?? "https://pixiv.shojo.cn/{pid}";
            
            // 网络代理设置
            chkUseProxy.IsChecked = _settings.UseProxy;
            txtProxyUrl.Text = _settings.ProxyUrl ?? "";
            
            // 设置代理模式
            if (_settings.FollowVPetLLMProxy)
            {
                rbFollowVPetLLM.IsChecked = true;
            }
            else
            {
                rbCustomProxy.IsChecked = true;
            }
            
            UpdateControlStates();
        }

        private void UpdateControlStates()
        {
            // 图片反向代理控件状态
            var useImageProxy = chkUseImageProxy.IsChecked == true;
            pnlImageProxy.IsEnabled = useImageProxy;
            txtImageProxyUrl.IsEnabled = useImageProxy;
            
            // 网络代理控件状态
            var useProxy = chkUseProxy.IsChecked == true;
            var useCustomProxy = rbCustomProxy.IsChecked == true;
            
            rbFollowVPetLLM.IsEnabled = useProxy;
            rbCustomProxy.IsEnabled = useProxy;
            pnlCustomProxy.IsEnabled = useProxy && useCustomProxy;
            txtProxyUrl.IsEnabled = useProxy && useCustomProxy;
        }

        private void chkUseImageProxy_Changed(object sender, RoutedEventArgs e)
        {
            UpdateControlStates();
        }

        private void chkUseProxy_Changed(object sender, RoutedEventArgs e)
        {
            UpdateControlStates();
        }

        private void ProxyMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateControlStates();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // 验证图片反向代理设置
            if (chkUseImageProxy.IsChecked == true && string.IsNullOrWhiteSpace(txtImageProxyUrl.Text))
            {
                MessageBox.Show("启用图片反向代理时，URL 模板不能为空", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtImageProxyUrl.Focus();
                return;
            }
            
            // 保存图片反向代理设置
            _settings.UseImageProxy = chkUseImageProxy.IsChecked == true;
            _settings.ImageProxyUrlTemplate = string.IsNullOrWhiteSpace(txtImageProxyUrl.Text) 
                ? "https://pixiv.shojo.cn/{pid}" 
                : txtImageProxyUrl.Text.Trim();
            
            // 保存网络代理设置
            _settings.UseProxy = chkUseProxy.IsChecked == true;
            _settings.FollowVPetLLMProxy = rbFollowVPetLLM.IsChecked == true;
            _settings.ProxyUrl = string.IsNullOrWhiteSpace(txtProxyUrl.Text) ? null : txtProxyUrl.Text.Trim();
            
            _plugin.UpdateSettings(_settings);
            
            MessageBox.Show("设置已保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

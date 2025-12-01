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
            var useProxy = chkUseProxy.IsChecked == true;
            var useCustomProxy = rbCustomProxy.IsChecked == true;
            
            rbFollowVPetLLM.IsEnabled = useProxy;
            rbCustomProxy.IsEnabled = useProxy;
            pnlCustomProxy.IsEnabled = useProxy && useCustomProxy;
            txtProxyUrl.IsEnabled = useProxy && useCustomProxy;
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

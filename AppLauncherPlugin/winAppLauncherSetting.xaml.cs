using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AppLauncherPlugin
{
    public partial class winAppLauncherSetting : Window
    {
        private AppLauncherPlugin _plugin;
        private ObservableCollection<AppLauncherPlugin.CustomApp> _customApps;

        public winAppLauncherSetting(AppLauncherPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            _customApps = new ObservableCollection<AppLauncherPlugin.CustomApp>();
            
            LoadSettings();
            dgCustomApps.ItemsSource = _customApps;
            dgCustomApps.SelectionChanged += DgCustomApps_SelectionChanged;
        }

        private void LoadSettings()
        {
            var setting = _plugin.GetSetting();
            
            chkEnableStartMenu.IsChecked = setting.EnableStartMenuScan;
            chkEnableSystemApps.IsChecked = setting.EnableSystemApps;
            chkLogLaunches.IsChecked = setting.LogLaunches;
            
            _customApps.Clear();
            foreach (var app in setting.CustomApps)
            {
                _customApps.Add(new AppLauncherPlugin.CustomApp
                {
                    Name = app.Name,
                    Path = app.Path,
                    Arguments = app.Arguments
                });
            }
        }

        private void DgCustomApps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgCustomApps.SelectedItem is AppLauncherPlugin.CustomApp selectedApp)
            {
                txtAppName.Text = selectedApp.Name;
                txtAppPath.Text = selectedApp.Path;
                txtAppArgs.Text = selectedApp.Arguments;
            }
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择应用程序"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                txtAppPath.Text = openFileDialog.FileName;
                
                // 如果应用名称为空，自动填入文件名（不含扩展名）
                if (string.IsNullOrWhiteSpace(txtAppName.Text))
                {
                    txtAppName.Text = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                }
            }
        }

        private void btnAddApp_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                var newApp = new AppLauncherPlugin.CustomApp
                {
                    Name = txtAppName.Text.Trim(),
                    Path = txtAppPath.Text.Trim(),
                    Arguments = txtAppArgs.Text.Trim()
                };

                // 检查是否已存在同名应用
                if (_customApps.Any(app => app.Name.Equals(newApp.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("已存在同名的应用程序！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _customApps.Add(newApp);
                ClearInputFields();
                MessageBox.Show("应用程序添加成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnUpdateApp_Click(object sender, RoutedEventArgs e)
        {
            if (dgCustomApps.SelectedItem is AppLauncherPlugin.CustomApp selectedApp && ValidateInput())
            {
                selectedApp.Name = txtAppName.Text.Trim();
                selectedApp.Path = txtAppPath.Text.Trim();
                selectedApp.Arguments = txtAppArgs.Text.Trim();
                
                dgCustomApps.Items.Refresh();
                MessageBox.Show("应用程序更新成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("请先选择要更新的应用程序！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnDeleteApp_Click(object sender, RoutedEventArgs e)
        {
            if (dgCustomApps.SelectedItem is AppLauncherPlugin.CustomApp selectedApp)
            {
                var result = MessageBox.Show($"确定要删除应用程序 '{selectedApp.Name}' 吗？", 
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    _customApps.Remove(selectedApp);
                    ClearInputFields();
                    MessageBox.Show("应用程序删除成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("请先选择要删除的应用程序！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnTestApp_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                try
                {
                    var testApp = new AppLauncherPlugin.CustomApp
                    {
                        Name = txtAppName.Text.Trim(),
                        Path = txtAppPath.Text.Trim(),
                        Arguments = txtAppArgs.Text.Trim()
                    };

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = testApp.Path,
                        Arguments = testApp.Arguments,
                        UseShellExecute = true
                    };

                    System.Diagnostics.Process.Start(startInfo);
                    MessageBox.Show("应用程序启动成功！", "测试成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"启动应用程序失败：{ex.Message}", "测试失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnRefreshApps_Click(object sender, RoutedEventArgs e)
        {
            _plugin.RefreshApps();
            MessageBox.Show("应用列表刷新完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnViewApps_Click(object sender, RoutedEventArgs e)
        {
            var availableApps = _plugin.GetAvailableApps();
            var appsWindow = new winAvailableApps(availableApps);
            appsWindow.ShowDialog();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            var setting = _plugin.GetSetting();
            
            setting.EnableStartMenuScan = chkEnableStartMenu.IsChecked ?? true;
            setting.EnableSystemApps = chkEnableSystemApps.IsChecked ?? true;
            setting.LogLaunches = chkLogLaunches.IsChecked ?? true;
            
            setting.CustomApps.Clear();
            setting.CustomApps.AddRange(_customApps);
            
            _plugin.SaveSetting();
            _plugin.RefreshApps();
            
            MessageBox.Show("设置保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtAppName.Text))
            {
                MessageBox.Show("请输入应用程序名称！", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtAppName.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtAppPath.Text))
            {
                MessageBox.Show("请输入应用程序路径！", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtAppPath.Focus();
                return false;
            }

            return true;
        }

        private void ClearInputFields()
        {
            txtAppName.Clear();
            txtAppPath.Clear();
            txtAppArgs.Clear();
        }
    }
}
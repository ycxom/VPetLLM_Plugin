using System.Collections.Generic;
using System.Windows;

namespace AppLauncherPlugin
{
    public partial class winAvailableApps : Window
    {
        public winAvailableApps(List<string> availableApps)
        {
            InitializeComponent();
            lstAvailableApps.ItemsSource = availableApps;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
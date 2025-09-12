using System.Windows;

namespace VPetLLM
{
    public partial class winForegroundAppSetting : Window
    {
        private readonly ForegroundAppPlugin _plugin;

        public winForegroundAppSetting(ForegroundAppPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            TextBox_JitterDelay.Text = _plugin.GetJitterDelay().ToString();
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TextBox_JitterDelay.Text, out int delay))
            {
                _plugin.SetJitterDelay(delay);
                Close();
            }
            else
            {
                MessageBox.Show("Please enter a valid number for the delay.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
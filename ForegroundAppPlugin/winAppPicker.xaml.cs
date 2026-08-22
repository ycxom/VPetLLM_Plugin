using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ForegroundAppPlugin
{
    /// <summary>列表项：包一层是为了给复选框一个可绑定的 IsChecked，并按运行状态染色。</summary>
    internal sealed class AppPickerItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsRunning { get; set; }
        public string TagText { get; set; } = string.Empty;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public Brush TagBackground => IsRunning
            ? new SolidColorBrush(Color.FromRgb(0xDF, 0xF6, 0xDD))
            : new SolidColorBrush(Color.FromRgb(0xF3, 0xF2, 0xF1));

        public Brush TagForeground => IsRunning
            ? new SolidColorBrush(Color.FromRgb(0x0B, 0x65, 0x13))
            : new SolidColorBrush(Color.FromRgb(0x60, 0x5E, 0x5C));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class winAppPicker : Window
    {
        private readonly string _language;
        private readonly List<AppPickerItem> _allItems;
        private readonly ObservableCollection<AppPickerItem> _visibleItems = new ObservableCollection<AppPickerItem>();

        /// <summary>用户点「添加所选」后要加进名单的进程名。取消则为空。</summary>
        internal List<string> SelectedProcessNames { get; private set; } = new List<string>();

        internal winAppPicker(string language, IEnumerable<string> alreadyListed)
        {
            InitializeComponent();
            _language = language;

            var listed = new HashSet<string>(
                (alreadyListed ?? Enumerable.Empty<string>()).Select(AppFilter.NormalizeProcessName),
                StringComparer.OrdinalIgnoreCase);

            _allItems = AppInventory.GetApps()
                // 已经在名单里的就不再列出来，免得重复添加
                .Where(x => !listed.Contains(AppFilter.NormalizeProcessName(x.ProcessName)))
                .Select(x => new AppPickerItem
                {
                    ProcessName = x.ProcessName,
                    DisplayName = x.DisplayName,
                    IsRunning = x.IsRunning,
                    TagText = Lang.T(language, x.IsRunning ? "picker_running" : "picker_installed")
                })
                .ToList();

            ApplyLocalization();
            ListBox_Apps.ItemsSource = _visibleItems;
            ApplyFilter(string.Empty);
        }

        private void ApplyLocalization()
        {
            Title = Lang.T(_language, "picker_title");
            TextBlock_Hint.Text = Lang.T(_language, "picker_hint");
            TextBlock_Empty.Text = Lang.T(_language, "picker_empty");
            Button_Add.Content = Lang.T(_language, "picker_add");
            Button_Cancel.Content = Lang.T(_language, "picker_cancel");
            TextBox_Search.Tag = Lang.T(_language, "picker_search");
        }

        private void TextBox_Search_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ApplyFilter(TextBox_Search.Text);

        private void ApplyFilter(string query)
        {
            // 勾选状态挂在 item 上，不受筛选影响：搜出来勾几个、换个词再勾几个，最后一起加。
            IEnumerable<AppPickerItem> matches = _allItems;
            if (!string.IsNullOrWhiteSpace(query))
            {
                var needle = query.Trim();
                matches = _allItems.Where(x =>
                    x.ProcessName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.DisplayName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _visibleItems.Clear();
            foreach (var item in matches) _visibleItems.Add(item);

            bool empty = _visibleItems.Count == 0;
            TextBlock_Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Button_Add_Click(object sender, RoutedEventArgs e)
        {
            SelectedProcessNames = _allItems
                .Where(x => x.IsChecked)
                .Select(x => x.ProcessName)
                .ToList();

            // 一个都没勾时，把当前高亮的行当作选择——避免"点了添加却什么也没发生"
            if (SelectedProcessNames.Count == 0)
            {
                SelectedProcessNames = ListBox_Apps.SelectedItems
                    .OfType<AppPickerItem>()
                    .Select(x => x.ProcessName)
                    .ToList();
            }

            DialogResult = SelectedProcessNames.Count > 0;
            Close();
        }

        private void Button_Cancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedProcessNames = new List<string>();
            DialogResult = false;
            Close();
        }
    }
}

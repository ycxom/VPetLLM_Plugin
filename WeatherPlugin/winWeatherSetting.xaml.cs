using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WeatherPlugin.Data;
using WeatherPlugin.Models;
using WeatherPlugin.Search;

namespace WeatherPlugin
{
    /// <summary>
    /// winWeatherSetting.xaml 的交互逻辑
    /// </summary>
    public partial class winWeatherSetting : Window
    {
        private readonly WeatherPlugin _plugin;
        private CityVectorSearch? _citySearch;
        private CityDatabase? _cityDatabase;
        private WeatherSettings _settings;

        public winWeatherSetting(WeatherPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            _settings = plugin.GetSettings();

            _cityDatabase = plugin.GetCityDatabase();
            if (_cityDatabase is not null)
            {
                _citySearch = new CityVectorSearch(_cityDatabase);

                // 加载常用城市到下拉框
                var commonCities = new[]
                {
                    "北京市", "上海市", "广州市", "深圳市", "天津市", "重庆市",
                    "杭州市", "南京市", "武汉市", "成都市", "西安市", "苏州市"
                };

                foreach (var city in commonCities)
                {
                    ComboBox_City.Items.Add(city);
                }
            }

            LoadSettings();
        }

        private void LoadSettings()
        {
            ComboBox_City.Text = _settings.DefaultCity;
            TextBox_Timeout.Text = _settings.TimeoutSeconds.ToString();
        }

        private void Button_Search_Click(object sender, RoutedEventArgs e)
        {
            var searchText = ComboBox_City.Text.Trim();
            if (string.IsNullOrEmpty(searchText) || _citySearch is null)
                return;

            var results = _citySearch.FindTopMatches(searchText, 10);
            
            ListBox_SearchResults.Items.Clear();
            foreach (var (city, adcode, score) in results)
            {
                ListBox_SearchResults.Items.Add(new SearchResultItem
                {
                    CityName = city,
                    Adcode = adcode,
                    Score = score,
                    DisplayText = $"{city} (代码: {adcode}, 匹配度: {score:P0})"
                });
            }

            Panel_SearchResults.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ListBox_SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListBox_SearchResults.SelectedItem is SearchResultItem item)
            {
                ComboBox_City.Text = item.CityName;
                _settings.DefaultCity = item.CityName;
                _settings.DefaultAdcode = item.Adcode;
            }
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            // 验证并保存设置
            if (!int.TryParse(TextBox_Timeout.Text, out var timeout) || timeout <= 0)
            {
                MessageBox.Show("请输入有效的超时时间（正整数）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cityName = ComboBox_City.Text.Trim();
            if (string.IsNullOrEmpty(cityName))
            {
                MessageBox.Show("请输入或选择默认城市", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 查找城市 Adcode
            if (_citySearch is not null)
            {
                var adcode = _citySearch.FindBestMatch(cityName);
                if (adcode.HasValue)
                {
                    _settings.DefaultCity = cityName;
                    _settings.DefaultAdcode = adcode.Value;
                }
                else
                {
                    MessageBox.Show($"未找到城市 '{cityName}'，请检查城市名称", "城市未找到", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _settings.TimeoutSeconds = timeout;

            _plugin.UpdateSettings(_settings);

            MessageBox.Show("设置已保存", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void Button_Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private class SearchResultItem
        {
            public string CityName { get; set; } = string.Empty;
            public int Adcode { get; set; }
            public double Score { get; set; }
            public string DisplayText { get; set; } = string.Empty;

            public override string ToString() => DisplayText;
        }
    }
}

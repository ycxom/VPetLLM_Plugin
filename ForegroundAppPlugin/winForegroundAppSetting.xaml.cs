using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ForegroundAppPlugin
{
    public partial class winForegroundAppSetting : UserControl
    {
        private readonly ForegroundAppPlugin _plugin;
        private readonly string _language;
        private bool _isLoading = true;

        public winForegroundAppSetting(ForegroundAppPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            _language = plugin.CurrentLanguage;

            ApplyLocalization();
            LoadSettingsToUI();
            RefreshPreview();
            _isLoading = false;
        }

        private void ApplyLocalization()
        {
            TextBlock_Title.Text = L("panel_title");

            TextBlock_SectionApp.Text = L("section_app");
            TextBlock_LabelJitter.Text = L("lbl_jitter");
            TextBlock_HintJitter.Text = L("hint_jitter");

            TextBlock_SectionMedia.Text = L("section_media");
            CheckBox_EnableMedia.Content = L("chk_media_enable");
            TextBlock_HintMedia.Text = L("hint_media_enable");
            TextBlock_LabelMediaInterval.Text = L("lbl_media_interval");
            CheckBox_MediaInDynamicInfo.Content = L("chk_media_dynamic");
            CheckBox_NotifyOnTrackChange.Content = L("chk_media_notify");
            TextBlock_HintNotify.Text = L("hint_media_notify");

            TextBlock_SectionPrivacy.Text = L("section_privacy");
            TextBlock_LabelKeywords.Text = L("lbl_keywords");
            TextBlock_HintKeywords.Text = L("hint_keywords");
            CheckBox_KeywordsMedia.Content = L("chk_keywords_media");
            TextBlock_LabelFilterMode.Text = L("lbl_filter_mode");
            TextBlock_LabelFilterList.Text = L("lbl_filter_list");
            TextBlock_HintFilterList.Text = L("hint_filter_list");
            TextBlock_WhitelistWarning.Text = L("hint_whitelist_empty");
            Button_BrowseApps.Content = L("btn_browse_apps");

            TextBlock_SectionPreview.Text = L("section_preview");
            Button_Refresh.Content = L("btn_refresh");

            Button_Save.Content = L("btn_save");

            // 顺序必须和 AppFilterMode 的取值一致：Off=0 / Whitelist=1 / Blacklist=2
            ComboBox_FilterMode.Items.Clear();
            ComboBox_FilterMode.Items.Add(L("filter_mode_off"));
            ComboBox_FilterMode.Items.Add(L("filter_mode_whitelist"));
            ComboBox_FilterMode.Items.Add(L("filter_mode_blacklist"));
        }

        private void LoadSettingsToUI()
        {
            var setting = _plugin.CurrentSetting;
            TextBox_JitterDelay.Text = setting.JitterDelay.ToString();
            TextBox_MediaPollInterval.Text = setting.MediaPollInterval.ToString();
            CheckBox_EnableMedia.IsChecked = setting.EnableMedia;
            CheckBox_MediaInDynamicInfo.IsChecked = setting.MediaInDynamicInfo;
            CheckBox_NotifyOnTrackChange.IsChecked = setting.NotifyOnTrackChange;

            TextBox_Keywords.Text = AppFilter.FormatList(setting.PrivacyKeywords);
            CheckBox_KeywordsMedia.IsChecked = setting.ApplyKeywordsToMedia;
            TextBox_FilterList.Text = AppFilter.FormatList(setting.FilterList);

            var mode = setting.FilterMode;
            ComboBox_FilterMode.SelectedIndex = mode >= 0 && mode <= 2 ? mode : 0;

            UpdateMediaOptionsState();
            UpdateFilterListState();
        }

        private void CheckBox_EnableMedia_Toggled(object sender, RoutedEventArgs e) => UpdateMediaOptionsState();

        private void UpdateMediaOptionsState()
        {
            if (Panel_MediaOptions == null) return;
            Panel_MediaOptions.IsEnabled = CheckBox_EnableMedia.IsChecked == true;
        }

        private void ComboBox_FilterMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            UpdateFilterListState();
        }

        private void TextBox_FilterList_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading) return;
            UpdateFilterListState();
        }

        /// <summary>名单区只在选了黑/白名单时可用；白名单为空要显式警告——那等于全拦。</summary>
        private void UpdateFilterListState()
        {
            if (Panel_FilterList == null || ComboBox_FilterMode == null) return;

            var mode = (AppFilterMode)Math.Max(0, ComboBox_FilterMode.SelectedIndex);
            Panel_FilterList.IsEnabled = mode != AppFilterMode.Off;

            bool warn = mode == AppFilterMode.Whitelist
                        && AppFilter.ParseList(TextBox_FilterList.Text).Count == 0;
            TextBlock_WhitelistWarning.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Button_BrowseApps_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var existing = AppFilter.ParseList(TextBox_FilterList.Text);
                var picker = new winAppPicker(_language, existing)
                {
                    Owner = Window.GetWindow(this)
                };

                if (picker.ShowDialog() != true) return;

                var merged = existing
                    .Concat(picker.SelectedProcessNames)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .GroupBy(AppFilter.NormalizeProcessName)
                    .Select(g => g.First())
                    .ToList();

                TextBox_FilterList.Text = AppFilter.FormatList(merged);
                UpdateFilterListState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, L("err_invalid_number_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Button_Refresh_Click(object sender, RoutedEventArgs e) => RefreshPreview();

        private void RefreshPreview()
        {
            TextBlock_PreviewApp.Text = _plugin.GetForegroundAppPreview();
            TextBlock_PreviewMedia.Text = _plugin.GetMediaPreview();
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TextBox_JitterDelay.Text, out int jitterDelay) || jitterDelay < 1)
            {
                ShowError("err_invalid_jitter");
                TextBox_JitterDelay.Focus();
                return;
            }

            if (!int.TryParse(TextBox_MediaPollInterval.Text, out int mediaInterval) || mediaInterval < 1)
            {
                ShowError("err_invalid_media_interval");
                TextBox_MediaPollInterval.Focus();
                return;
            }

            _plugin.ApplySetting(new ForegroundAppPlugin.Setting
            {
                JitterDelay = jitterDelay,
                MediaPollInterval = mediaInterval,
                EnableMedia = CheckBox_EnableMedia.IsChecked == true,
                MediaInDynamicInfo = CheckBox_MediaInDynamicInfo.IsChecked == true,
                NotifyOnTrackChange = CheckBox_NotifyOnTrackChange.IsChecked == true,
                PrivacyKeywords = AppFilter.ParseList(TextBox_Keywords.Text),
                ApplyKeywordsToMedia = CheckBox_KeywordsMedia.IsChecked == true,
                FilterMode = Math.Max(0, ComboBox_FilterMode.SelectedIndex),
                FilterList = AppFilter.ParseList(TextBox_FilterList.Text)
            });

            CloseOwnerWindow();
        }

        private void ShowError(string key)
        {
            MessageBox.Show(L(key), L("err_invalid_number_title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private string L(string key) => Lang.T(_language, key);
    }
}

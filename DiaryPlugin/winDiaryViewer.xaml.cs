using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DiaryPlugin
{
    public partial class winDiaryViewer : UserControl
    {
        private readonly DiaryPlugin _plugin;

        /// <summary>列表项视图模型。</summary>
        public class DiaryItem
        {
            public string Date { get; set; } = "";
            public string Content { get; set; } = "";
            public string Preview => Content.Length > 40 ? Content.Substring(0, 40) : Content;
        }

        public winDiaryViewer(DiaryPlugin plugin)
        {
            _plugin = plugin;
            InitializeComponent();
            LoadAll();
            // 打开时后台补齐历史日记向量（embedding 上线后无需等下次启动）
            _ = BackfillAsync();
        }

        private async System.Threading.Tasks.Task BackfillAsync()
        {
            try
            {
                var n = await _plugin.BackfillVectorsAsync();
                if (n > 0) StatusText.Text = $"已为 {n} 篇历史日记补齐向量";
            }
            catch { /* 静默：补齐失败不影响查看 */ }
        }

        private void LoadAll()
        {
            var entries = _plugin.GetAllEntries();
            BindList(entries);
            StatusText.Text = $"共 {entries.Count} 篇日记";
        }

        private void BindList(List<DiaryEntry> entries)
        {
            DateList.ItemsSource = entries
                .Select(e => new DiaryItem { Date = e.Date, Content = e.Content })
                .ToList();

            if (DateList.Items.Count > 0)
                DateList.SelectedIndex = 0;
            else
            {
                ContentDate.Text = "";
                ContentBody.Text = "还没有日记。与我多聊聊，我每天都会记录下来～";
            }
        }

        private void DateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DateList.SelectedItem is DiaryItem item)
            {
                ContentDate.Text = item.Date;
                ContentBody.Text = item.Content;
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e) => await DoSearch();

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await DoSearch();
        }

        private async System.Threading.Tasks.Task DoSearch()
        {
            var query = SearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(query)) { LoadAll(); return; }

            StatusText.Text = "搜索中…";
            try
            {
                var vec = await _plugin.EmbedAsync(query);
                var hits = _plugin.SearchEntries(vec, query, 20);
                BindList(hits);
                StatusText.Text = vec is null
                    ? $"关键词命中 {hits.Count} 篇"
                    : $"向量检索命中 {hits.Count} 篇";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"搜索失败: {ex.Message}";
            }
        }

        private void ShowAll_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            LoadAll();
        }

        private async void WriteToday_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "正在生成今天的日记…";
            try
            {
                await _plugin.ForceWriteTodayAsync();
                LoadAll();
                StatusText.Text = "已尝试生成今天的日记（若今天没有互动则不会生成）";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"生成失败: {ex.Message}";
            }
        }

        private void DeleteOne_Click(object sender, RoutedEventArgs e)
        {
            if (DateList.SelectedItem is not DiaryItem item)
            {
                StatusText.Text = "请先在左侧选中一篇日记";
                return;
            }
            var r = MessageBox.Show($"确定删除 {item.Date} 这篇日记吗？此操作不可撤销。", "删除本篇",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes)
            {
                _plugin.DeleteEntry(item.Date);
                LoadAll();
                StatusText.Text = $"已删除 {item.Date}";
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show("确定要删除全部日记吗？此操作不可撤销。", "清除全部日记",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes)
            {
                _plugin.ClearAllEntries();
                LoadAll();
                StatusText.Text = "已清除全部日记";
            }
        }
    }
}

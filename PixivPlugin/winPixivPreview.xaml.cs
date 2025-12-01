using System.IO;
using System.Windows;
using Microsoft.Win32;
using PixivPlugin.Models;
using PixivPlugin.Services;

namespace PixivPlugin
{
    public partial class winPixivPreview : Window
    {
        private readonly ImageLoader _imageLoader;
        private readonly List<PixivIllust> _illusts;
        private int _currentIndex;
        private int _currentPageIndex; // 多页图片的当前页
        private bool _isDownloading;
        private string? _downloadPath;

        public winPixivPreview(List<PixivIllust> illusts, ImageLoader imageLoader, string? downloadPath = null)
        {
            InitializeComponent();
            _illusts = illusts;
            _imageLoader = imageLoader;
            _currentIndex = 0;
            _currentPageIndex = 0;
            _downloadPath = downloadPath;

            if (_illusts.Count > 0)
            {
                LoadCurrentImage();
            }
            else
            {
                ShowError("没有可显示的图片");
            }
        }

        /// <summary>
        /// 单张图片构造函数（用于随机推荐）
        /// </summary>
        public winPixivPreview(PixivIllust illust, ImageLoader imageLoader, string? downloadPath = null)
            : this(new List<PixivIllust> { illust }, imageLoader, downloadPath)
        {
        }

        private async void LoadCurrentImage()
        {
            if (_currentIndex < 0 || _currentIndex >= _illusts.Count)
                return;

            var illust = _illusts[_currentIndex];
            
            // 更新信息
            txtTitle.Text = illust.Title;
            txtAuthor.Text = $"作者: {illust.User.Name}";
            txtTags.Text = string.Join(", ", illust.Tags.Take(8).Select(t => t.Name));
            
            UpdatePageInfo();
            UpdateNavigationButtons();

            // 加载图片
            ShowLoading();
            var thumbnailUrl = illust.GetThumbnailUrl(_currentPageIndex);
            var image = await _imageLoader.LoadImageAsync(thumbnailUrl);
            
            if (image != null)
            {
                imgPreview.Source = image;
                HideLoading();
            }
            else
            {
                ShowError("图片加载失败");
            }
        }

        private void UpdatePageInfo()
        {
            var illust = _illusts[_currentIndex];
            
            if (_illusts.Count == 1 && illust.PageCount <= 1)
            {
                // 单张图片，不显示页码
                txtPageInfo.Text = "";
            }
            else if (_illusts.Count == 1 && illust.PageCount > 1)
            {
                // 单个作品多页
                txtPageInfo.Text = $"第 {_currentPageIndex + 1} / {illust.PageCount} 页";
            }
            else if (illust.PageCount > 1)
            {
                // 多个作品，当前作品多页
                txtPageInfo.Text = $"{_currentIndex + 1}/{_illusts.Count} (第 {_currentPageIndex + 1}/{illust.PageCount} 页)";
            }
            else
            {
                // 多个作品，当前作品单页
                txtPageInfo.Text = $"{_currentIndex + 1} / {_illusts.Count}";
            }
        }

        private void UpdateNavigationButtons()
        {
            var illust = _illusts[_currentIndex];
            
            // 上一张按钮：第一张图片的第一页时禁用
            btnPrevious.IsEnabled = !(_currentIndex == 0 && _currentPageIndex == 0);
            
            // 下一张按钮：最后一张图片的最后一页时禁用
            var isLastImage = _currentIndex == _illusts.Count - 1;
            var isLastPage = _currentPageIndex >= illust.PageCount - 1;
            btnNext.IsEnabled = !(isLastImage && isLastPage);
        }

        private void btnPrevious_Click(object sender, RoutedEventArgs e)
        {
            var illust = _illusts[_currentIndex];
            
            if (_currentPageIndex > 0)
            {
                // 当前作品的上一页
                _currentPageIndex--;
            }
            else if (_currentIndex > 0)
            {
                // 上一个作品的最后一页
                _currentIndex--;
                var prevIllust = _illusts[_currentIndex];
                _currentPageIndex = Math.Max(0, prevIllust.PageCount - 1);
            }
            
            LoadCurrentImage();
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            var illust = _illusts[_currentIndex];
            
            if (_currentPageIndex < illust.PageCount - 1)
            {
                // 当前作品的下一页
                _currentPageIndex++;
            }
            else if (_currentIndex < _illusts.Count - 1)
            {
                // 下一个作品的第一页
                _currentIndex++;
                _currentPageIndex = 0;
            }
            
            LoadCurrentImage();
        }

        private async void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
                return;

            var illust = _illusts[_currentIndex];
            var originalUrl = illust.GetOriginalUrl(_currentPageIndex);
            
            if (string.IsNullOrEmpty(originalUrl))
            {
                MessageBox.Show("无法获取原图地址", "下载失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 获取保存路径
            var defaultFileName = ImageLoader.GetDefaultFileName(originalUrl);
            var savePath = GetSavePath(defaultFileName);
            
            if (string.IsNullOrEmpty(savePath))
                return;

            _isDownloading = true;
            btnDownload.IsEnabled = false;
            progressDownload.Visibility = Visibility.Visible;
            progressDownload.Value = 0;
            txtDownloadStatus.Text = "下载中...";

            var progress = new Progress<int>(percent =>
            {
                progressDownload.Value = percent;
                txtDownloadStatus.Text = $"下载中... {percent}%";
            });

            var success = await _imageLoader.DownloadImageAsync(originalUrl, savePath, progress);

            _isDownloading = false;
            btnDownload.IsEnabled = true;
            progressDownload.Visibility = Visibility.Collapsed;

            if (success)
            {
                txtDownloadStatus.Text = $"已保存: {Path.GetFileName(savePath)}";
            }
            else
            {
                txtDownloadStatus.Text = "下载失败，请重试";
                MessageBox.Show("下载失败，请检查网络连接或代理设置", "下载失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string? GetSavePath(string defaultFileName)
        {
            if (!string.IsNullOrEmpty(_downloadPath) && Directory.Exists(_downloadPath))
            {
                return Path.Combine(_downloadPath, defaultFileName);
            }

            var dialog = new SaveFileDialog
            {
                FileName = defaultFileName,
                Filter = "图片文件|*.jpg;*.png;*.gif|所有文件|*.*",
                Title = "保存图片"
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null;
        }

        private void ShowLoading()
        {
            txtLoading.Visibility = Visibility.Visible;
            txtError.Visibility = Visibility.Collapsed;
            imgPreview.Source = null;
        }

        private void HideLoading()
        {
            txtLoading.Visibility = Visibility.Collapsed;
            txtError.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            txtLoading.Visibility = Visibility.Collapsed;
            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
        }
    }
}

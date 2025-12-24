using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;
using WpfAnimatedGif;

namespace StickerPlugin.Services
{
    /// <summary>
    /// 图片显示管理器
    /// 通过反射加载 VPet.Plugin.Imgae.dll 并控制图片显示
    /// 支持静态图片和 GIF 动画
    /// </summary>
    public class ImageDisplayManager : IDisposable
    {
        private readonly IMainWindow _mainWindow;
        private readonly string _dllPath;

        private Assembly? _imagePluginAssembly;
        private object? _imageUI;
        private Image? _imageControl;
        private DispatcherTimer? _hideTimer;

        public bool IsInitialized { get; private set; }
        public string? LastError { get; private set; }

        public ImageDisplayManager(IMainWindow mainWindow, string dllPath)
        {
            _mainWindow = mainWindow;
            _dllPath = dllPath;
        }

        /// <summary>
        /// 初始化图片显示管理器
        /// </summary>
        public bool Initialize()
        {
            try
            {
                // 检查 DLL 路径是否有效
                if (string.IsNullOrEmpty(_dllPath))
                {
                    LastError = "DLL path is empty. Please subscribe to the Steam Workshop plugin.";
                    return false;
                }

                // 检查 DLL 是否存在
                if (!File.Exists(_dllPath))
                {
                    LastError = $"VPet.Plugin.Imgae.dll not found at: {_dllPath}";
                    return false;
                }

                // 加载程序集
                _imagePluginAssembly = Assembly.LoadFrom(_dllPath);
                if (_imagePluginAssembly == null)
                {
                    LastError = "Failed to load VPet.Plugin.Imgae.dll";
                    return false;
                }

                // 获取 ImageUI 类型
                var imageUIType = _imagePluginAssembly.GetType("VPet.Plugin.Imgae.ImageUI");
                if (imageUIType == null)
                {
                    LastError = "ImageUI type not found in VPet.Plugin.Imgae.dll";
                    return false;
                }

                // 在 UI 线程上创建 ImageUI 实例
                Exception? uiException = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // 创建 ImageUI 实例（使用无参构造函数）
                        _imageUI = Activator.CreateInstance(imageUIType);
                        if (_imageUI == null)
                        {
                            LastError = "Failed to create ImageUI instance";
                            return;
                        }

                        // 获取 Image 控件（assembly/internal 访问修饰符，需要 NonPublic）
                        var imageField = imageUIType.GetField("Image", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (imageField != null)
                        {
                            _imageControl = imageField.GetValue(_imageUI) as Image;
                        }
                        
                        // 如果字段获取失败，尝试通过 LogicalTreeHelper 查找
                        if (_imageControl == null && _imageUI is UserControl uc)
                        {
                            _imageControl = FindVisualChild<Image>(uc);
                        }

                        if (_imageControl == null)
                        {
                            LastError = "Failed to get Image control from ImageUI";
                            return;
                        }

                        // 将 ImageUI 添加到 UIGrid
                        if (_imageUI is UserControl userControl && _mainWindow?.Main?.UIGrid != null)
                        {
                            var uiGrid = _mainWindow.Main.UIGrid;
                            var childCount = uiGrid.Children.Count;
                            uiGrid.Children.Insert(childCount > 0 ? childCount - 1 : 0, userControl);

                            // 初始隐藏
                            userControl.Visibility = Visibility.Collapsed;
                        }
                    }
                    catch (Exception ex)
                    {
                        uiException = ex;
                    }
                });

                if (uiException != null)
                {
                    LastError = $"UI initialization failed: {uiException.Message}";
                    return false;
                }

                if (_imageUI == null || _imageControl == null)
                {
                    if (string.IsNullOrEmpty(LastError))
                        LastError = "ImageUI or ImageControl is null after initialization";
                    return false;
                }

                // 初始化隐藏定时器（必须在 UI 线程上创建）
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _hideTimer = new DispatcherTimer();
                    _hideTimer.Tick += HideTimer_Tick;
                });

                IsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Failed to initialize ImageDisplayManager: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 在可视化树中查找指定类型的子元素
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                
                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }

        /// <summary>
        /// 从 Base64 数据显示图片（支持 GIF 动画）
        /// </summary>
        public async Task ShowImageFromBase64Async(string base64Data, int durationSeconds)
        {
            if (!IsInitialized || _imageUI == null || _imageControl == null)
            {
                LastError = "ImageDisplayManager not initialized";
                return;
            }

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // 解析 Base64 数据
                        var base64 = base64Data;
                        var isGif = false;

                        // 检测是否为 GIF（通过 data URI 前缀或文件头）
                        if (base64.StartsWith("data:image/gif"))
                        {
                            isGif = true;
                        }

                        // 移除 data:image/xxx;base64, 前缀
                        if (base64.Contains(","))
                        {
                            base64 = base64.Substring(base64.IndexOf(",") + 1);
                        }

                        // 转换为字节数组
                        var imageBytes = Convert.FromBase64String(base64);

                        // 检测 GIF 文件头 (47 49 46 38 = "GIF8")
                        if (!isGif && imageBytes.Length > 4)
                        {
                            isGif = imageBytes[0] == 0x47 && imageBytes[1] == 0x49 &&
                                    imageBytes[2] == 0x46 && imageBytes[3] == 0x38;
                        }

                        // 先清除之前的图片/动画
                        ImageBehavior.SetAnimatedSource(_imageControl, null);
                        _imageControl.Source = null;

                        if (isGif)
                        {
                            // 使用 WpfAnimatedGif 显示 GIF 动画
                            // 注意：不能使用 using，因为 WpfAnimatedGif 需要保持 stream
                            var stream = new MemoryStream(imageBytes);
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.StreamSource = stream;
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.EndInit();
                            image.Freeze();

                            // 设置动画图片
                            ImageBehavior.SetAnimatedSource(_imageControl, image);
                            ImageBehavior.SetRepeatBehavior(_imageControl, System.Windows.Media.Animation.RepeatBehavior.Forever);
                        }
                        else
                        {
                            // 静态图片
                            var stream = new MemoryStream(imageBytes);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = stream;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            _imageControl.Source = bitmap;
                        }

                        // 显示 ImageUI
                        if (_imageUI is UserControl userControl)
                        {
                            userControl.Visibility = Visibility.Visible;
                        }

                        // 设置隐藏定时器
                        if (_hideTimer != null)
                        {
                            _hideTimer.Stop();
                            _hideTimer.Interval = TimeSpan.FromSeconds(durationSeconds);
                            _hideTimer.Start();
                        }
                    }
                    catch (Exception innerEx)
                    {
                        LastError = $"Failed to display image: {innerEx.Message}";
                    }
                });
            }
            catch (Exception ex)
            {
                LastError = $"Failed to show image from Base64: {ex.Message}";
            }
        }

        /// <summary>
        /// 显示图片（从 URL）
        /// </summary>
        public async Task ShowImageAsync(string imagePath, int durationSeconds)
        {
            if (!IsInitialized || _imageUI == null)
            {
                return;
            }

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // 加载图片
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // 设置图片源
                    if (_imageControl != null)
                    {
                        _imageControl.Source = bitmap;
                    }

                    // 显示 ImageUI
                    if (_imageUI is UserControl userControl)
                    {
                        userControl.Visibility = Visibility.Visible;
                    }

                    // 设置隐藏定时器
                    if (_hideTimer != null)
                    {
                        _hideTimer.Stop();
                        _hideTimer.Interval = TimeSpan.FromSeconds(durationSeconds);
                        _hideTimer.Start();
                    }
                });
            }
            catch (Exception ex)
            {
                LastError = $"Failed to show image: {ex.Message}";
            }
        }

        /// <summary>
        /// 隐藏图片
        /// </summary>
        public void HideImage()
        {
            if (_imageUI == null)
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                // 停止 GIF 动画并清除
                if (_imageControl != null)
                {
                    ImageBehavior.SetAnimatedSource(_imageControl, null);
                    _imageControl.Source = null;
                }

                if (_imageUI is UserControl userControl)
                {
                    userControl.Visibility = Visibility.Collapsed;
                }

                _hideTimer?.Stop();
            });
        }

        private void HideTimer_Tick(object? sender, EventArgs e)
        {
            // 停止定时器防止重复触发
            _hideTimer?.Stop();
            HideImage();
        }

        public void Dispose()
        {
            _hideTimer?.Stop();
            HideImage();

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_imageUI is UserControl userControl)
                {
                    var uiGrid = _mainWindow.Main.UIGrid;
                    if (uiGrid.Children.Contains(userControl))
                    {
                        uiGrid.Children.Remove(userControl);
                    }
                }
            });
        }
    }
}

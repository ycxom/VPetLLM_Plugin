using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Interface;

namespace StickerPlugin.Services
{
    /// <summary>
    /// Image 插件协调器（StickerPlugin 侧）
    /// 通过反射与 VPet.Plugin.Image 插件通信
    /// </summary>
    public class ImagePluginCoordinator : IDisposable
    {
        private const string ImagePluginName = "LLM表情包";
        private const string CoordinatorPropertyName = "ImageCoordinator";

        private readonly IMainWindow _mainWindow;
        private readonly Action<string> _log;
        private object? _imagePluginCoordinator;
        private Type? _coordinatorType;
        private bool _initialized;

        public ImagePluginCoordinator(IMainWindow mainWindow, Action<string> log)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// 初始化协调器，查找 Image 插件
        /// </summary>
        public bool Initialize()
        {
            try
            {
                _log($"开始查找 {ImagePluginName} 插件...");
                _log($"当前已加载插件数量: {_mainWindow.Plugins?.Count ?? 0}");

                // 查找 Image 插件
                var imagePlugin = _mainWindow.Plugins?.FirstOrDefault(p => p.PluginName == ImagePluginName);

                if (imagePlugin == null)
                {
                    _log($"未找到 {ImagePluginName} 插件");
                    _log("可用插件列表:");
                    if (_mainWindow.Plugins != null)
                    {
                        foreach (var plugin in _mainWindow.Plugins)
                        {
                            _log($"  - {plugin.PluginName}");
                        }
                    }
                    return false;
                }

                _log($"找到 {ImagePluginName} 插件: {imagePlugin.GetType().FullName}");

                // 获取 ImageCoordinator 属性
                var coordinatorProperty = imagePlugin.GetType().GetProperty(
                    CoordinatorPropertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (coordinatorProperty == null)
                {
                    _log($"错误：{ImagePluginName} 插件没有 {CoordinatorPropertyName} 属性");
                    return false;
                }

                _imagePluginCoordinator = coordinatorProperty.GetValue(imagePlugin);

                if (_imagePluginCoordinator == null)
                {
                    _log($"错误：{CoordinatorPropertyName} 属性为 null");
                    return false;
                }

                _coordinatorType = _imagePluginCoordinator.GetType();
                _log($"成功获取协调器: {_coordinatorType.FullName}");

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                _log($"初始化 ImagePluginCoordinator 失败: {ex.Message}");
                _log($"堆栈跟踪: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 检查是否可以使用独占模式
        /// </summary>
        public bool CanUseExclusiveMode()
        {
            if (!_initialized || _imagePluginCoordinator == null || _coordinatorType == null)
            {
                _log("CanUseExclusiveMode: 协调器未初始化或已释放");
                return false;
            }

            try
            {
                var method = _coordinatorType.GetMethod("CanUseExclusiveMode");
                if (method == null)
                {
                    _log("错误：找不到 CanUseExclusiveMode 方法");
                    return false;
                }

                var result = method.Invoke(_imagePluginCoordinator, null);
                return result is bool canUse && canUse;
            }
            catch (Exception ex)
            {
                _log($"调用 CanUseExclusiveMode 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 启动独占会话
        /// </summary>
        public async Task<string?> StartExclusiveSessionAsync()
        {
            if (!_initialized || _imagePluginCoordinator == null || _coordinatorType == null)
            {
                _log("错误：协调器未初始化");
                return null;
            }

            try
            {
                var method = _coordinatorType.GetMethod("StartExclusiveSessionAsync");
                if (method == null)
                {
                    _log("错误：找不到 StartExclusiveSessionAsync 方法");
                    return null;
                }

                var task = method.Invoke(_imagePluginCoordinator, new object[] { "StickerPlugin" }) as Task<string>;
                if (task == null)
                {
                    _log("错误：StartExclusiveSessionAsync 返回 null");
                    return null;
                }

                return await task;
            }
            catch (Exception ex)
            {
                _log($"启动独占会话失败: {ex.Message}");
                _log($"堆栈跟踪: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 结束独占会话
        /// </summary>
        public async Task EndExclusiveSessionAsync(string? sessionId = null)
        {
            if (!_initialized || _imagePluginCoordinator == null || _coordinatorType == null)
            {
                _log("错误：协调器未初始化");
                return;
            }

            try
            {
                var method = _coordinatorType.GetMethod("EndExclusiveSessionAsync");
                if (method == null)
                {
                    _log("错误：找不到 EndExclusiveSessionAsync 方法");
                    return;
                }

                var task = method.Invoke(_imagePluginCoordinator, new object[] { "StickerPlugin", sessionId ?? "" }) as Task;
                if (task != null)
                {
                    await task;
                }
            }
            catch (Exception ex)
            {
                _log($"结束独占会话失败: {ex.Message}");
                _log($"堆栈跟踪: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 在独占会话中显示表情包
        /// </summary>
        public async Task ShowImageInSessionAsync(string base64Image, int durationSeconds)
        {
            if (!_initialized || _imagePluginCoordinator == null || _coordinatorType == null)
            {
                _log("错误：协调器未初始化");
                return;
            }

            try
            {
                // 获取当前会话 ID
                var getSessionIdMethod = _coordinatorType.GetMethod("IsSessionActive");
                if (getSessionIdMethod == null)
                {
                    _log("错误：找不到 IsSessionActive 方法");
                    return;
                }

                var isActive = getSessionIdMethod.Invoke(_imagePluginCoordinator, null);
                if (isActive is not bool active || !active)
                {
                    _log("错误：没有活跃的会话");
                    return;
                }

                // 获取会话 ID（通过反射获取私有字段）
                var sessionManagerField = _coordinatorType.GetField("_sessionManager", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sessionManagerField == null)
                {
                    _log("错误：找不到 _sessionManager 字段");
                    return;
                }

                var sessionManager = sessionManagerField.GetValue(_imagePluginCoordinator);
                if (sessionManager == null)
                {
                    _log("错误：_sessionManager 为 null");
                    return;
                }

                var getCurrentSessionIdMethod = sessionManager.GetType().GetMethod("GetCurrentSessionId");
                if (getCurrentSessionIdMethod == null)
                {
                    _log("错误：找不到 GetCurrentSessionId 方法");
                    return;
                }

                var sessionId = getCurrentSessionIdMethod.Invoke(sessionManager, null) as string;
                if (string.IsNullOrEmpty(sessionId))
                {
                    _log("错误：会话 ID 为空");
                    return;
                }

                // 调用显示方法
                var method = _coordinatorType.GetMethod("ShowImageInSessionAsync");
                if (method == null)
                {
                    _log("错误：找不到 ShowImageInSessionAsync 方法");
                    return;
                }

                var task = method.Invoke(_imagePluginCoordinator, new object[] { sessionId, base64Image, durationSeconds }) as Task<string>;
                if (task != null)
                {
                    await task;
                }
            }
            catch (Exception ex)
            {
                _log($"显示表情包失败: {ex.Message}");
                _log($"堆栈跟踪: {ex.StackTrace}");
            }
        }

        public void Dispose()
        {
            _imagePluginCoordinator = null;
            _coordinatorType = null;
            _initialized = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using VPet_Simulator.Windows.Interface;

namespace StickerPlugin.Services
{
    /// <summary>
    /// 表情包能力的可用状态。面板据此决定引导用户做哪一步。
    /// </summary>
    public enum ImagePluginStatus
    {
        /// <summary>没装「LLM表情包」MOD</summary>
        NotInstalled,

        /// <summary>装了，但版本太旧，缺少本插件需要的公开入口</summary>
        Incompatible,

        /// <summary>装了也兼容，但 MOD 里的「在线网络表情包库」开关没打开</summary>
        OnlineLibraryDisabled,

        /// <summary>可用</summary>
        Ready
    }

    /// <summary>
    /// 「LLM表情包」MOD 的调用桥。
    ///
    /// 表情包的搜索、下载、缓存、GIF 播放、显示时长、服务地址与凭证全部由 MOD 自己实现，
    /// 本插件不再持有任何一份副本：只负责在 MOD 已加载的实例上唤起它的公开入口，
    /// 并把 MOD 内部的参数（在线库开关、标签数量）读出来供 VPetLLM 侧使用。
    ///
    /// 这里是本插件唯一的反射面，且只触碰 public 成员 —— 不加载程序集、不读私有字段。
    /// 之所以走反射而不是直接引用 MOD 程序集：MOD 是可选前置，由 VPet 从创意工坊目录
    /// 单独加载；硬引用会让本插件在 MOD 缺失时直接类型加载失败，也会因加载上下文不同
    /// 而拿到两份互不相同的类型。
    /// </summary>
    public sealed class ImagePluginBridge
    {
        public const string ImagePluginName = "LLM表情包";
        public const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3657291049";

        private const string CallerId = "StickerPlugin";

        private readonly IMainWindow _mainWindow;
        private readonly Action<string> _log;

        private MainPlugin? _imagePlugin;

        // ImageMgr 上的公开入口
        private MethodInfo? _showStickerByEmotion;      // ShowOnlineStickerByEmotionAsync(string, List<string>) -> Task<bool>
        private MethodInfo? _getStickerManager;         // GetOnlineStickerManager() -> OnlineStickerManager
        private MethodInfo? _testConnection;            // TestOnlineStickerConnectionAsync() -> Task<bool>
        private PropertyInfo? _settingsProperty;        // Settings -> ImageSettings
        private PropertyInfo? _coordinatorProperty;     // ImageCoordinator -> IImagePluginCoordinator

        // OnlineStickerManager 上的公开入口，首次用到时解析
        private MethodInfo? _getAvailableTags;          // GetAvailableTagsAsync() -> Task<List<string>>

        // IImagePluginCoordinator 上的公开入口，首次用到时解析
        private MethodInfo? _canUseExclusiveMode;
        private MethodInfo? _startExclusiveSession;
        private MethodInfo? _endExclusiveSession;

        public ImagePluginBridge(IMainWindow mainWindow, Action<string> log)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>MOD 是否已找到（不代表在线库已启用）</summary>
        public bool IsPluginFound => _imagePlugin is not null;

        /// <summary>
        /// 定位 MOD 并解析入口。VPet 加载插件的顺序不保证，所以允许反复调用重试。
        /// </summary>
        public bool Resolve()
        {
            if (_imagePlugin is not null && _showStickerByEmotion is not null)
                return true;

            var plugin = _mainWindow.Plugins?.FirstOrDefault(p => p.PluginName == ImagePluginName);
            if (plugin is null)
            {
                _imagePlugin = null;
                return false;
            }

            _imagePlugin = plugin;
            var type = plugin.GetType();

            _showStickerByEmotion = type.GetMethod("ShowOnlineStickerByEmotionAsync",
                new[] { typeof(string), typeof(List<string>) });
            _getStickerManager = type.GetMethod("GetOnlineStickerManager", Type.EmptyTypes);
            _testConnection = type.GetMethod("TestOnlineStickerConnectionAsync", Type.EmptyTypes);
            _settingsProperty = type.GetProperty("Settings");
            _coordinatorProperty = type.GetProperty("ImageCoordinator");

            if (_showStickerByEmotion is null)
            {
                _log($"「{ImagePluginName}」版本过旧：缺少 ShowOnlineStickerByEmotionAsync 入口，请更新该 MOD");
                return false;
            }

            _log($"已连接「{ImagePluginName}」({type.FullName})");
            return true;
        }

        /// <summary>
        /// 当前状态。每次读取都重新判定，因为用户可能在 MOD 设置里随时改开关。
        /// </summary>
        public ImagePluginStatus GetStatus()
        {
            if (!Resolve())
                return _imagePlugin is null ? ImagePluginStatus.NotInstalled : ImagePluginStatus.Incompatible;

            return IsOnlineLibraryEnabled ? ImagePluginStatus.Ready : ImagePluginStatus.OnlineLibraryDisabled;
        }

        #region MOD 内部参数

        /// <summary>MOD 设置里的「在线网络表情包库」总开关</summary>
        public bool IsOnlineLibraryEnabled => ReadOnlineSetting("IsEnabled", false);

        /// <summary>MOD 设置里的「释放标签数量」，决定往系统提示词里塞多少个标签</summary>
        public int TagCount => Math.Max(1, ReadOnlineSetting("TagCount", 10));

        /// <summary>MOD 设置里的「显示时长（秒）」，仅供面板展示</summary>
        public int DisplayDurationSeconds => ReadOnlineSetting("DisplayDurationSeconds", 6);

        private T ReadOnlineSetting<T>(string name, T fallback)
        {
            try
            {
                var settings = _settingsProperty?.GetValue(_imagePlugin);
                var online = settings?.GetType().GetProperty("OnlineSticker")?.GetValue(settings);
                var value = online?.GetType().GetProperty(name)?.GetValue(online);
                return value is T typed ? typed : fallback;
            }
            catch (Exception ex)
            {
                _log($"读取 MOD 参数 {name} 失败: {ex.Message}");
                return fallback;
            }
        }

        #endregion

        #region 功能入口

        /// <summary>
        /// 让 MOD 按标签搜索并显示一张表情包。
        /// 搜索、下载、缓存、显示与自动隐藏全在 MOD 内部完成。
        /// </summary>
        public async Task<bool> ShowStickerAsync(string tags)
        {
            if (!Resolve() || _showStickerByEmotion is null)
                return false;

            var parts = SplitTags(tags);
            if (parts.Count == 0)
                return false;

            var emotion = parts[0];
            var additional = parts.Skip(1).ToList();

            try
            {
                var task = (Task<bool>?)_showStickerByEmotion.Invoke(_imagePlugin, new object[] { emotion, additional });
                if (task is null)
                    return false;

                var shown = await task;
                if (!shown)
                    _log($"未显示表情包（标签: {tags}）—— 可能是在线库未启用或无匹配结果");
                return shown;
            }
            catch (Exception ex)
            {
                _log($"显示表情包失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>模型给的标签串可能用中英文逗号或分号分隔</summary>
        private static List<string> SplitTags(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
                return new List<string>();

            var separators = new[] { ',', ';', '，', '；', '、' };
            return tags.Split(separators)
                       .Select(t => t.Trim())
                       .Where(t => t.Length > 0)
                       .ToList();
        }

        /// <summary>
        /// 取 MOD 侧的可用标签全集（MOD 自带缓存与离线兜底）。
        /// </summary>
        public async Task<List<string>> GetAvailableTagsAsync()
        {
            if (!Resolve() || _getStickerManager is null)
                return new List<string>();

            try
            {
                var manager = _getStickerManager.Invoke(_imagePlugin, null);
                if (manager is null)
                    return new List<string>();

                _getAvailableTags ??= manager.GetType().GetMethod("GetAvailableTagsAsync", Type.EmptyTypes);
                if (_getAvailableTags is null)
                    return new List<string>();

                var task = (Task<List<string>>?)_getAvailableTags.Invoke(manager, null);
                return task is null ? new List<string>() : await task;
            }
            catch (Exception ex)
            {
                _log($"获取标签失败: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>连通性测试，直接用 MOD 自己的凭证与服务地址</summary>
        public async Task<bool> TestConnectionAsync()
        {
            if (!Resolve() || _testConnection is null)
                return false;

            try
            {
                var task = (Task<bool>?)_testConnection.Invoke(_imagePlugin, null);
                return task is not null && await task;
            }
            catch (Exception ex)
            {
                _log($"测试连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 唤起 MOD 自己的设置窗口（服务地址、凭证、时长、标签数都在那里）。
        /// 只要 MOD 对象在就能开 —— 版本过旧、入口对不上时更需要让用户进去看一眼。
        /// </summary>
        public bool OpenModSettings()
        {
            Resolve();

            var plugin = _imagePlugin;
            var dispatcher = Application.Current?.Dispatcher;
            if (plugin is null || dispatcher is null)
                return false;

            dispatcher.Invoke(() =>
            {
                try
                {
                    plugin.Setting();
                }
                catch (Exception ex)
                {
                    _log($"打开「{ImagePluginName}」设置失败: {ex.Message}");
                }
            });
            return true;
        }

        #endregion

        #region 独占会话

        /// <summary>
        /// 会话期间 MOD 会挂起自己的气泡触发，避免它的情感表情和本插件推的表情包互相抢显示位。
        /// </summary>
        public bool CanUseExclusiveMode()
        {
            var coordinator = GetCoordinator();
            if (coordinator is null)
                return false;

            try
            {
                _canUseExclusiveMode ??= coordinator.GetType().GetMethod("CanUseExclusiveMode", Type.EmptyTypes);
                return _canUseExclusiveMode?.Invoke(coordinator, null) is bool ok && ok;
            }
            catch (Exception ex)
            {
                _log($"检查独占模式失败: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> StartExclusiveSessionAsync()
        {
            var coordinator = GetCoordinator();
            if (coordinator is null)
                return null;

            try
            {
                _startExclusiveSession ??= coordinator.GetType()
                    .GetMethod("StartExclusiveSessionAsync", new[] { typeof(string) });
                var task = (Task<string>?)_startExclusiveSession?.Invoke(coordinator, new object[] { CallerId });
                return task is null ? null : await task;
            }
            catch (Exception ex)
            {
                _log($"启动独占会话失败: {ex.Message}");
                return null;
            }
        }

        public async Task EndExclusiveSessionAsync(string sessionId)
        {
            var coordinator = GetCoordinator();
            if (coordinator is null)
                return;

            try
            {
                _endExclusiveSession ??= coordinator.GetType()
                    .GetMethod("EndExclusiveSessionAsync", new[] { typeof(string), typeof(string) });
                if (_endExclusiveSession?.Invoke(coordinator, new object[] { CallerId, sessionId }) is Task task)
                    await task;
            }
            catch (Exception ex)
            {
                _log($"结束独占会话失败: {ex.Message}");
            }
        }

        private object? GetCoordinator()
        {
            if (!Resolve())
                return null;

            try
            {
                return _coordinatorProperty?.GetValue(_imagePlugin);
            }
            catch (Exception ex)
            {
                _log($"获取协调器失败: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}

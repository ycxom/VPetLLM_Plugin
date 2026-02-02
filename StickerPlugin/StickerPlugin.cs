using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using StickerPlugin.Models;
using StickerPlugin.Services;

namespace StickerPlugin
{
    /// <summary>
    /// VPetLLM 表情包插件
    /// 支持 AI 搜索并发送表情包
    /// 依赖 VPet.Plugin.Image 插件显示表情包
    /// </summary>
    public class StickerPlugin : IActionPlugin, IPluginWithData, IDynamicInfoPlugin, IProcessingLifecyclePlugin
    {
        private const string ImagePluginName = "LLM表情包";
        private const string ImagePluginWorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3657291049";
        
        public string Name => "Sticker";
        public string Author => "ycxom";

        public string Description
        {
            get
            {
                if (_vpetLLM is null)
                    return "搜索并发送表情包。";

                return _vpetLLM.Settings.Language switch
                {
                    "ja" => "スタンプを検索して送信します。",
                    "zh-hant" => "搜尋並發送表情包。",
                    "en" => "Search and send stickers.",
                    _ => "搜索并发送表情包。"
                };
            }
        }

        public string Parameters => "action(string: send/setting), tags(string, required for send)";

        public string Examples => @"发送表情包: `<|plugin_Sticker_begin|> action(send), tags(可爱, 开心) <|plugin_Sticker_end|>`
打开设置: `<|plugin_Sticker_begin|> action(setting) <|plugin_Sticker_end|>`";

        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = string.Empty;
        public string PluginDataDir { get; set; } = string.Empty;

        private VPetLLM.VPetLLM? _vpetLLM;
        private PluginSettings _settings = new();
        private ImageVectorService? _imageVectorService;
        private ImagePluginCoordinator? _imagePluginCoordinator; // Image插件协调器
        private bool _coordinatorInitAttempted = false; // 是否已尝试初始化协调器
        private Random _random = new();
        private ulong _steamId = 0;

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            // 注意：不要覆盖 FilePath，PluginManager 已经正确设置了 DLL 文件路径

            // 加载设置
            LoadSettings();

            // 获取 Steam ID
            try
            {
                _steamId = plugin.MW?.SteamID ?? 0;
            }
            catch
            {
                _steamId = 0;
            }

            // 初始化 API 服务
            _imageVectorService = new ImageVectorService(
                _settings.GetEffectiveServiceUrl(), 
                _settings.GetEffectiveApiKey(), 
                Log, 
                _steamId,
                async () => await (plugin.MW?.GenerateAuthKey() ?? Task.FromResult(0)),
                _settings.UseBuiltInCredentials
            );

            // 注意：不在这里初始化 ImagePluginCoordinator
            // 因为此时其他插件可能还未加载完成
            // 将在第一次使用时延迟初始化
            Log("StickerPlugin 初始化完成，ImagePluginCoordinator 将在首次使用时初始化");

            // 预加载标签
            _ = Task.Run(async () =>
            {
                try
                {
                    var cacheDuration = TimeSpan.FromMinutes(_settings.CacheDurationMinutes);
                    await _imageVectorService.GetCachedTagsAsync(cacheDuration);
                }
                catch
                {
                    // 静默失败
                }
            });
        }

        public async Task<string> Function(string arguments)
        {
            try
            {
                // 解析 action 参数
                var actionMatch = Regex.Match(arguments, @"action\((\w+)\)");
                var action = actionMatch.Success ? actionMatch.Groups[1].Value.ToLower() : "send";

                // 打开设置窗口
                if (action == "setting")
                {
                    OpenSettingsWindow();
                    return string.Empty;
                }

                // 发送表情包
                if (action == "send")
                {
                    var tagsMatch = Regex.Match(arguments, @"tags\(([^)]+)\)");
                    var tagsStr = tagsMatch.Success ? tagsMatch.Groups[1].Value.Trim() : "";

                if (string.IsNullOrWhiteSpace(tagsStr))
                    return string.Empty;

                    // 使用生命周期管理的会话
                    await SearchAndShowStickerAsync(tagsStr, GetOrInitializeCoordinator(), _lifecycleSessionId);
                    return string.Empty;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Log($"Sticker Plugin Error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 获取或初始化 ImagePluginCoordinator（延迟初始化）
        /// </summary>
        private ImagePluginCoordinator? GetOrInitializeCoordinator()
        {
            // 如果已经有有效的协调器，检查它是否仍然可用
            if (_imagePluginCoordinator != null)
            {
                // 简单检查：尝试调用 CanUseExclusiveMode
                try
                {
                    // 如果协调器仍然有效，直接返回
                    _imagePluginCoordinator.CanUseExclusiveMode();
                    return _imagePluginCoordinator;
                }
                catch
                {
                    // 协调器已失效，需要重新初始化
                    Log("检测到协调器已失效，尝试重新初始化...");
                    _imagePluginCoordinator = null;
                    _coordinatorInitAttempted = false; // 重置标志，允许重新初始化
                }
            }

            // 如果之前尝试过但失败了，不再重试（避免重复错误提示）
            if (_coordinatorInitAttempted)
            {
                return null;
            }

            _coordinatorInitAttempted = true;
            
            if (_vpetLLM?.MW == null)
            {
                Log("错误：MainWindow 未初始化");
                return null;
            }

            Log("开始初始化 ImagePluginCoordinator...");
            _imagePluginCoordinator = new ImagePluginCoordinator(_vpetLLM.MW, Log);
            
            var initResult = _imagePluginCoordinator.Initialize();
            if (initResult)
            {
                Log("ImagePluginCoordinator 初始化成功");
            }
            else
            {
                Log($"警告：未找到 {ImagePluginName} 插件");
                Log($"请从 Steam 创意工坊订阅: {ImagePluginWorkshopUrl}");
                _imagePluginCoordinator = null;
            }
            
            return _imagePluginCoordinator;
        }

        /// <summary>
        /// 搜索并显示表情包
        /// </summary>
        private async Task SearchAndShowStickerAsync(string tags, ImagePluginCoordinator? coordinator, string? sessionId)
        {
            if (_imageVectorService is null)
                return;

            // 检查协调器和会话
            if (coordinator == null || string.IsNullOrEmpty(sessionId))
            {
                Log($"错误：独占会话未启动");
                return;
            }

            try
            {
                // 搜索表情包
                var response = await _imageVectorService.SearchAsync(tags, limit: 1);

                if (response is null || !response.Success || response.Results is null || response.Results.Count == 0)
                {
                    Log("未找到匹配的表情包");
                    return;
                }

                // 选择最高分的结果
                var bestResult = response.Results.OrderByDescending(r => r.Score).First();
                Log($"找到表情包，相似度: {bestResult.Score:F2}");

                // 显示表情包（通过 Image 插件）
                if (!string.IsNullOrEmpty(bestResult.Base64))
                {
                    Log("显示表情包");
                    await coordinator.ShowImageInSessionAsync(
                        bestResult.Base64,
                        _settings.DisplayDurationSeconds
                    );
                    Log("表情包显示完成");
                }
            }
            catch (Exception ex)
            {
                Log($"Sticker error: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取要添加到系统提示词的标签信息
        /// </summary>
        public async Task<string> GetSystemPromptAdditionAsync()
        {
            if (_imageVectorService is null)
            {
                return string.Empty;
            }

            try
            {
                var cacheDuration = TimeSpan.FromMinutes(_settings.CacheDurationMinutes);
                var allTags = await _imageVectorService.GetCachedTagsAsync(cacheDuration);

                if (allTags.Count == 0)
                {
                    return string.Empty;
                }

                // 验证设置
                _settings.Validate(allTags.Count);

                // 随机选择标签
                var selectedTags = SelectRandomTags(allTags, _settings.TagCount);

                if (selectedTags.Count == 0)
                {
                    return string.Empty;
                }

                // 格式化提示词
                var tagsStr = string.Join(", ", selectedTags);
                return $@"
[表情包功能]
你可以使用表情包插件发送表情包来增强对话表现力。
可用标签: {tagsStr}
使用方法: `<|plugin_Sticker_begin|> action(send), tags(标签1, 标签2) <|plugin_Sticker_end|>`
提示: 组合多个标签可以更精准地匹配表情包。
";
            }
            catch (Exception ex)
            {
                Log($"Failed to get system prompt addition: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 随机选择指定数量的标签
        /// </summary>
        private List<string> SelectRandomTags(List<string> allTags, int count)
        {
            if (allTags.Count <= count)
            {
                return new List<string>(allTags);
            }

            var selected = new HashSet<string>();
            var shuffled = allTags.OrderBy(_ => _random.Next()).ToList();

            foreach (var tag in shuffled)
            {
                if (selected.Count >= count)
                    break;
                selected.Add(tag);
            }

            return selected.ToList();
        }

        #region IDynamicInfoPlugin

        /// <summary>
        /// 获取动态信息（同步版本，用于 IDynamicInfoPlugin 接口）
        /// </summary>
        public string GetDynamicInfo()
        {
            try
            {
                // 使用 Task.Run 避免死锁
                var task = Task.Run(async () => await GetSystemPromptAdditionAsync());
                return task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log($"GetDynamicInfo failed: {ex.Message}");
                return string.Empty;
            }
        }

        #endregion

        private void OpenSettingsWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var window = new winStickerSetting(this);
                window.Show();
            });
        }

        /// <summary>
        /// 显示 Image 插件缺失提示
        /// </summary>
        private void ShowImagePluginMissingPrompt()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var result = MessageBox.Show(
                    $"表情包显示功能需要安装「{ImagePluginName}」插件。\n\n" +
                    $"请从 Steam 创意工坊订阅后重启 VPet。\n\n" +
                    $"点击「是」复制订阅链接到剪贴板。\n\n" +
                    $"链接: {ImagePluginWorkshopUrl}",
                    "缺少前置插件",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Clipboard.SetText(ImagePluginWorkshopUrl);
                        MessageBox.Show("链接已复制到剪贴板！", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch
                    {
                        // 忽略剪贴板错误
                    }
                }
            });
        }

        public void Unload()
        {
            SaveSettings();
            _imageVectorService?.Dispose();
            _imagePluginCoordinator?.Dispose();
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }

        #region Settings Management

        public void LoadSettings()
        {
            _settings = PluginSettings.Load(PluginDataDir);
        }

        public void SaveSettings()
        {
            _settings.Save(PluginDataDir);
        }

        public PluginSettings GetSettings() => _settings;

        public void UpdateSettings(PluginSettings settings)
        {
            _settings = settings;
            _settings.Validate();
            SaveSettings();

            // 重新初始化服务
            _imageVectorService?.Dispose();
            _imageVectorService = new ImageVectorService(
                _settings.GetEffectiveServiceUrl(), 
                _settings.GetEffectiveApiKey(), 
                Log, 
                _steamId,
                async () => await (_vpetLLM?.MW?.GenerateAuthKey() ?? Task.FromResult(0)),
                _settings.UseBuiltInCredentials
            );
        }

        /// <summary>
        /// 测试服务连接
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            if (_imageVectorService is null)
            {
                return false;
            }
            return await _imageVectorService.HealthCheckAsync();
        }

        /// <summary>
        /// 获取 VPet 的 MODPath 列表（已弃用，保留以兼容）
        /// </summary>
        [Obsolete("不再需要 MODPath，StickerPlugin 现在完全依赖 VPet.Plugin.Image")]
        public IEnumerable<System.IO.DirectoryInfo>? GetModPaths()
        {
            return _vpetLLM?.MW?.MODPath;
        }

        #endregion

        #region IProcessingLifecyclePlugin

        private string? _lifecycleSessionId;

        /// <summary>
        /// 在 VPetLLM 开始处理用户输入之前调用
        /// 立即启动独占会话以屏蔽气泡触发
        /// </summary>
        public async Task<object?> OnProcessingStartAsync(string userInput)
        {
            try
            {
                var coordinator = GetOrInitializeCoordinator();
                if (coordinator != null && coordinator.CanUseExclusiveMode())
                {
                    Log("VPetLLM 开始处理，立即启动独占会话以屏蔽气泡触发");
                    _lifecycleSessionId = await coordinator.StartExclusiveSessionAsync();
                    Log($"独占会话已启动，会话 ID: {_lifecycleSessionId}");
                    return _lifecycleSessionId;
                }
            }
            catch (Exception ex)
            {
                Log($"OnProcessingStartAsync 失败: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 在 LLM 开始返回响应时调用
        /// </summary>
        public Task OnResponseStartAsync(object? context)
        {
            // 不需要特殊处理
            return Task.CompletedTask;
        }

        /// <summary>
        /// 在 VPetLLM 完成处理后调用
        /// 结束独占会话
        /// </summary>
        public async Task OnProcessingCompleteAsync(object? context)
        {
            try
            {
                var coordinator = GetOrInitializeCoordinator();
                if (coordinator != null && !string.IsNullOrEmpty(_lifecycleSessionId))
                {
                    await coordinator.EndExclusiveSessionAsync(_lifecycleSessionId);
                    Log("独占会话已结束（生命周期）");
                    _lifecycleSessionId = null;
                }
            }
            catch (Exception ex)
            {
                Log($"OnProcessingCompleteAsync 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在处理过程中发生错误时调用
        /// 确保结束独占会话
        /// </summary>
        public async Task OnProcessingErrorAsync(object? context, Exception exception)
        {
            try
            {
                var coordinator = GetOrInitializeCoordinator();
                if (coordinator != null && !string.IsNullOrEmpty(_lifecycleSessionId))
                {
                    await coordinator.EndExclusiveSessionAsync(_lifecycleSessionId);
                    Log("独占会话已结束（错误处理）");
                    _lifecycleSessionId = null;
                }
            }
            catch (Exception ex)
            {
                Log($"OnProcessingErrorAsync 失败: {ex.Message}");
            }
        }

        #endregion
    }
}


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
    public class StickerPlugin : IActionPlugin, IPluginWithData, IDynamicInfoPlugin
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

            // 初始化 Image 插件协调器（必需）
            _imagePluginCoordinator = new ImagePluginCoordinator(plugin.MW!, Log);
            var coordinatorInitResult = _imagePluginCoordinator.Initialize();
            if (coordinatorInitResult)
            {
                Log("ImagePluginCoordinator 初始化成功");
            }
            else
            {
                Log($"警告：未找到 {ImagePluginName} 插件，表情包功能将无法使用");
                Log($"请从 Steam 创意工坊订阅: {ImagePluginWorkshopUrl}");
            }

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

                    await SearchAndShowStickerAsync(tagsStr);
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
        /// 搜索并显示表情包
        /// </summary>
        private async Task SearchAndShowStickerAsync(string tags)
        {
            if (_imageVectorService is null)
                return;

            // 检查 Image 插件是否可用
            if (_imagePluginCoordinator == null)
            {
                Log($"错误：{ImagePluginName} 插件未初始化");
                ShowImagePluginMissingPrompt();
                return;
            }

            string? sessionId = null;
            bool useExclusiveMode = false;

            try
            {
                // 检查是否可以使用独占模式（Image 插件可用）
                if (_imagePluginCoordinator.CanUseExclusiveMode())
                {
                    Log("启动独占会话");
                    sessionId = await _imagePluginCoordinator.StartExclusiveSessionAsync();
                    useExclusiveMode = true;
                    Log($"独占会话已启动，会话 ID: {sessionId}");
                }
                else
                {
                    Log($"警告：{ImagePluginName} 插件不可用或未启用");
                    ShowImagePluginMissingPrompt();
                    return;
                }

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
                    await _imagePluginCoordinator.ShowImageInSessionAsync(
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
            finally
            {
                // 确保结束独占会话
                if (useExclusiveMode && _imagePluginCoordinator != null && !string.IsNullOrEmpty(sessionId))
                {
                    try
                    {
                        await _imagePluginCoordinator.EndExclusiveSessionAsync();
                        Log("独占会话已结束");
                    }
                    catch (Exception ex)
                    {
                        Log($"结束独占会话失败: {ex.Message}");
                    }
                }
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
    }
}


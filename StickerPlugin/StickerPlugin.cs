using System.Text.RegularExpressions;
using System.Windows;
using VPetLLM.Core;
using StickerPlugin.Models;
using StickerPlugin.Services;

namespace StickerPlugin
{
    /// <summary>
    /// VPetLLM 表情包插件
    /// 支持 AI 搜索并发送表情包
    /// </summary>
    public class StickerPlugin : IActionPlugin, IPluginWithData, IDynamicInfoPlugin
    {
        private const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3027023665";
        
        public string Name => "Sticker";
        public string Author => "ycxom";

        public string Description
        {
            get
            {
                if (_vpetLLM == null)
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
        private ImageDisplayManager? _imageDisplayManager;
        private Random _random = new();
        private bool _dllMissingPromptShown = false;

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            FilePath = plugin.PluginPath;

            // 加载设置
            LoadSettings();

            // 初始化 API 服务
            _imageVectorService = new ImageVectorService(_settings.GetEffectiveServiceUrl(), _settings.GetEffectiveApiKey(), Log);

            // 初始化图片显示管理器
            var dllPath = _settings.GetEffectiveDllPath();
            Log($"Using ImagePlugin DLL: {dllPath}");
            _imageDisplayManager = new ImageDisplayManager(plugin.MW, dllPath);
            var initResult = _imageDisplayManager.Initialize();
            
            if (!initResult)
            {
                Log($"ImageDisplayManager initialization failed: {_imageDisplayManager.LastError}");
            }

            // 预加载标签
            _ = Task.Run(async () =>
            {
                try
                {
                    var cacheDuration = TimeSpan.FromMinutes(_settings.CacheDurationMinutes);
                    await _imageVectorService.GetCachedTagsAsync(cacheDuration);
                }
                catch (Exception ex)
                {
                    Log($"Failed to preload tags: {ex.Message}");
                }
            });

            Log("Sticker Plugin Initialized!");
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
                    {
                        Log("No tags provided for sticker search");
                        return string.Empty;
                    }

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
            if (_imageVectorService == null)
            {
                Log("ImageVectorService not initialized");
                return;
            }

            try
            {
                // 搜索表情包
                var response = await _imageVectorService.SearchAsync(tags, limit: 1);

                if (response == null || !response.Success)
                {
                    Log("Sticker service unavailable");
                    return;
                }

                if (response.Results == null || response.Results.Count == 0)
                {
                    Log($"No sticker found for tags: {tags}");
                    return;
                }

                // 选择最高分的结果
                var bestResult = response.Results.OrderByDescending(r => r.Score).First();

                // 显示表情包
                if (_imageDisplayManager?.IsInitialized == true)
                {
                    // 优先使用 Base64 数据
                    if (!string.IsNullOrEmpty(bestResult.Base64))
                    {
                        Log($"Showing image from Base64: {bestResult.Filename}");
                        await _imageDisplayManager.ShowImageFromBase64Async(
                            bestResult.Base64,
                            _settings.DisplayDurationSeconds
                        );
                    }
                    else
                    {
                        Log($"No Base64 data for: {bestResult.Filename}");
                    }
                }
                else
                {
                    Log($"ImageDisplayManager not initialized: {_imageDisplayManager?.LastError}");
                    // 检测是否是 DLL 未找到的情况，弹出订阅提示
                    ShowDllMissingPrompt();
                }
            }
            catch (Exception ex)
            {
                Log($"Search sticker failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取要添加到系统提示词的标签信息
        /// </summary>
        public async Task<string> GetSystemPromptAdditionAsync()
        {
            if (_imageVectorService == null)
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
        /// 显示 DLL 缺失提示（仅显示一次）
        /// </summary>
        private void ShowDllMissingPrompt()
        {
            if (_dllMissingPromptShown)
                return;

            _dllMissingPromptShown = true;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var result = MessageBox.Show(
                    $"表情包显示功能需要订阅 Steam 创意工坊插件。\n\n" +
                    $"请订阅后重启 VPet 以启用表情包显示功能。\n\n" +
                    $"点击「是」复制订阅链接到剪贴板。\n\n" +
                    $"链接: {WorkshopUrl}",
                    "缺少前置插件",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Clipboard.SetText(WorkshopUrl);
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
            _imageDisplayManager?.Dispose();
            Log("Sticker Plugin Unloaded!");
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
            _imageVectorService = new ImageVectorService(_settings.GetEffectiveServiceUrl(), _settings.GetEffectiveApiKey(), Log);
        }

        /// <summary>
        /// 测试服务连接
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            if (_imageVectorService == null)
            {
                return false;
            }
            return await _imageVectorService.HealthCheckAsync();
        }

        #endregion
    }
}

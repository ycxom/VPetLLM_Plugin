using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using StickerPlugin.Services;

namespace StickerPlugin
{
    /// <summary>
    /// VPetLLM 表情包插件。
    ///
    /// 本插件只做两件事：把「发表情包」这个动作暴露给 LLM，以及在需要时引导用户安装、
    /// 启用前置 MOD「LLM表情包」。表情包库本身（搜索、下载、缓存、显示、凭证、时长配置）
    /// 由该 MOD 完整实现，本插件不再重复一套 —— 只通过 <see cref="ImagePluginBridge"/>
    /// 唤起 MOD 的公开入口和读取它内部的参数。
    /// </summary>
    public partial class StickerPlugin : IPluginTab, IActionPlugin, IDynamicInfoPlugin, IProcessingLifecyclePlugin
    {
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

        private VPetLLM.VPetLLM? _vpetLLM;
        private ImagePluginBridge? _bridge;
        private readonly Random _random = new();

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            // 注意：不要覆盖 FilePath，PluginManager 已经正确设置了 DLL 文件路径

            // 这里不建桥：VPet 的插件加载顺序不保证，此刻「LLM表情包」可能还没加载。
            // 首次使用时再解析，解析失败也允许后续重试。
            Log("StickerPlugin 已初始化，将在首次使用时连接「LLM表情包」MOD");
        }

        /// <summary>
        /// 取调用桥。MOD 未加载时返回的桥仍然可用，只是所有调用都会返回失败状态，
        /// 由面板负责把「去安装 / 去启用」的引导展示给用户。
        /// </summary>
        internal ImagePluginBridge? GetBridge()
        {
            if (_bridge is not null)
                return _bridge;

            if (_vpetLLM?.MW is null)
                return null;

            _bridge = new ImagePluginBridge(_vpetLLM.MW, Log);
            _bridge.Resolve();
            return _bridge;
        }

        public async Task<string> Function(string arguments)
        {
            try
            {
                var actionMatch = Regex.Match(arguments, @"action\((\w+)\)");
                var action = actionMatch.Success ? actionMatch.Groups[1].Value.ToLower() : "send";

                // 打开设置 = 直接唤起 MOD 自己的设置窗口，本插件不再有独立的配置项。
                // MOD 没装/唤不起来时退回本插件的引导面板，那里会告诉用户该装什么、该开哪个开关。
                if (action == "setting")
                {
                    if (GetBridge()?.OpenModSettings() != true)
                        OpenGuidePanel();
                    return string.Empty;
                }

                if (action == "send")
                {
                    var tagsMatch = Regex.Match(arguments, @"tags\(([^)]+)\)");
                    var tags = tagsMatch.Success ? tagsMatch.Groups[1].Value.Trim() : "";

                    if (string.IsNullOrWhiteSpace(tags))
                        return string.Empty;

                    var bridge = GetBridge();
                    if (bridge is null)
                        return string.Empty;

                    await bridge.ShowStickerAsync(tags);
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

        /// <summary>MOD 不可用时，把本插件的状态/引导面板弹成一个独立窗口</summary>
        private void OpenGuidePanel()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                new Window
                {
                    Title = TabTitle,
                    Width = 520,
                    Height = 560,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = CreatePanel()
                }.Show();
            });
        }

        #region IDynamicInfoPlugin

        /// <summary>
        /// 往系统提示词里补一段可用标签。标签全集和「释放多少个」都取自 MOD 内部，
        /// 但用法说明必须由本插件给出 —— MOD 自己那段说的是它的情感分析触发路径，
        /// 和本插件的工具调用语法不是一回事。
        /// </summary>
        public async Task<string> GetSystemPromptAdditionAsync()
        {
            var bridge = GetBridge();
            if (bridge is null || bridge.GetStatus() != ImagePluginStatus.Ready)
                return string.Empty;

            try
            {
                var allTags = await bridge.GetAvailableTagsAsync();
                if (allTags.Count == 0)
                    return string.Empty;

                var selectedTags = SelectRandomTags(allTags, Math.Min(bridge.TagCount, allTags.Count));
                if (selectedTags.Count == 0)
                    return string.Empty;

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

        private List<string> SelectRandomTags(List<string> allTags, int count)
        {
            if (allTags.Count <= count)
                return new List<string>(allTags);

            return allTags.OrderBy(_ => _random.Next()).Take(count).ToList();
        }

        #endregion

        public void Unload()
        {
            _bridge = null;
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }

        #region IProcessingLifecyclePlugin

        private string? _lifecycleSessionId;

        /// <summary>
        /// VPetLLM 开始处理用户输入时立即占住独占会话，让 MOD 在这一轮里停掉自己的
        /// 气泡触发，免得它的情感表情和本插件即将推的表情包互相抢显示位。
        /// </summary>
        public async Task<object?> OnProcessingStartAsync(string userInput)
        {
            try
            {
                var bridge = GetBridge();
                if (bridge is not null && bridge.CanUseExclusiveMode())
                {
                    _lifecycleSessionId = await bridge.StartExclusiveSessionAsync();
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

        public Task OnResponseStartAsync(object? context) => Task.CompletedTask;

        public Task OnProcessingCompleteAsync(object? context) => EndLifecycleSessionAsync("生命周期");

        public Task OnProcessingErrorAsync(object? context, Exception exception) => EndLifecycleSessionAsync("错误处理");

        private async Task EndLifecycleSessionAsync(string reason)
        {
            try
            {
                var sessionId = _lifecycleSessionId;
                if (sessionId is null)
                    return;

                // 先清空再结束：结束过程里抛异常也不会把会话 ID 留成僵尸值，
                // 否则下一轮 CanUseExclusiveMode 会一直判定为「已有活跃会话」而永久失效。
                _lifecycleSessionId = null;

                var bridge = GetBridge();
                if (bridge is not null)
                {
                    await bridge.EndExclusiveSessionAsync(sessionId);
                    Log($"独占会话已结束（{reason}）");
                }
            }
            catch (Exception ex)
            {
                Log($"结束独占会话失败（{reason}）: {ex.Message}");
            }
        }

        #endregion
    }
}

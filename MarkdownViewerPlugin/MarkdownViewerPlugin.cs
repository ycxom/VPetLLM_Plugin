using System;
using System.Threading.Tasks;
using System.Windows;
using VPetLLM.Core;

namespace MarkdownViewerPlugin
{
    // 提供简单动作入口：传入内联 Markdown 文本
    public class MarkdownViewerPlugin : IVPetLLMPlugin, IActionPlugin, VPetLLM.Core.IPluginTakeover
    {
        public string Name => "MarkdownViewer";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM == null) return "在独立窗口中渲染并显示标准 Markdown 文档。请使用标准 Markdown 格式，支持标题、列表、代码块、表格等。";
                switch (_vpetLLM.Settings.Language)
                {
                    case "ja": return "標準 Markdown ドキュメントを独立ウィンドウでレンダリングして表示します。標準 Markdown 形式を使用してください。見出し、リスト、コードブロック、テーブルなどをサポートします。";
                    case "zh-hans": return "在独立窗口中渲染并显示标准 Markdown 文档。请使用标准 Markdown 格式，支持标题、列表、代码块、表格等。";
                    case "zh-hant": return "在獨立視窗中渲染並顯示標準 Markdown 文件。請使用標準 Markdown 格式，支援標題、清單、程式碼區塊、表格等。";
                    case "en":
                    default: return "Render and display standard Markdown documents in a standalone window. Use standard Markdown format. Supports headings, lists, code blocks, tables, etc.";
                }
            }
        }
        public string Parameters => "markdown_content";
        public string Examples => "Example: `<|plugin_MarkdownViewer_begin|> # Python Example\n\n```python\ndef hello():\n    print(\"Hello\")\n```\n\n## Explanation\nThis is a simple function. <|plugin_MarkdownViewer_end|>`";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";

        // IPluginTakeover 接口实现
        public bool SupportsTakeover => true;

        private VPetLLM.VPetLLM? _vpetLLM;
        private System.Text.StringBuilder _takeoverContent = new System.Text.StringBuilder();
        private winMarkdownViewer _takeoverWindow;
        private bool _isTakingOver = false;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private const int UPDATE_INTERVAL_MS = 50; // 更新间隔：50ms（更流畅的实时渲染）
        private System.Threading.Timer _renderTimer;
        private bool _hasPendingUpdate = false;
        private readonly object _updateLock = new object();

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            FilePath = plugin.PluginPath;

            // 确保依赖可解析（temp 解压目录找不到依赖时，从原插件目录回退加载）
            try { DependencyResolver.Ensure(FilePath); } catch { }

            VPetLLM.Utils.Logger.Log("MarkdownViewer Plugin Initialized.");
        }

        public Task<string> Function(string arguments)
        {
            try
            {
                var content = (arguments ?? string.Empty).Trim();
                
                // 移除可能的引号包装
                if (content.StartsWith("\"") && content.EndsWith("\""))
                {
                    content = content.Substring(1, content.Length - 2);
                }

                // 处理 AI 可能生成的转义反引号：\`\`\` -> ```
                content = content.Replace("\\`\\`\\`", "```");
                
                // 处理其他常见的转义序列
                content = content.Replace("\\n", "\n")
                               .Replace("\\t", "\t")
                               .Replace("\\r", "\r");

                // 确保解析器已就绪
                try { DependencyResolver.Ensure(FilePath); } catch { }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var win = new winMarkdownViewer();
                    win.Show();
                    try { win.Activate(); win.Topmost = true; win.Topmost = false; } catch { }

                    if (!string.IsNullOrEmpty(content))
                    {
                        win.RenderMarkdown(content);
                        win.SetTitleFromSource("AI Generated Markdown");
                    }
                    else
                    {
                        win.RenderMarkdown("# Markdown Viewer\n\n请通过插件参数传入标准 Markdown 文本。");
                        win.SetTitleFromSource("Markdown Viewer");
                    }
                });

                return Task.FromResult("Markdown window opened successfully.");
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"MarkdownViewer Function error: {ex.Message}");
                return Task.FromResult($"Failed to open markdown viewer: {ex.Message}");
            }
        }



        public void Unload()
        {
            VPetLLM.Utils.Logger.Log("MarkdownViewer Plugin Unloaded.");
        }

        public void Log(string message)
        {
            if (_vpetLLM == null) return;
            _vpetLLM.Log(message);
        }

        #region IPluginTakeover 实现

        /// <summary>
        /// 开始接管处理
        /// </summary>
        public Task<bool> BeginTakeoverAsync(string initialContent)
        {
            try
            {
                Log("MarkdownViewer: 开始接管模式（流式渲染）");
                _isTakingOver = true;
                _takeoverContent.Clear();
                _takeoverContent.Append(initialContent);
                _hasPendingUpdate = false;

                // 确保依赖可解析
                try { DependencyResolver.Ensure(FilePath); } catch { }

                // 创建窗口
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _takeoverWindow = new winMarkdownViewer();
                    _takeoverWindow.Show();
                    try { _takeoverWindow.Activate(); _takeoverWindow.Topmost = true; _takeoverWindow.Topmost = false; } catch { }
                    _takeoverWindow.SetTitleFromSource("Streaming Markdown ⚡");
                    
                    // 显示初始内容
                    if (!string.IsNullOrEmpty(initialContent))
                    {
                        _takeoverWindow.RenderMarkdown(initialContent);
                    }
                });

                // 启动定时器，用于批量渲染累积的内容
                _renderTimer = new System.Threading.Timer(RenderPendingContent, null, UPDATE_INTERVAL_MS, UPDATE_INTERVAL_MS);

                Log($"MarkdownViewer: 接管开始成功，初始内容长度: {initialContent.Length}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log($"MarkdownViewer: 接管开始失败: {ex.Message}");
                _isTakingOver = false;
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 处理接管期间的内容片段（流式渲染）
        /// </summary>
        public Task<bool> ProcessTakeoverContentAsync(string content)
        {
            try
            {
                if (!_isTakingOver || _takeoverWindow == null)
                    return Task.FromResult(false);

                lock (_updateLock)
                {
                    _takeoverContent.Append(content);
                    _hasPendingUpdate = true;
                }

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log($"MarkdownViewer: 处理内容片段失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 定时器回调：渲染累积的内容
        /// </summary>
        private void RenderPendingContent(object state)
        {
            try
            {
                if (!_isTakingOver || _takeoverWindow == null)
                    return;

                bool shouldRender = false;
                string contentToRender = null;

                lock (_updateLock)
                {
                    if (_hasPendingUpdate)
                    {
                        contentToRender = _takeoverContent.ToString();
                        _hasPendingUpdate = false;
                        shouldRender = true;
                    }
                }

                if (shouldRender && contentToRender != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (_takeoverWindow != null && !_takeoverWindow.IsClosed)
                            {
                                _takeoverWindow.RenderMarkdown(contentToRender);
                                _lastUpdateTime = DateTime.Now;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"MarkdownViewer: 渲染失败: {ex.Message}");
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                Log($"MarkdownViewer: 定时渲染失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 结束接管处理
        /// </summary>
        public Task<string> EndTakeoverAsync()
        {
            try
            {
                Log("MarkdownViewer: 结束接管模式，准备最终渲染");
                
                // 停止定时器
                _renderTimer?.Dispose();
                _renderTimer = null;
                
                var finalContent = _takeoverContent.ToString();
                
                // 最终统一更新：确保所有内容都被正确渲染
                if (_takeoverWindow != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_takeoverWindow != null && !_takeoverWindow.IsClosed)
                        {
                            Log($"MarkdownViewer: 执行最终渲染，内容长度: {finalContent.Length}");
                            
                            // 强制完整渲染，确保没有遗漏
                            _takeoverWindow.RenderMarkdown(finalContent);
                            
                            // 更新窗口标题，标记为完成状态
                            _takeoverWindow.SetTitleFromSource("Markdown ✓");
                            
                            Log("MarkdownViewer: 最终渲染完成");
                        }
                        else
                        {
                            Log("MarkdownViewer: 窗口已关闭，跳过最终渲染");
                        }
                    });
                }
                else
                {
                    Log("MarkdownViewer: 窗口为 null，跳过最终渲染");
                }

                _isTakingOver = false;
                _hasPendingUpdate = false;
                _takeoverContent.Clear();
                
                Log($"MarkdownViewer: 接管结束，最终内容长度: {finalContent.Length}");
                return Task.FromResult("Markdown rendered successfully in streaming mode.");
            }
            catch (Exception ex)
            {
                Log($"MarkdownViewer: 结束接管失败: {ex.Message}");
                _isTakingOver = false;
                _renderTimer?.Dispose();
                _renderTimer = null;
                return Task.FromResult($"Failed to end takeover: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查是否应该结束接管
        /// </summary>
        public bool ShouldEndTakeover(string content)
        {
            // 检测闭合标记 )]
            // 需要确保括号匹配
            int openCount = 0;
            int closeCount = 0;

            foreach (char c in content)
            {
                if (c == '(') openCount++;
                else if (c == ')') closeCount++;
            }

            // 当括号完全匹配且至少有一对括号时，认为应该结束
            var shouldEnd = openCount > 0 && openCount == closeCount && content.TrimEnd().EndsWith(")]");
            
            if (shouldEnd)
            {
                Log($"MarkdownViewer: 检测到接管结束标记，括号匹配: ({openCount}, {closeCount})");
            }

            return shouldEnd;
        }

        #endregion
    }
}
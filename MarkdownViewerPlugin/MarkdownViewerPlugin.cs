using System;
using System.Threading.Tasks;
using System.Windows;
using VPetLLM.Core;

namespace MarkdownViewerPlugin
{
    // 提供简单动作入口：传入内联 Markdown 文本
    public class MarkdownViewerPlugin : IVPetLLMPlugin, IActionPlugin
    {
        public string Name => "MarkdownViewer";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM == null) return "在独立窗口中渲染并显示 Markdown 文档。";
                switch (_vpetLLM.Settings.Language)
                {
                    case "ja": return "Markdown ドキュメントを独立ウィンドウでレンダリングして表示します。";
                    case "zh-hans": return "在独立窗口中渲染并显示 Markdown 文档。";
                    case "zh-hant": return "在獨立視窗中渲染並顯示 Markdown 文件。";
                    case "en":
                    default: return "Render and display Markdown in a standalone window.";
                }
            }
        }
        public string Parameters => "content";
        public string Examples => "[:plugin(MarkdownViewer(# 标题\\n这里是内容))]";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;

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
                var content = (arguments ?? string.Empty);
                
                // 智能识别和处理转义序列
                content = ProcessEscapeSequences(content);
                
                // 智能识别 Markdown 内容
                content = RecognizeMarkdownContent(content);

                // 再次保险：在调用窗口前确保解析器已就绪
                try { DependencyResolver.Ensure(FilePath); } catch { }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var win = new winMarkdownViewer();
                    win.Show();
                    try { win.Activate(); win.Topmost = true; win.Topmost = false; } catch { }

                    if (!string.IsNullOrEmpty(content))
                    {
                        win.RenderMarkdown(content);
                        win.SetTitleFromSource("Inline Markdown");
                    }
                    else
                    {
                        win.RenderMarkdown("# Markdown Viewer\n\n请通过插件参数传入 Markdown 文本进行渲染。");
                        win.SetTitleFromSource("Inline Markdown");
                    }
                });

                return Task.FromResult("Markdown window opened.");
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"MarkdownViewer Function error: {ex.Message}");
                return Task.FromResult($"Failed to open markdown viewer: {ex.Message}");
            }
        }

        /// <summary>
        /// 智能处理转义序列，支持多种格式
        /// </summary>
        private string ProcessEscapeSequences(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

            // 处理常见的转义序列
            content = content.Replace("\\r\\n", "\n")
                           .Replace("\\n", "\n")
                           .Replace("\\r", "\n")
                           .Replace("\\t", "\t")
                           .Replace("\\\"", "\"")
                           .Replace("\\'", "'");

            // 处理 Unicode 转义序列 (如 \u0020)
            var unicodePattern = new System.Text.RegularExpressions.Regex(@"\\u([0-9A-Fa-f]{4})");
            content = unicodePattern.Replace(content, m => 
                ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

            return content.Trim();
        }

        /// <summary>
        /// 智能识别 Markdown 内容特征
        /// </summary>
        private string RecognizeMarkdownContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

            // 检测是否已经是有效的 Markdown 格式
            if (IsValidMarkdown(content))
            {
                _vpetLLM?.Log("MarkdownViewer: Detected valid Markdown content");
                return content;
            }

            // 尝试从常见的包装格式中提取 Markdown
            content = ExtractFromCommonFormats(content);

            return content;
        }

        /// <summary>
        /// 检测是否为有效的 Markdown 内容
        /// </summary>
        private bool IsValidMarkdown(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;

            // 检测常见的 Markdown 特征
            var markdownPatterns = new[]
            {
                @"^#{1,6}\s+",           // 标题 (# ## ### 等)
                @"^\*\*.*\*\*",          // 粗体
                @"^_.*_",                // 斜体
                @"^\*\s+",               // 无序列表
                @"^\d+\.\s+",            // 有序列表
                @"^>\s+",                // 引用
                @"^```",                 // 代码块
                @"^\|.*\|",              // 表格
                @"^\[.*\]\(.*\)",        // 链接
                @"^---+$",               // 分隔线
                @"^===+$"                // 分隔线
            };

            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                foreach (var pattern in markdownPatterns)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), pattern))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 从常见格式中提取 Markdown 内容
        /// </summary>
        private string ExtractFromCommonFormats(string content)
        {
            // 移除可能的 JSON 字符串包装
            if (content.StartsWith("\"") && content.EndsWith("\""))
            {
                content = content.Substring(1, content.Length - 2);
                content = ProcessEscapeSequences(content);
                _vpetLLM?.Log("MarkdownViewer: Extracted from JSON string format");
            }

            // 移除可能的 XML/HTML 包装
            var xmlPattern = new System.Text.RegularExpressions.Regex(@"^<!\[CDATA\[(.*)\]\]>$", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var xmlMatch = xmlPattern.Match(content);
            if (xmlMatch.Success)
            {
                content = xmlMatch.Groups[1].Value;
                _vpetLLM?.Log("MarkdownViewer: Extracted from CDATA format");
            }

            return content;
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
    }
}
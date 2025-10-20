using Markdig;
using System.Text;

namespace MarkdownViewerPlugin
{
    public static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UsePipeTables()
            .UseEmojiAndSmiley()
            .UseTaskLists()
            .Build();

        public static string RenderToHtml(string markdown)
        {
            var body = Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
            var html = new StringBuilder();
            html.Append("<!DOCTYPE html><html><head><meta charset='utf-8' />");
            // 基础样式
            html.Append("<style>");
            html.Append(@"body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'Noto Sans',sans-serif,'Apple Color Emoji','Segoe UI Emoji';line-height:1.6;padding:24px;color:#1f2328;background:#fff;}
h1,h2,h3{border-bottom:1px solid #eaecef;padding-bottom:.3em;}
pre{background:#0d1117;color:#c9d1d9;padding:12px;border-radius:6px;overflow:auto;}
code{background:#f6f8fa;padding:2px 4px;border-radius:4px;}
table{border-collapse:collapse;}
td,th{border:1px solid #d0d7de;padding:6px 12px;}
a{color:#0969da;text-decoration:none;} a:hover{text-decoration:underline;}
blockquote{color:#57606a;border-left:4px solid #d0d7de;padding:0 1em;}");
            html.Append("</style>");
            // highlight.js
            html.Append("<link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github-dark.min.css'>");
            html.Append("<script src='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js'></script>");
            html.Append("<script>document.addEventListener('DOMContentLoaded',()=>{document.querySelectorAll('pre code').forEach(el=>hljs.highlightElement(el));});</script>");
            html.Append("</head><body>");
            html.Append(body);
            html.Append("</body></html>");
            return html.ToString();
        }
    }
}
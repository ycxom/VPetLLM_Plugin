using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace MarkdownViewerPlugin
{
    /// <summary>Markdown 阅读器插件的调用契约。整段标记内容就是要渲染的正文。</summary>
    public partial class MarkdownViewerPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, form, contentDesc, remarks) = lang switch
            {
                "zh-hans" => (
                    "把长内容渲染成排版良好的窗口给用户看",
                    "打开一个 Markdown 窗口展示内容",
                    "Markdown 正文，支持标题、列表、表格和代码块",
                    "适合放代码、教程、对比表这类气泡里塞不下的内容。整段标记之间的文字就是正文，不要再套 name(value)。"),
                "zh-hant" => (
                    "把長內容渲染成排版良好的窗口給用戶看",
                    "打開一個 Markdown 窗口展示內容",
                    "Markdown 正文，支持標題、列表、表格和程式碼區塊",
                    "適合放程式碼、教程、對比表這類氣泡裡塞不下的內容。整段標記之間的文字就是正文，不要再套 name(value)。"),
                "ja" => (
                    "長い内容を整形されたウィンドウで表示する",
                    "Markdown ウィンドウを開いて内容を表示する",
                    "Markdown 本文。見出し・リスト・表・コードブロックに対応",
                    "コードやチュートリアル、比較表など吹き出しに収まらない内容向けです。タグの間のテキストがそのまま本文になります。name(value) で包まないでください。"),
                _ => (
                    "Render long content in a well-formatted window for the user",
                    "Open a Markdown window showing the content",
                    "Markdown body; supports headings, lists, tables and code blocks",
                    "Good for code, tutorials and comparison tables that do not fit in a speech bubble. The text between the markers is the body itself — do not wrap it in name(value).")
            };

            return new ToolSchema
            {
                Summary = summary,
                Remarks = remarks,
                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = form,
                        Style = ToolCallStyle.RawText,
                        Parameters = new[] { ToolParameter.Str("markdown_content", contentDesc) },
                        Example = "# 标题\n\n- 要点一\n- 要点二"
                    }
                }
            };
        }
    }
}

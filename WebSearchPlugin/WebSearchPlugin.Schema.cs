using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace WebSearchPlugin
{
    /// <summary>
    /// 联网搜索插件的调用契约。放在独立的 partial 文件里，主文件不用改。
    ///
    /// 注意本插件用的是 <c>动词|参数</c> 的竖线语法，不是其它插件的 <c>name(value)</c>，
    /// 所以每种形态都显式给出示例，不走渲染器的自动生成。
    /// </summary>
    public partial class WebSearchPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, searchForm, queryDesc, fetchForm, urlDesc, settingForm, remarks) = lang switch
            {
                "zh-hans" => (
                    "联网搜索与网页抓取",
                    "用关键词搜索互联网，返回若干条结果摘要",
                    "搜索关键词",
                    "抓取指定网页并转成 Markdown 正文",
                    "完整网址，需带 http:// 或 https://",
                    "打开搜索插件的设置窗口",
                    "参数用竖线分隔，不是 name(value) 形式。"),
                "zh-hant" => (
                    "聯網搜索與網頁抓取",
                    "用關鍵詞搜索互聯網，返回若干條結果摘要",
                    "搜索關鍵詞",
                    "抓取指定網頁並轉成 Markdown 正文",
                    "完整網址，需帶 http:// 或 https://",
                    "打開搜索插件的設置窗口",
                    "參數用豎線分隔，不是 name(value) 形式。"),
                "ja" => (
                    "ウェブ検索とページ取得",
                    "キーワードでウェブを検索し、結果の要約を返す",
                    "検索キーワード",
                    "指定したページを取得して Markdown 本文に変換する",
                    "完全な URL（http:// または https:// を含む）",
                    "検索プラグインの設定を開く",
                    "引数は縦棒区切りで、name(value) 形式ではありません。"),
                _ => (
                    "Web search and page fetching",
                    "Search the web for a query and return result summaries",
                    "The search query",
                    "Fetch a web page and convert it to Markdown body text",
                    "Full URL, including http:// or https://",
                    "Open the web search plugin settings window",
                    "Arguments use a pipe separator, not the name(value) form.")
            };

            return new ToolSchema
            {
                Summary = summary,
                Remarks = remarks,
                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = searchForm,
                        Style = ToolCallStyle.RawText,
                        Parameters = new[] { ToolParameter.Str("search|query", queryDesc) },
                        Example = "search|AMD 9950HX"
                    },
                    new ToolCallForm
                    {
                        Summary = fetchForm,
                        Style = ToolCallStyle.RawText,
                        Parameters = new[] { ToolParameter.Str("fetch|url", urlDesc) },
                        Example = "fetch|https://example.com"
                    },
                    new ToolCallForm
                    {
                        Summary = settingForm,
                        Parameters = new[] { ToolParameter.Choice("action", "", new[] { "setting" }) },
                        Example = "action(setting)"
                    }
                }
            };
        }
    }
}

using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace DiaryPlugin
{
    /// <summary>日记插件的调用契约。</summary>
    public partial class DiaryPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, searchForm, keywordDesc, viewForm, writeForm, remarks) = lang switch
            {
                "zh-hans" => (
                    "写日记与翻查过往日记",
                    "在过往日记里按关键词检索",
                    "检索关键词",
                    "打开日记查看器（也是设置入口）",
                    "立刻补写今天的日记",
                    "只有 search 会把结果回传给你；view 和 write 只在本地开窗口/后台执行，不返回内容。"),
                "zh-hant" => (
                    "寫日記與翻查過往日記",
                    "在過往日記裡按關鍵詞檢索",
                    "檢索關鍵詞",
                    "打開日記查看器（也是設置入口）",
                    "立刻補寫今天的日記",
                    "只有 search 會把結果回傳給你；view 和 write 只在本地開窗口/後台執行，不返回內容。"),
                "ja" => (
                    "日記を書く・過去の日記を調べる",
                    "過去の日記をキーワードで検索する",
                    "検索キーワード",
                    "日記ビューアを開く（設定もここ）",
                    "今日の日記をすぐに書く",
                    "結果が返るのは search だけです。view と write はローカルで実行され、内容は返しません。"),
                _ => (
                    "Write diary entries and search past ones",
                    "Search past diary entries by keyword",
                    "Search keyword",
                    "Open the diary viewer (also the settings entry point)",
                    "Write today's diary entry right now",
                    "Only search returns content to you; view and write act locally and return nothing.")
            };

            var keywordSample = lang switch
            {
                "zh-hans" => "海边",
                "zh-hant" => "海邊",
                "ja" => "海",
                _ => "beach"
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
                        Parameters = new[] { ToolParameter.Str("search", keywordDesc, sample: keywordSample) },
                        Example = $"search({keywordSample})"
                    },
                    new ToolCallForm
                    {
                        Summary = viewForm,
                        Parameters = new[] { ToolParameter.Choice("action", "", new[] { "view", "setting" }) },
                        Example = "action(view)"
                    },
                    new ToolCallForm
                    {
                        Summary = writeForm,
                        Parameters = new[] { ToolParameter.Choice("action", "", new[] { "write" }) },
                        Example = "action(write)"
                    }
                }
            };
        }
    }
}

using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace PixivPlugin
{
    /// <summary>Pixiv 插件的调用契约。</summary>
    public partial class PixivPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, searchForm, keywordDesc, pageDesc, randomForm, settingForm) = lang switch
            {
                "zh-hans" => (
                    "从 Pixiv 取插画",
                    "按关键词搜索插画",
                    "搜索关键词，通常是作品名或角色名",
                    "结果页码",
                    "随机取一张插画（省略 action 时也走这条）",
                    "打开 Pixiv 插件的设置窗口"),
                "zh-hant" => (
                    "從 Pixiv 取插畫",
                    "按關鍵詞搜索插畫",
                    "搜索關鍵詞，通常是作品名或角色名",
                    "結果頁碼",
                    "隨機取一張插畫（省略 action 時也走這條）",
                    "打開 Pixiv 插件的設置窗口"),
                "ja" => (
                    "Pixiv からイラストを取得する",
                    "キーワードでイラストを検索する",
                    "検索キーワード。作品名やキャラクター名など",
                    "結果のページ番号",
                    "ランダムに1枚取得する（action を省略した場合もこれ）",
                    "Pixiv プラグインの設定を開く"),
                _ => (
                    "Fetch illustrations from Pixiv",
                    "Search for illustrations by keyword",
                    "Search keyword, usually a title or character name",
                    "Result page number",
                    "Fetch a random illustration (also the default when action is omitted)",
                    "Open the Pixiv plugin settings window")
            };

            var keywordSample = lang switch
            {
                "zh-hans" or "zh-hant" => "小鸟游星野",
                "ja" => "小鳥遊星野",
                _ => "Hoshino"
            };

            return new ToolSchema
            {
                Summary = summary,
                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = searchForm,
                        Parameters = new[]
                        {
                            ToolParameter.Choice("action", "", new[] { "search" }),
                            ToolParameter.Str("keyword", keywordDesc, sample: keywordSample),
                            ToolParameter.Int("page", pageDesc, required: false, @default: "1")
                        },
                        Example = $"action(search), keyword({keywordSample})"
                    },
                    new ToolCallForm
                    {
                        Summary = randomForm,
                        Parameters = new[]
                        {
                            ToolParameter.Choice("action", "", new[] { "random" }, required: false, @default: "random")
                        },
                        Example = "action(random)"
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

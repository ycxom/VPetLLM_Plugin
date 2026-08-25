using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace StickerPlugin
{
    /// <summary>
    /// 表情包插件的调用契约。放在独立的 partial 文件里，主文件不用改。
    /// </summary>
    public partial class StickerPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, sendForm, tagsDesc, settingForm) = lang switch
            {
                "zh-hans" => (
                    "发送表情包",
                    "按标签挑一张表情包发给用户",
                    "描述表情的标签，多个用逗号分隔",
                    "打开表情包 MOD 的设置窗口"),
                "zh-hant" => (
                    "發送表情包",
                    "按標籤挑一張表情包發給用戶",
                    "描述表情的標籤，多個用逗號分隔",
                    "打開表情包 MOD 的設置窗口"),
                "ja" => (
                    "スタンプを送る",
                    "タグに合うスタンプを1枚選んで送る",
                    "スタンプを表すタグ。複数はカンマ区切り",
                    "スタンプ MOD の設定を開く"),
                _ => (
                    "Send a sticker",
                    "Pick a sticker matching the given tags and send it",
                    "Tags describing the sticker; separate multiple tags with commas",
                    "Open the sticker MOD settings window")
            };

            var tagSample = lang switch
            {
                "zh-hans" => "可爱, 开心",
                "zh-hant" => "可愛, 開心",
                "ja" => "かわいい, うれしい",
                _ => "cute, happy"
            };

            return new ToolSchema
            {
                Summary = summary,

                // 表情包要和模型说出口的那句话一起出现。走原生工具的话插件在 LLM 请求
                // 过程中就被调用，表情包会抢在回复之前先弹出来；而且那时独占会话还没建立，
                // MOD 的气泡触发没被挂起，它自己的情感表情会和这张表情包抢显示位。
                RequiresReplySession = true,

                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = sendForm,
                        Parameters = new[]
                        {
                            // action 省略时插件默认按 send 处理，所以标成可选
                            ToolParameter.Choice("action", "", new[] { "send" }, required: false, @default: "send"),
                            ToolParameter.Str("tags", tagsDesc, sample: tagSample)
                        },
                        Example = $"action(send), tags({tagSample})"
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

using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace OneBotPlugin
{
    /// <summary>
    /// OneBot（QQ）插件的调用契约。
    ///
    /// 这个插件的主形态吃的是一段 JSON，不是 <c>name(value)</c>，所以用 RawText 形态
    /// 加显式示例。另外 <c>action(...)</c> 必须写在开头（解析用的是 StartsWith），
    /// 这点在签名注释里点明。
    /// </summary>
    public partial class OneBotPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, groupForm, groupDesc, privateForm, privateDesc, actionForm, remarks) = lang switch
            {
                "zh-hans" => (
                    "通过 QQ 收发消息",
                    "回复群聊",
                    "JSON 对象：group_id 为群号，message 为文本；可选 image 为图片 URL",
                    "回复私聊",
                    "JSON 对象：user_id 为对方 QQ 号，message 为文本；可选 image 为图片 URL",
                    "打开设置 / 查看连接状态 / 重连",
                    "收到 [OneBot Group:xxx User:xxx] 形式的消息后用本插件回复。message 字段是发到 QQ 的内容，" +
                    "和桌宠气泡（say）是两回事。action 形态必须把 action(...) 写在最开头。"),
                "zh-hant" => (
                    "通過 QQ 收發消息",
                    "回覆群聊",
                    "JSON 物件：group_id 為群號，message 為文本；可選 image 為圖片 URL",
                    "回覆私聊",
                    "JSON 物件：user_id 為對方 QQ 號，message 為文本；可選 image 為圖片 URL",
                    "打開設置 / 查看連接狀態 / 重連",
                    "收到 [OneBot Group:xxx User:xxx] 形式的消息後用本插件回覆。message 欄位是發到 QQ 的內容，" +
                    "和桌寵氣泡（say）是兩回事。action 形態必須把 action(...) 寫在最開頭。"),
                "ja" => (
                    "QQ でメッセージを送受信する",
                    "グループに返信する",
                    "JSON オブジェクト：group_id はグループ番号、message は本文。image に画像 URL を指定可",
                    "個人チャットに返信する",
                    "JSON オブジェクト：user_id は相手の QQ 番号、message は本文。image に画像 URL を指定可",
                    "設定を開く / 接続状態を確認 / 再接続",
                    "[OneBot Group:xxx User:xxx] 形式のメッセージを受け取ったら本プラグインで返信します。message は QQ に送る内容で、" +
                    "デスクトップペットの吹き出し（say）とは別物です。action 形態では action(...) を先頭に置く必要があります。"),
                _ => (
                    "Send and receive messages over QQ",
                    "Reply to a group chat",
                    "JSON object: group_id is the group number, message is the text; optional image is an image URL",
                    "Reply to a direct message",
                    "JSON object: user_id is the recipient's QQ number, message is the text; optional image is an image URL",
                    "Open settings / check connection status / reconnect",
                    "Use this plugin to reply after receiving a [OneBot Group:xxx User:xxx] message. The message field is what gets sent to QQ, " +
                    "which is different from the pet's speech bubble (say). For the action form, action(...) must come first in the argument.")
            };

            return new ToolSchema
            {
                Summary = summary,
                Remarks = remarks,
                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = groupForm,
                        Style = ToolCallStyle.RawText,
                        Parameters = new[] { ToolParameter.Str("json", groupDesc) },
                        Example = "{\"group_id\":123456,\"message\":\"你好小明！\"}"
                    },
                    new ToolCallForm
                    {
                        Summary = privateForm,
                        Style = ToolCallStyle.RawText,
                        Parameters = new[] { ToolParameter.Str("json", privateDesc) },
                        Example = "{\"user_id\":789012,\"message\":\"收到了\"}"
                    },
                    new ToolCallForm
                    {
                        Summary = actionForm,
                        Parameters = new[]
                        {
                            ToolParameter.Choice("action", "", new[] { "setting", "status", "reconnect" })
                        },
                        Example = "action(status)"
                    }
                }
            };
        }
    }
}

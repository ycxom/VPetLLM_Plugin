using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace RemoteChatPlugin;

/// <summary>远程聊天插件的调用契约。</summary>
public sealed partial class RemoteChatPlugin : IToolSchemaPlugin
{
    public ToolSchema? GetToolSchema()
    {
        var lang = _vpetLLM?.Settings.Language ?? "en";

        var (summary, actionForm, actionDesc, configureForm, serverDesc, webDesc, remarks) = lang switch
        {
            "zh-hans" => (
                "让用户在浏览器里远程和桌宠聊天",
                "查询状态或管理配对",
                "status 看连接状态；pair 复制配对链接；key 复制访问密钥；disconnect 断开浏览器；" +
                "rotate 轮换配对凭据（旧链接立即失效）；official 切回官方服务器；reconnect 重连",
                "改成自建服务器",
                "WebSocket 地址，生产环境必须是 wss://（localhost 可用 ws://）",
                "网页地址，生产环境必须是 https://（localhost 可用 http://）",
                "配对链接和访问密钥只会复制到用户本机剪贴板，不会显示出来，你也读不到 —— 不要试图转述它们。"),
            "zh-hant" => (
                "讓用戶在瀏覽器裡遠程和桌寵聊天",
                "查詢狀態或管理配對",
                "status 看連接狀態；pair 複製配對連結；key 複製訪問密鑰；disconnect 斷開瀏覽器；" +
                "rotate 輪換配對憑據（舊連結立即失效）；official 切回官方伺服器；reconnect 重連",
                "改成自建伺服器",
                "WebSocket 地址，生產環境必須是 wss://（localhost 可用 ws://）",
                "網頁地址，生產環境必須是 https://（localhost 可用 http://）",
                "配對連結和訪問密鑰只會複製到用戶本機剪貼簿，不會顯示出來，你也讀不到 —— 不要試圖轉述它們。"),
            "ja" => (
                "ブラウザからペットと遠隔で会話できるようにする",
                "状態確認とペアリング管理",
                "status は接続状態、pair はペアリングリンクをコピー、key はアクセスキーをコピー、" +
                "disconnect はブラウザを切断、rotate は資格情報を更新（旧リンクは無効）、official は公式サーバーに戻す、reconnect は再接続",
                "自前サーバーに切り替える",
                "WebSocket アドレス。本番では wss:// 必須（localhost は ws:// 可）",
                "Web アドレス。本番では https:// 必須（localhost は http:// 可）",
                "ペアリングリンクとアクセスキーはユーザーのクリップボードにコピーされるだけで表示されず、あなたも読めません。復唱しようとしないでください。"),
            _ => (
                "Let the user chat with the pet remotely from a browser",
                "Check status or manage pairing",
                "status shows the connection; pair copies the pairing link; key copies the access key; disconnect drops the browser; " +
                "rotate re-issues credentials (old links stop working); official switches back to the official server; reconnect restarts the link",
                "Switch to a self-hosted server",
                "WebSocket address; production must use wss:// (localhost may use ws://)",
                "Web address; production must use https:// (localhost may use http://)",
                "Pairing links and access keys are only copied to the user's local clipboard, never displayed, and you cannot read them — do not try to repeat them.")
        };

        return new ToolSchema
        {
            Summary = summary,
            Remarks = remarks,
            Forms = new[]
            {
                new ToolCallForm
                {
                    Summary = actionForm,
                    Parameters = new[]
                    {
                        ToolParameter.Choice("action", actionDesc,
                            new[] { "status", "pair", "key", "disconnect", "rotate", "official", "reconnect" })
                    },
                    Example = "action(status)"
                },
                new ToolCallForm
                {
                    Summary = configureForm,
                    Parameters = new[]
                    {
                        ToolParameter.Choice("action", "", new[] { "configure" }),
                        ToolParameter.Str("server", serverDesc, sample: "wss://example.com/ws"),
                        ToolParameter.Str("web", webDesc, sample: "https://example.com")
                    },
                    Example = "action(configure), server(wss://example.com/ws), web(https://example.com)"
                }
            }
        };
    }
}

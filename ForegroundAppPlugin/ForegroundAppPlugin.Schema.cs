using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace ForegroundAppPlugin
{
    /// <summary>前台应用感知插件的调用契约。它唯一的调用形态就是打开设置。</summary>
    public partial class ForegroundAppPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, settingForm, remarks) = lang switch
            {
                "zh-hans" => (
                    "感知用户当前正在使用的应用",
                    "打开前台应用插件的设置窗口",
                    "当前前台应用是自动写进上下文的，不需要你主动调用；本插件只提供设置入口。"),
                "zh-hant" => (
                    "感知用戶當前正在使用的應用",
                    "打開前台應用插件的設置窗口",
                    "當前前台應用是自動寫進上下文的，不需要你主動調用；本插件只提供設置入口。"),
                "ja" => (
                    "ユーザーが今使っているアプリを把握する",
                    "フォアグラウンドアプリプラグインの設定を開く",
                    "現在のアプリ情報は自動的にコンテキストへ入るため、呼び出す必要はありません。本プラグインは設定画面のみを提供します。"),
                _ => (
                    "Awareness of the app the user is currently using",
                    "Open the foreground app plugin settings window",
                    "The current foreground app is injected into context automatically; you do not need to call this. The plugin only exposes settings.")
            };

            return new ToolSchema
            {
                Summary = summary,
                Remarks = remarks,
                Forms = new[]
                {
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

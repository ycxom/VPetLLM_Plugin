using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace AppLauncherPlugin
{
    /// <summary>应用启动器的调用契约。</summary>
    public partial class AppLauncherPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, launchForm, nameDesc, listForm, settingForm, remarks) = lang switch
            {
                "zh-hans" => (
                    "启动用户电脑上的应用程序",
                    "按名称启动一个应用",
                    "应用名称，需与已登记的名称一致；不确定时先用 action(list) 查",
                    "列出所有可启动的应用",
                    "打开应用启动器的设置窗口",
                    "只能启动设置里已登记的应用，不是任意可执行文件。"),
                "zh-hant" => (
                    "啟動用戶電腦上的應用程式",
                    "按名稱啟動一個應用",
                    "應用名稱，需與已登記的名稱一致；不確定時先用 action(list) 查",
                    "列出所有可啟動的應用",
                    "打開應用啟動器的設置窗口",
                    "只能啟動設置裡已登記的應用，不是任意可執行檔。"),
                "ja" => (
                    "ユーザーのPCでアプリを起動する",
                    "名前を指定してアプリを起動する",
                    "アプリ名。登録済みの名前と一致させること。不明なら先に action(list) で確認",
                    "起動可能なアプリを一覧表示する",
                    "アプリランチャーの設定を開く",
                    "設定に登録済みのアプリのみ起動できます。任意の実行ファイルは起動できません。"),
                _ => (
                    "Launch applications on the user's machine",
                    "Launch an application by name",
                    "Application name; must match a registered entry. Use action(list) first if unsure",
                    "List every launchable application",
                    "Open the app launcher settings window",
                    "Only applications registered in settings can be launched, not arbitrary executables.")
            };

            return new ToolSchema
            {
                Summary = summary,
                Remarks = remarks,
                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = launchForm,
                        // 不带 action() 时整段内容就当作应用名
                        Style = ToolCallStyle.RawText,
                        Parameters = new[] { ToolParameter.Str("app_name", nameDesc) }
                    },
                    new ToolCallForm
                    {
                        Summary = listForm,
                        Parameters = new[] { ToolParameter.Choice("action", "", new[] { "list" }) },
                        Example = "action(list)"
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

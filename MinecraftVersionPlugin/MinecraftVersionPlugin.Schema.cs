using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace MinecraftVersionPlugin
{
    /// <summary>Minecraft 服务器监视插件的调用契约。</summary>
    public partial class MinecraftVersionPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, statusForm, settingForm) = lang switch
            {
                "zh-hans" => (
                    "查看已配置的 Minecraft 服务器",
                    "查询各服务器的在线人数与版本",
                    "打开插件设置窗口，管理服务器列表"),
                "zh-hant" => (
                    "查看已配置的 Minecraft 伺服器",
                    "查詢各伺服器的線上人數與版本",
                    "打開插件設置窗口，管理伺服器列表"),
                "ja" => (
                    "設定済みの Minecraft サーバーを確認する",
                    "各サーバーのオンライン人数とバージョンを取得する",
                    "プラグイン設定を開いてサーバー一覧を管理する"),
                _ => (
                    "Check configured Minecraft servers",
                    "Get each server's player count and version",
                    "Open the plugin settings window to manage the server list")
            };

            return new ToolSchema
            {
                Summary = summary,
                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = statusForm,
                        Parameters = new[] { ToolParameter.Choice("action", "", new[] { "servers_status" }) },
                        Example = "action(servers_status)"
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

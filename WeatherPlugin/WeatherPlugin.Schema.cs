using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

namespace WeatherPlugin
{
    /// <summary>
    /// 天气插件的调用契约。放在独立的 partial 文件里，主文件不用改。
    /// </summary>
    public partial class WeatherPlugin : IToolSchemaPlugin
    {
        public ToolSchema? GetToolSchema()
        {
            var lang = _vpetLLM?.Settings.Language ?? "en";

            var (summary, queryForm, cityDesc, typeDesc, settingForm, remarks) = lang switch
            {
                "zh-hans" => (
                    "查询天气",
                    "查询指定城市的实时天气或未来预报",
                    "城市名；留空则使用设置里的默认城市",
                    "current 为实时天气，forecast 为未来预报",
                    "打开天气插件的设置窗口",
                    "数据源仅覆盖中国大陆的省市区县，查询其他国家和地区会直接失败。"),
                "zh-hant" => (
                    "查詢天氣",
                    "查詢指定城市的即時天氣或未來預報",
                    "城市名；留空則使用設置裡的預設城市",
                    "current 為即時天氣，forecast 為未來預報",
                    "打開天氣插件的設置窗口",
                    "資料源僅覆蓋中國大陸的省市區縣，查詢其他國家和地區會直接失敗。"),
                "ja" => (
                    "天気を調べる",
                    "指定した都市の現在の天気または予報を取得する",
                    "都市名。省略すると設定の既定都市を使用",
                    "current は現在の天気、forecast は予報",
                    "天気プラグインの設定を開く",
                    "データソースは中国本土の都市のみ対応しており、他の国や地域は取得できません。"),
                _ => (
                    "Look up the weather",
                    "Get current conditions or the forecast for a city",
                    "City name; omit to use the default city from settings",
                    "current for live conditions, forecast for the outlook",
                    "Open the weather plugin settings window",
                    "The data source only covers cities in mainland China; other countries and regions will fail.")
            };

            var citySample = lang is "zh-hans" or "zh-hant" ? "北京" : "Beijing";

            return new ToolSchema
            {
                Summary = summary,
                Remarks = remarks,
                Forms = new[]
                {
                    new ToolCallForm
                    {
                        Summary = queryForm,
                        Parameters = new[]
                        {
                            ToolParameter.Str("city", cityDesc, required: false, sample: citySample),
                            ToolParameter.Choice("type", typeDesc, new[] { "current", "forecast" },
                                required: false, @default: "current")
                        }
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

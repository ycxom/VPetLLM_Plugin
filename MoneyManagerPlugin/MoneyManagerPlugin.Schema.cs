using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

/// <summary>金钱管理插件的调用契约。本插件没有命名空间，与主文件保持一致。</summary>
public partial class MoneyManagerPlugin : IToolSchemaPlugin
{
    public ToolSchema? GetToolSchema()
    {
        var lang = _vpetLLM?.Settings.Language ?? "en";

        var (summary, form, actionDesc, amountDesc, remarks) = lang switch
        {
            "zh-hans" => (
                "增减或设定桌宠的金钱",
                "调整金钱数额",
                "add 增加，sub 扣除，set 直接设为该数值",
                "金额，可带小数",
                "action 和 amount 两个参数都必须给，缺一个调用会失败。"),
            "zh-hant" => (
                "增減或設定桌寵的金錢",
                "調整金錢數額",
                "add 增加，sub 扣除，set 直接設為該數值",
                "金額，可帶小數",
                "action 和 amount 兩個參數都必須給，缺一個調用會失敗。"),
            "ja" => (
                "ペットの所持金を増減・設定する",
                "所持金を調整する",
                "add は加算、sub は減算、set はその値に設定",
                "金額（小数可）",
                "action と amount は両方必須です。片方でも欠けると失敗します。"),
            _ => (
                "Adjust or set the pet's money",
                "Change the money amount",
                "add to increase, sub to deduct, set to assign the value directly",
                "Amount, decimals allowed",
                "Both action and amount are required; omitting either makes the call fail.")
        };

        return new ToolSchema
        {
            Summary = summary,
            Remarks = remarks,
            Forms = new[]
            {
                new ToolCallForm
                {
                    Summary = form,
                    Parameters = new[]
                    {
                        ToolParameter.Choice("action", actionDesc, new[] { "add", "sub", "set" }),
                        ToolParameter.Num("amount", amountDesc, sample: "100")
                    },
                    Example = "action(add), amount(100)"
                }
            }
        };
    }
}

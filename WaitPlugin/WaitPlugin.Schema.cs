using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

/// <summary>
/// 等待插件的调用契约。本插件没有命名空间，与主文件保持一致。
///
/// 这里纠正了一处老描述里的错误说法：旧的 Parameters/Examples 反复强调
/// "必须写成 seconds(N)，写裸数字会被忽略"，但实际解析是
/// <c>new Regex(@"(\d+)").Match(arguments)</c> —— 抓的是参数里的**任意一串数字**，
/// 裸数字完全有效（插件自己的报错文案给的例子就是裸数字）。
/// 照旧描述写反而会让模型以为自己写错了。
/// </summary>
public partial class WaitPlugin : IToolSchemaPlugin
{
    public ToolSchema? GetToolSchema()
    {
        var lang = _vpetLLM?.Settings.Language ?? "en";

        var (summary, form, secondsDesc, remarks) = lang switch
        {
            "zh-hans" => (
                "等待用户一段时间不回复",
                "开始一次等待计时",
                "等待的秒数",
                "本调用不会立刻返回结果。用户若在时限内回话，等待自动取消；若超时无人回应，" +
                "系统会发来 wait_timeout 通知，那时你再决定要不要提醒用户。重复调用会取消上一次等待。"),
            "zh-hant" => (
                "等待用戶一段時間不回覆",
                "開始一次等待計時",
                "等待的秒數",
                "本調用不會立刻返回結果。用戶若在時限內回話，等待自動取消；若超時無人回應，" +
                "系統會發來 wait_timeout 通知，那時你再決定要不要提醒用戶。重複調用會取消上一次等待。"),
            "ja" => (
                "ユーザーの返事を一定時間待つ",
                "待機タイマーを開始する",
                "待機する秒数",
                "この呼び出しはすぐには結果を返しません。時間内に返事があれば待機は自動的に取り消され、" +
                "時間切れになると wait_timeout が届きます。再度呼ぶと前回の待機は取り消されます。"),
            _ => (
                "Wait for a while without a user reply",
                "Start a wait timer",
                "How many seconds to wait",
                "This call does not return a result immediately. If the user replies in time the wait is cancelled; " +
                "if it times out the system sends you wait_timeout and you decide whether to nudge the user. " +
                "Calling again cancels the previous wait.")
        };

        return new ToolSchema
        {
            Summary = summary,
            Remarks = remarks,
            Forms = new[]
            {
                new ToolCallForm
                {
                    // 解析只是从参数里抓第一串数字，所以裸数字和 seconds(60) 都行
                    Summary = form,
                    Style = ToolCallStyle.RawText,
                    Parameters = new[] { ToolParameter.Int("seconds", secondsDesc, sample: "60") },
                    Example = "60"
                }
            }
        };
    }
}

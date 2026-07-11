using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;

public class WaitPlugin : IActionPlugin, IDynamicInfoPlugin
{
    public string Name => "wait";
    public string Author => "ycxom";
    public string Description
    {
        get
        {
            if (_vpetLLM is null) return "让AI等待用户回复一段时间（秒），不阻塞当前回复。如果用户在等待期间回复了，等待会被自动取消；如果超时用户仍未回复，系统会通知LLM，届时LLM会主动提醒用户。";
            switch (_vpetLLM.Settings.Language)
            {
                case "ja":
                    return "AIがユーザーの返信を一定秒数待ちます（非ブロッキング）。待機中にユーザーが返信すれば自動的にキャンセルされます。タイムアウトした場合はシステムからAIに通知され、AIがユーザーに催促します。";
                case "zh-hans":
                    return "让AI等待用户回复一段时间（秒），不阻塞当前回复。如果用户在等待期间回复了，等待会被自动取消；如果超时用户仍未回复，系统会通知LLM，届时LLM会主动提醒用户。";
                case "zh-hant":
                    return "讓AI等待使用者回覆一段時間（秒），不阻塞當前回覆。如果使用者在等待期間回覆了，等待會被自動取消；如果超時使用者仍未回覆，系統會通知LLM，屆時LLM會主動提醒使用者。";
                case "en":
                default:
                    return "Wait for the user to reply within N seconds without blocking. If the user replies during the wait, it is automatically cancelled. If it times out with no reply, the system will notify the LLM so it can proactively remind the user.";
            }
        }
    }
    public string Parameters => "seconds(int) — MUST be written as seconds(60), NOT a bare number";
    public string Examples => "Example: `<|plugin_wait_begin|> seconds(60) <|plugin_wait_end|>` IMPORTANT: the argument MUST be in the form seconds(N) (e.g. seconds(30)), never a bare number, or the command is ignored. If the user replies in time, nothing happens. If the time passes with no reply, the system sends you \"wait_timeout, no reply from user within Ns\", then you can remind the user.";
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;
    private CancellationTokenSource? _cts;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        VPetLLM.Utils.System.Logger.Log("Wait Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        if (_vpetLLM is null) return Task.FromResult("VPetLLM instance is not initialized.");

        var match = new Regex(@"(\d+)").Match(arguments ?? "");
        if (!match.Success)
        {
            return Task.FromResult("等待失败：请提供等待的秒数，例如 <|plugin_wait_begin|>60<|plugin_wait_end|>。");
        }

        var seconds = int.Parse(match.Groups[1].Value);
        if (seconds <= 0) seconds = 1;

        // 新的等待请求会取消之前尚未完成的等待
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        Log($"WaitPlugin: 开始等待 {seconds} 秒");
        _ = WaitTask(seconds, cts);

        // 返回空字符串，不触发回灌给AI，避免AI误以为等待已结束
        return Task.FromResult("");
    }

    private async Task WaitTask(int seconds, CancellationTokenSource cts)
    {
        if (_vpetLLM is null) return;

        try
        {
            var initialUserMessageCount = TryCountUserMessages() ?? 0;
            var elapsed = 0;

            try
            {
                while (elapsed < seconds)
                {
                    await Task.Delay(1000, cts.Token);
                    elapsed++;

                    // TryCountUserMessages 返回 null 表示本次读取失败（历史列表正在被修改），跳过本次比较
                    var current = TryCountUserMessages();
                    if (current.HasValue && current.Value > initialUserMessageCount)
                    {
                        Log($"WaitPlugin: 检测到用户在 {elapsed}s 时回复，取消等待通知");
                        return;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Log("WaitPlugin: 等待被新的等待请求取消");
                return;
            }

            if (cts.IsCancellationRequested) return;

            Log($"WaitPlugin: {seconds}s 超时，用户未回复，通知AI");
            var response = $"wait_timeout, no reply from user within {seconds}s";
            var result = await _vpetLLM.ChatCore.Chat(response, true);
            Log($"WaitPlugin: 超时通知已发送，AI回复长度: {result?.Length ?? 0}");
        }
        catch (Exception e)
        {
            Log($"WaitPlugin: WaitTask 异常: {e}");
        }
    }

    /// <summary>
    /// 统计历史中的用户消息数。GetChatHistory() 返回的是实时列表（非副本），
    /// 后台线程枚举时若主线程正在修改会抛 InvalidOperationException，
    /// 此时返回 null 表示本次读取无效，由调用方跳过。
    /// </summary>
    private int? TryCountUserMessages()
    {
        if (_vpetLLM is null) return 0;
        try
        {
            var count = 0;
            // ToArray 先做一次快照，尽量缩小与主线程修改的竞态窗口
            foreach (var m in _vpetLLM.GetChatHistory().ToArray())
            {
                if ((m.MessageType == "User") || (m.MessageType is null && m.NormalizedRole == "user"))
                    count++;
            }
            return count;
        }
        catch (InvalidOperationException)
        {
            // 列表在枚举期间被修改，本次读取作废
            return null;
        }
    }

    public void Invoke()
    {
    }

    public string GetDynamicInfo()
    {
        if (_vpetLLM is null) return "";
        var lang = _vpetLLM.Settings.Language ?? "en";

        return lang switch
        {
            "zh-hans" => @"等待插件功能说明 (Wait Plugin)
当用户表达要等待、犹豫、或需要思考时（如 ""等一下我再告诉你""、""让我想想""、""给我点时间""），你应该使用此插件来暂停并等待用户的进一步回复。

使用方式:
<|plugin_wait_begin|> seconds(30) <|plugin_wait_end|>

说明:
- 参数格式必须是 seconds(N)，例如 seconds(30) 表示等待30秒，seconds(60) 表示等待60秒
- 等待期间用户若有新回复，等待立即取消，你可以继续处理用户的新输入
- 如果超时（用户在指定秒数内未回复），系统会主动唤起你，告知你用户未在时间内回复，届时你可以礼貌地提醒用户

这样做的好处:
- 避免在用户还没想好时就重复回答同样的问题
- 充分利用等待时间，不用在这里傻等
- 用户若改变主意或有新想法，可以立即被你处理

适用场景: 用户犹豫、需要查资料、需要思考、或明确说要稍等。",

            "zh-hant" => @"等待插件功能說明 (Wait Plugin)
當用戶表達要等待、猶豫、或需要思考時（如 ""等一下我再告訴你""、""讓我想想""、""給我點時間""），你應該使用此插件來暫停並等待用戶的進一步回覆。

使用方式:
<|plugin_wait_begin|> seconds(30) <|plugin_wait_end|>

說明:
- 參數格式必須是 seconds(N)，例如 seconds(30) 表示等待30秒，seconds(60) 表示等待60秒
- 等待期間用戶若有新回覆，等待立即取消，你可以繼續處理用戶的新輸入
- 如果超時（用戶在指定秒數內未回覆），系統會主動喚起你，告知你用戶未在時間內回覆，屆時你可以禮貌地提醒用戶

這樣做的好處:
- 避免在用戶還沒想好時就重複回答同樣的問題
- 充分利用等待時間，不用在這裡傻等
- 用戶若改變主意或有新想法，可以立即被你處理

適用場景: 用戶猶豫、需要查資料、需要思考、或明確說要稍等。",

            "ja" => @"待機プラグイン機能説明 (Wait Plugin)
ユーザーが待機、躊躇、または考える必要があると表現した場合（例：「ちょっと待ってからまた言うよ」、「ちょっと考えさせて」、「少し時間をくれ」）、このプラグインを使用してユーザーからのさらなる返信を待つべきです。

使用方法:
<|plugin_wait_begin|> seconds(30) <|plugin_wait_end|>

説明:
- パラメータ形式は seconds(N) である必要があります。例: seconds(30) は30秒待つ、seconds(60) は60秒待つ
- 待機中にユーザーが新しい返信をした場合、待機は即座にキャンセルされ、ユーザーの新しい入力を処理できます
- タイムアウト（指定時間内にユーザーが返信しない）した場合、システムはあなたを自動的に起動し、ユーザーが時間内に返信しなかったことを通知します。その時点で、ユーザーに丁寧にリマインダーを送ることができます

このように行うことの利点:
- ユーザーがまだ考えていないときに、同じ質問を繰り返すのを避ける
- 待機時間を十分に活用する
- ユーザーが考え直したり新しい考えがあれば、すぐに対応できます

適用シーン: ユーザーが躊躇している、資料を調べる必要がある、考える必要がある、または明確に待つと言った場合。",

            _ => @"Wait Plugin Usage Guide:
When the user indicates they need to wait, hesitate, or think (e.g., 'wait a moment', 'let me think', 'give me some time'), you should use this plugin to pause and wait for further user input.

Usage:
<|plugin_wait_begin|> seconds(30) <|plugin_wait_end|>

Notes:
- Parameter format MUST be seconds(N), e.g. seconds(30) for 30 seconds, seconds(60) for 60 seconds
- If the user replies while waiting, the wait is immediately cancelled and you can handle their new input
- If timeout occurs (user doesn't reply within the specified time), the system will automatically re-invoke you to notify you of the timeout, so you can politely remind the user

Benefits:
- Avoid repeating the same answer when the user hasn't decided yet
- Don't waste time waiting here; use it productively
- Respond immediately if the user changes their mind or has new input

Suitable scenarios: User hesitation, needs to research, needs to think, or explicitly asks to wait."
        };
    }

    public void Unload()
    {
        _cts?.Cancel();
        VPetLLM.Utils.System.Logger.Log("Wait Plugin Unloaded!");
    }

    public void Log(string message)
    {
        if (_vpetLLM is null) return;
        _vpetLLM.Log(message);
    }
}

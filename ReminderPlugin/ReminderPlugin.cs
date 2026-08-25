using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

public class ReminderPlugin : IActionPlugin, IToolSchemaPlugin
{
    public string Name => "reminder";
    public string Author => "ycxom";
    public string Description
    {
        get
        {
            if (_vpetLLM is null) return "设置一个定时提醒（延迟执行）。event参数只填简短事件描述。如果用户要求「到时间后再做某事」，不要在本次回复中调用其他插件执行该事，等提醒触发后系统会通知你，届时再执行。";
            switch (_vpetLLM.Settings.Language)
            {
                case "ja":
                    return "タイマーリマインダーを設定します（遅延実行）。eventパラメータには短い説明のみ記入。「時間になったら何かをする」場合、今回の返信でそのアクションを実行せず、リマインダー通知後に実行してください。";
                case "zh-hans":
                    return "设置一个定时提醒（延迟执行）。event参数只填简短事件描述。如果用户要求「到时间后再做某事」，不要在本次回复中调用其他插件执行该事，等提醒触发后系统会通知你，届时再执行。";
                case "zh-hant":
                    return "設置一個定時提醒（延遲執行）。event參數只填簡短事件描述。如果用戶要求「到時間後再做某事」，不要在本次回覆中調用其他插件執行該事，等提醒觸發後系統會通知你，届時再執行。";
                case "en":
                default:
                    return "Set a timed reminder (deferred). The event parameter should be a brief description only. If the user asks to do something AFTER the timer, do NOT execute that action in this response. Wait for the reminder notification, then act.";
            }
        }
    }
    public string Examples => "Example: `<|plugin_reminder_begin|> time(10), unit(minutes), event(\"study\") <|plugin_reminder_end|>` Note: event is a short label, NOT a full sentence. When reminder fires, system sends you \"reminder_finished, Task: ...\", then you should respond and execute any deferred actions.";

    public string Parameters => "time(int), unit(string, optional: seconds/minutes), event(string, brief description like \"study\" or \"open browser\")";
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        VPetLLM.Utils.System.Logger.Log("Reminder Plugin Initialized!");
    }

    /// <summary>
    /// 调用契约。这里有个容易踩的点：解析 event 的正则是 <c>event\(""(.*?)""\)</c>，
    /// 也就是**值必须带一对英文双引号**（<c>event("study")</c>），漏了引号会直接匹配失败。
    /// 示例里必须原样体现，否则模型写出 <c>event(study)</c> 就静默失效。
    /// </summary>
    public ToolSchema? GetToolSchema()
    {
        var lang = _vpetLLM?.Settings.Language ?? "en";

        var (summary, form, timeDesc, unitDesc, eventDesc, remarks) = lang switch
        {
            "zh-hans" => (
                "设置定时提醒（延迟执行）",
                "在指定时长之后提醒你自己去做某事",
                "延迟的时长，正整数",
                "时间单位",
                "简短的事件标签，注意值要用英文双引号包住",
                "如果用户要求「到时间后再做某事」，本次回复不要调用其它插件去做那件事；提醒触发时系统会发来 reminder_finished，届时再执行。"),
            "zh-hant" => (
                "設置定時提醒（延遲執行）",
                "在指定時長之後提醒你自己去做某事",
                "延遲的時長，正整數",
                "時間單位",
                "簡短的事件標籤，注意值要用英文雙引號包住",
                "如果用戶要求「到時間後再做某事」，本次回覆不要調用其它插件去做那件事；提醒觸發時系統會發來 reminder_finished，屆時再執行。"),
            "ja" => (
                "タイマーリマインダーを設定する（遅延実行）",
                "指定した時間の経過後に自分自身へ通知する",
                "遅延時間（正の整数）",
                "時間の単位",
                "短いイベントラベル。値は半角二重引用符で囲むこと",
                "「時間になったら何かをする」と頼まれた場合、今回の返信ではそれを実行しないでください。リマインダー発火時に reminder_finished が届くので、そのときに実行します。"),
            _ => (
                "Set a timed reminder (deferred execution)",
                "Remind yourself to do something after a delay",
                "How long to wait, a positive integer",
                "Time unit",
                "A short event label; the value must be wrapped in double quotes",
                "If the user asks you to do something AFTER the timer, do not do it in this response. The system sends reminder_finished when it fires; act then.")
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
                        ToolParameter.Int("time", timeDesc, sample: "10"),
                        ToolParameter.Str("event", eventDesc, sample: "\"study\""),
                        ToolParameter.Choice("unit", unitDesc, new[] { "seconds", "minutes" },
                            required: false, @default: "minutes")
                    },
                    Example = "time(10), unit(minutes), event(\"study\")"
                }
            }
        };
    }

    public Task<string> Function(string arguments)
    {
        try
        {
            var timeMatch = new Regex(@"time\((\d+)\)").Match(arguments);
            var unitMatch = new Regex(@"unit\((\w+)\)").Match(arguments);
            var eventMatch = new Regex(@"event\(""(.*?)""\)").Match(arguments);

            if (!timeMatch.Success || !eventMatch.Success)
            {
                return Task.FromResult("创建提醒失败：缺少 'time' 或 'event' 参数。");
            }

            var timeValue = int.Parse(timeMatch.Groups[1].Value);
            var unit = unitMatch.Success ? unitMatch.Groups[1].Value.ToLower() : "seconds";
            var message = eventMatch.Groups[1].Value;

            TimeSpan delay;
            switch (unit)
            {
                case "minute":
                case "minutes":
                    delay = TimeSpan.FromMinutes(timeValue);
                    break;
                case "second":
                case "seconds":
                default:
                    delay = TimeSpan.FromSeconds(timeValue);
                    break;
            }

            _ = ReminderTask(delay, message);

            // 返回空字符串，不触发回灌给AI，避免AI误以为提醒已完成
            // 时间到时由 ReminderTask 通过 ChatCore.Chat() 主动通知AI
            return Task.FromResult("");
        }
        catch (Exception e)
        {
            return Task.FromResult($"创建提醒失败，请检查参数: {e.Message}");
        }
    }

    private async Task ReminderTask(TimeSpan delay, string message)
    {
        if (_vpetLLM is null) return;
        await Task.Delay(delay);

        var aiName = _vpetLLM.Settings.AiName;
        var notificationTitle = $"{aiName} 提醒你";
        var notificationMessage = $"该 “{message}” 了";

        // 先发送提醒完成消息给AI（异步，不等待弹窗关闭）
        var response = $"reminder_finished, Task: \"{message}\"";
        await _vpetLLM.ChatCore.Chat(response, true);

        // 在UI线程上显示通知弹窗（MessageBox会阻塞UI线程，但AI已收到消息）
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow is not null)
            {
                mainWindow.Activate();
                mainWindow.Topmost = true;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => mainWindow.Topmost = false);
                });
            }

            System.Windows.MessageBox.Show(notificationMessage, notificationTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        });
    }

    public void Invoke()
    {
    }

    public void Unload()
    {
        VPetLLM.Utils.System.Logger.Log("Reminder Plugin Unloaded!");
    }

    public void Log(string message)
    {
        if (_vpetLLM is null) return;
        _vpetLLM.Log(message);
    }
}

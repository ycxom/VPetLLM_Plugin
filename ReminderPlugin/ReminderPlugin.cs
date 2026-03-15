using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;

public class ReminderPlugin : IActionPlugin
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

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class ReminderPlugin : IActionPlugin
{
    public string Name => "reminder";
    public string Description => "设置一个定时提醒。";
    public string Examples => "Example: `[:plugin(reminder(time(10), unit(minutes), event(\"study\")))]`";
    public string Parameters => "time(int), unit(string, optional: seconds/minutes), event(string)";
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        FilePath = plugin.PluginPath;
        VPetLLM.Utils.Logger.Log("Reminder Plugin Initialized!");
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

            return Task.FromResult($"定时任务设置成功，{timeValue} {unit} 后提醒你告知用户 '{message}'");
        }
        catch (Exception e)
        {
            return Task.FromResult($"创建提醒失败，请检查参数: {e.Message}");
        }
    }

    private async Task ReminderTask(TimeSpan delay, string message)
    {
        if (_vpetLLM == null) return;
        await Task.Delay(delay);

        // 使用Dispatcher在UI线程上显示提醒
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            var response = $"reminder_finished, Task: \"{message}\"";
            await _vpetLLM.ChatCore.Chat(response, true);
        });
    }

    public void Invoke()
    {
    }

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("Reminder Plugin Unloaded!");
    }

    public void Log(string message)
    {
        if (_vpetLLM == null) return;
        _vpetLLM.Log(message);
    }
}
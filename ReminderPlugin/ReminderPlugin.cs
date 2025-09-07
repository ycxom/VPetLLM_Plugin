using System;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class ReminderPlugin : IActionPlugin
{
    public string Name => "reminder";
    public string Description => "设置一个定时提醒。";
    public string Parameters => "{\"time_in_minutes\": \"提醒的分钟数\", \"message\": \"提醒的内容\"}";
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
            var args = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(arguments);
            if (args == null)
            {
                return Task.FromResult("创建提醒失败：无效的参数。");
            }
            int minutes = args.time_in_minutes;
            string message = args.message;

            // 启动一个后台任务来处理延迟和提醒，不要 await 它
            _ = ReminderTask(minutes, message);

            return Task.FromResult($"好的，我会在 {minutes} 分钟后提醒你: {message}");
        }
        catch (Exception e)
        {
            return Task.FromResult($"创建提醒失败，请检查参数: {e.Message}");
        }
    }

    private async Task ReminderTask(int minutes, string message)
    {
        if (_vpetLLM == null) return;
        await Task.Delay(TimeSpan.FromMinutes(minutes));

        // 使用Dispatcher在UI线程上显示提醒
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _vpetLLM.MW.Main.Say($"叮咚！提醒时间到啦：{message}");
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
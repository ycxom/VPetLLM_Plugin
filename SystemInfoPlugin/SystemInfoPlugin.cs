using System;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class SystemInfoPlugin : IActionPlugin
{
    public string Name => "system_info";
    public string Description => "获取当前操作系统的版本信息。";
    public string Parameters => "{}"; // 这个插件不需要任何参数
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; }

    private VPetLLM.VPetLLM _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        VPetLLM.Utils.Logger.Log("System Info Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        var osInfo = Environment.OSVersion.VersionString;
        _vpetLLM.Log($"SystemInfoPlugin: Function called. Returning OSVersion: {osInfo}");
        return Task.FromResult(osInfo);
    }

    public void Invoke()
    {
        // 这个方法现在可以被保留，或者在未来用于无参数的、手动的调用
    }

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("System Info Plugin Unloaded!");
    }

    public void Log(string message)
    {
        _vpetLLM.Log(message);
    }
}
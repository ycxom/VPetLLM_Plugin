using System;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class SystemInfoPlugin : IActionPlugin
{
    public string Name => "system_info";
    public string Description => "获取当前操作系统的版本信息。";  // 显示在 插件 列表介绍，也用于告知ai 这是个什么工具
    public string Parameters => ""; // 不用传参留空
    public string Examples => "Example: `[:plugin(system_info())]`";   //  给ai一个示例，用于帮助ai调用
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        FilePath = plugin.PluginPath;
        VPetLLM.Utils.Logger.Log("System Info Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        var osInfo = Environment.OSVersion.VersionString;
        if (_vpetLLM == null) return Task.FromResult("VPetLLM instance is not initialized.");
        _vpetLLM.Log($"SystemInfoPlugin: Function called. Returning OSVersion: {osInfo}");
        return Task.FromResult(osInfo);
    }


    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("System Info Plugin Unloaded!");
    }

    public void Log(string message)
    {
        if (_vpetLLM == null) return;
        _vpetLLM.Log(message);
    }
}
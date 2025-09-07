using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class ExamplePlugin : IActionPlugin
{
    public string Name => "example_plugin";
    public string Description => "一个简单的示例插件，当被调用时，它会返回一段固定的问候语。";
    public string Parameters => "{}"; // 这个插件不需要任何参数
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        FilePath = plugin.PluginPath;
        VPetLLM.Utils.Logger.Log("Example Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        if (_vpetLLM == null) return Task.FromResult("VPetLLM instance is not initialized.");
        var result = "你好，我是示例插件！";
        _vpetLLM.Log($"ExamplePlugin: Function called. Returning: {result}");
        return Task.FromResult(result);
    }

    public void Invoke()
    {
        // 这个方法现在可以被保留，或者在未来用于无参数的、手动的调用
    }

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("Example Plugin Unloaded!");
    }

    public void Log(string message)
    {
        if (_vpetLLM == null) return;
        _vpetLLM.Log(message);
    }
}
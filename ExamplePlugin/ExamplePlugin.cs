using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class ExamplePlugin : IActionPlugin
{
    public string Name => "example_plugin";
    public string Description => "A simple example plugin that returns a fixed greeting when called.";
    public string Parameters => ""; // This plugin does not require any parameters.
    public string Examples => "Example: `[:plugin(example_plugin())]`";
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
        var result = "Hello, I am an example plugin!";
        _vpetLLM.Log($"ExamplePlugin: Function called. Returning: {result}");
        return Task.FromResult(result);
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
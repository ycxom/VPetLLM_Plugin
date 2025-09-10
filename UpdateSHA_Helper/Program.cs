using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: UpdateSHA_Helper.exe <PluginId> <DllPath> <PluginListPath>");
    return 1;
}

var pluginId = args[0];
var dllPath = args[1];
var pluginListPath = args[2];

if (!File.Exists(dllPath))
{
    Console.Error.WriteLine($"Error: DLL file not found at '{dllPath}'");
    return 1;
}

if (!File.Exists(pluginListPath))
{
    Console.Error.WriteLine($"Error: PluginList.json not found at '{pluginListPath}'");
    return 1;
}

try
{
    // 计算哈希
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(dllPath);
    var hash = sha256.ComputeHash(stream);
    var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

    // 使用能处理BOM的UTF8编码读取
    var jsonString = File.ReadAllText(pluginListPath, Encoding.UTF8);
    var jsonObj = JObject.Parse(jsonString);

    if (jsonObj[pluginId] is JObject pluginEntry)
    {
        var oldHash = pluginEntry["SHA256"]?.ToString() ?? "N/A";
        pluginEntry["SHA256"] = hashString;

        // 使用保留格式的Indented方式写回
        var newJsonString = jsonObj.ToString(Formatting.Indented);
        
        // 使用能处理BOM的UTF8编码写回
        File.WriteAllText(pluginListPath, newJsonString, Encoding.UTF8);
        
        Console.WriteLine($"Successfully updated SHA256 for {pluginId} from {oldHash} to {hashString}");
        return 0;
    }
    else
    {
        Console.Error.WriteLine($"Error: PluginId '{pluginId}' not found in '{pluginListPath}'");
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An unexpected error occurred: {ex.Message}");
    return 1;
}
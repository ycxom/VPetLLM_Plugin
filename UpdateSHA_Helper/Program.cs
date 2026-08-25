using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: UpdateSHA_Helper.exe <PluginId> <DllPath> <PluginListPath> [extraFile...]");
    return 1;
}

var pluginId = args[0];
var dllPath = args[1];
var pluginListPath = args[2];
var extraPaths = args.Skip(3).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

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

static string Sha256Of(string path)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(path);
    return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
}

/// 把主文件 URL 的最后一段换成 fileName，推出附属文件的下载地址。
static string DeriveUrl(string mainUrl, string fileName)
{
    if (string.IsNullOrEmpty(mainUrl)) return fileName;
    var slash = mainUrl.LastIndexOf('/');
    return slash < 0 ? fileName : mainUrl.Substring(0, slash + 1) + fileName;
}

try
{
    var hashString = Sha256Of(dllPath);

    // 使用能处理BOM的UTF8编码读取
    var jsonString = File.ReadAllText(pluginListPath, Encoding.UTF8);
    var jsonObj = JObject.Parse(jsonString);

    if (jsonObj[pluginId] is not JObject pluginEntry)
    {
        Console.Error.WriteLine($"Error: PluginId '{pluginId}' not found in '{pluginListPath}'");
        return 1;
    }

    var oldHash = pluginEntry["SHA256"]?.ToString() ?? "N/A";
    pluginEntry["SHA256"] = hashString;
    Console.WriteLine($"Successfully updated SHA256 for {pluginId} from {oldHash} to {hashString}");

    // ---- 附属文件（如 TerminalPlugin.rules.json）----
    //
    // 宿主按 Extra 里的每一项独立下载并校验哈希。这里只更新哈希、不覆盖已有的 File，
    // 这样手工填过特殊下载地址的条目不会被构建覆盖掉。
    if (extraPaths.Length > 0)
    {
        var mainUrl = pluginEntry["File"]?.ToString() ?? "";
        var extraArray = pluginEntry["Extra"] as JArray ?? new JArray();

        foreach (var extraPath in extraPaths)
        {
            if (!File.Exists(extraPath))
            {
                Console.Error.WriteLine($"Warning: extra file not found, skipped: '{extraPath}'");
                continue;
            }

            var fileName = Path.GetFileName(extraPath);
            var extraHash = Sha256Of(extraPath);

            var existing = extraArray
                .OfType<JObject>()
                .FirstOrDefault(e => string.Equals(e["Name"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                extraArray.Add(new JObject
                {
                    ["Name"] = fileName,
                    ["File"] = DeriveUrl(mainUrl, fileName),
                    ["SHA256"] = extraHash
                });
                Console.WriteLine($"Added extra file '{fileName}' with SHA256 {extraHash}");
            }
            else
            {
                var oldExtraHash = existing["SHA256"]?.ToString() ?? "N/A";
                existing["SHA256"] = extraHash;
                if (string.IsNullOrEmpty(existing["File"]?.ToString()))
                {
                    existing["File"] = DeriveUrl(mainUrl, fileName);
                }
                Console.WriteLine($"Updated extra file '{fileName}' SHA256 from {oldExtraHash} to {extraHash}");
            }
        }

        if (extraArray.Count > 0)
        {
            pluginEntry["Extra"] = extraArray;
        }
    }

    // 使用保留格式的Indented方式写回
    var newJsonString = jsonObj.ToString(Formatting.Indented);

    // 使用能处理BOM的UTF8编码写回
    File.WriteAllText(pluginListPath, newJsonString, Encoding.UTF8);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An unexpected error occurred: {ex.Message}");
    return 1;
}

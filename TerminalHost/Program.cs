using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VPetLLM.TerminalHost;

// stdin 收一个 JSON 请求，stdout 回一个 JSON 应答，然后退出。
// 全程不碰控制台的编码设置，直接按 UTF-8 读写标准流——宿主那边也按 UTF-8 解。

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
};

HostResponse response;

try
{
    string payload;
    using (var stdin = Console.OpenStandardInput())
    using (var reader = new StreamReader(stdin, new UTF8Encoding(false)))
    {
        payload = await reader.ReadToEndAsync();
    }

    if (string.IsNullOrWhiteSpace(payload))
    {
        response = HostResponse.Fail("empty request");
    }
    else
    {
        var request = JsonSerializer.Deserialize<HostRequest>(payload, jsonOptions)
                      ?? throw new InvalidOperationException("request deserialized to null");

        WatchParent(request.ParentPid);

        var shell = ShellResolver.Resolve(request.Shell);

        response = request.Op?.ToLowerInvariant() switch
        {
            "probe" => new HostResponse
            {
                Ok = true,
                HasPwsh = shell.HasPwsh,
                HasPowerShell = shell.HasPowerShell,
                SelectedShell = shell.Type.ToString(),
                ShellPath = shell.Path
            },
            "exec" or null or "" => string.IsNullOrWhiteSpace(request.Command)
                ? HostResponse.Fail("empty command")
                : await CommandRunner.RunAsync(request, shell),
            _ => HostResponse.Fail($"unknown op: {request.Op}")
        };
    }
}
catch (Exception ex)
{
    response = HostResponse.Fail($"{ex.GetType().Name}: {ex.Message}");
}

using (var stdout = Console.OpenStandardOutput())
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(response, jsonOptions);
    stdout.Write(bytes, 0, bytes.Length);
    stdout.Flush();
}

return response.Ok ? 0 : 1;


/// <summary>
/// 宿主进程一死就自杀。
///
/// 单次调用模式下本进程活不过一条命令的超时时间，但 VPet 若在命令执行到一半时崩溃，
/// 这里能让整棵进程树立刻被 job 回收，而不是干等到超时。
/// </summary>
static void WatchParent(int parentPid)
{
    if (parentPid <= 0) return;

    try
    {
        var parent = Process.GetProcessById(parentPid);
        parent.EnableRaisingEvents = true;
        parent.Exited += (_, _) => Environment.Exit(137);
        if (parent.HasExited) Environment.Exit(137);
    }
    catch
    {
        // 拿不到宿主进程（已退出或权限不足）就不看门，交给超时兜底
    }
}

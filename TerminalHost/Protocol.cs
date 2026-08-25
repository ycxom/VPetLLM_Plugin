namespace VPetLLM.TerminalHost;

/// <summary>
/// 宿主插件发过来的一次请求。整个协议是"一次调用一个进程"：
/// stdin 收一个 JSON，stdout 回一个 JSON，然后退出。
///
/// 没有常驻进程、没有请求 id、没有重连逻辑——命令由 AI 触发，频率极低，
/// 一次进程启动几十毫秒完全够用，换来的是零状态、零生命周期 bug。
/// </summary>
public sealed class HostRequest
{
    /// <summary>"exec" 执行命令 / "probe" 只探测可用的 shell。</summary>
    public string Op { get; set; } = "exec";

    public string Command { get; set; } = "";

    /// <summary>工作目录；不存在时回退到用户主目录。</summary>
    public string? Cwd { get; set; }

    /// <summary>auto / pwsh / powershell / cmd。</summary>
    public string Shell { get; set; } = "auto";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>摘掉名字里带 KEY/SECRET/TOKEN/PASSWORD 的环境变量。</summary>
    public bool FilterSecrets { get; set; } = true;

    /// <summary>回传命令执行后的 $PWD，让 cd 能跨调用保持。</summary>
    public bool PersistCwd { get; set; } = true;

    /// <summary>宿主进程 PID；宿主一死本进程立即自杀，避免留下孤儿。</summary>
    public int ParentPid { get; set; }
}

/// <summary>一次请求的应答。</summary>
public sealed class HostResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }

    // ---- op = exec ----
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }

    /// <summary>命令执行完之后的工作目录；没能回传时为 null，调用方保留原值。</summary>
    public string? Cwd { get; set; }

    // ---- op = probe（exec 也会一并带上，省一次往返）----
    public bool HasPwsh { get; set; }
    public bool HasPowerShell { get; set; }

    /// <summary>本次实际使用/选中的 shell：Pwsh / PowerShell / Cmd。</summary>
    public string SelectedShell { get; set; } = "";

    public string ShellPath { get; set; } = "";

    public static HostResponse Fail(string error) => new() { Ok = false, Error = error };
}

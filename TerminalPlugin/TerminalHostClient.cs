using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalPlugin
{
    /// <summary>发给执行进程的一次请求。字段与 TerminalHost 侧的 HostRequest 一一对应。</summary>
    internal sealed class HostRequest
    {
        public string Op { get; set; } = "exec";
        public string Command { get; set; } = "";
        public string? Cwd { get; set; }
        public string Shell { get; set; } = "auto";
        public int TimeoutSeconds { get; set; } = 30;
        public bool FilterSecrets { get; set; } = true;
        public bool PersistCwd { get; set; } = true;
        public int ParentPid { get; set; }
    }

    /// <summary>执行进程的应答。</summary>
    internal sealed class HostResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string Stdout { get; set; } = "";
        public string Stderr { get; set; } = "";
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public string? Cwd { get; set; }
        public bool HasPwsh { get; set; }
        public bool HasPowerShell { get; set; }
        public string SelectedShell { get; set; } = "";
        public string ShellPath { get; set; } = "";
    }

    /// <summary>
    /// 调用外部执行进程 <c>VPetLLM.TerminalHost.exe</c>。
    ///
    /// 命令不再由 VPet 进程直接交给 shell —— 那条"桌宠隐藏窗口拉起 cmd.exe 并回收
    /// 三条流"的链是行为引擎权重最高的特征之一。现在 VPet 只启动一个名字写明用途的
    /// 工具进程，shell 相关的一切（进程创建、Job Object、cmd/powershell 的参数拼装）
    /// 都发生在那边。
    ///
    /// 采用"一次调用一个进程"：stdin 递一个 JSON，stdout 收一个 JSON，进程随即退出。
    /// 命令由 AI 触发、频率极低，几十毫秒的进程启动完全可接受，换来的是零常驻进程、
    /// 零协议状态、零重连逻辑。
    /// </summary>
    internal static class TerminalHostClient
    {
        public const string ExecutableName = "VPetLLM.TerminalHost.exe";

        /// <summary>执行进程超时之外额外给的余量，够它把结果序列化回来。</summary>
        private const int GraceMs = 5000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>根据插件 DLL 的原始路径推出执行进程的位置（与 DLL 同目录）。</summary>
        public static string ResolvePath(string pluginFilePath)
        {
            var dir = string.IsNullOrEmpty(pluginFilePath)
                ? AppContext.BaseDirectory
                : (Path.GetDirectoryName(pluginFilePath) ?? AppContext.BaseDirectory);
            return Path.Combine(dir, ExecutableName);
        }

        /// <summary>
        /// 跑一次执行进程。失败时返回 <c>Ok = false</c> 并带上原因，调用方负责呈现，
        /// 不抛异常。
        /// </summary>
        public static async Task<HostResponse> InvokeAsync(string hostPath, HostRequest request)
        {
            if (!File.Exists(hostPath))
            {
                return new HostResponse { Ok = false, Error = $"执行进程不存在: {hostPath}" };
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory
            };

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var payload = JsonSerializer.Serialize(request, JsonOptions);
                await process.StandardInput.WriteAsync(payload);
                process.StandardInput.Close();

                // 先挂上两个读取任务再等退出，避免管道写满把子进程堵死
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                // 执行进程自己会按 TimeoutSeconds 掐命令；这里的硬超时只是兜底，
                // 防止它本身卡死（例如序列化阶段出问题）导致我们无限等待。
                var hardTimeoutMs = Math.Max(request.TimeoutSeconds, 1) * 1000 + GraceMs;
                using var cts = new CancellationTokenSource(hardTimeoutMs);

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }
                    return new HostResponse { Ok = false, Error = "执行进程无响应，已强制终止" };
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    var detail = string.IsNullOrWhiteSpace(stderr) ? $"exit {process.ExitCode}" : stderr.Trim();
                    return new HostResponse { Ok = false, Error = $"执行进程没有返回结果（{detail}）" };
                }

                var response = JsonSerializer.Deserialize<HostResponse>(stdout, JsonOptions);
                return response ?? new HostResponse { Ok = false, Error = "执行进程返回了无法解析的结果" };
            }
            catch (Exception ex)
            {
                return new HostResponse { Ok = false, Error = $"{ex.GetType().Name}: {ex.Message}" };
            }
        }
    }
}

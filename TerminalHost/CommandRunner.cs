using System.Diagnostics;
using System.Text;

namespace VPetLLM.TerminalHost;

/// <summary>
/// 真正把命令交给 shell 的地方。
///
/// 这段代码原本长在 TerminalPlugin 里，也就是长在 VPet 进程里——
/// "桌宠游戏隐藏窗口拉起 cmd.exe 并回收三条流"是行为引擎最看重的那条链。
/// 搬到本进程后，VPet 只会启动一个名字写着 Terminal Host 的工具进程，
/// 行为画像干净了；这条链仍然存在，但落在一个用途可解释的程序上。
/// </summary>
public static class CommandRunner
{
    private static readonly string[] SecretEnvFragments =
        { "KEY", "SECRET", "TOKEN", "PASSWORD", "PASSWD", "CREDENTIAL", "APIKEY" };

    /// <summary>杀掉进程后等待输出管道排空的时间。孙进程可能仍持有管道，不能无限等。</summary>
    private const int IoDrainTimeoutMs = 2000;

    /// <summary>约定的超时退出码，与 codex 保持一致。</summary>
    private const int TimeoutExitCode = 124;

    public static async Task<HostResponse> RunAsync(HostRequest request, ShellSelection shell)
    {
        var workingDirectory = !string.IsNullOrEmpty(request.Cwd) && Directory.Exists(request.Cwd)
            ? request.Cwd!
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // cd 的结果通过临时文件回传（PowerShell 才支持，见 BuildPowerShellScript）
        var cwdCapturePath = (request.PersistCwd && shell.Type != ShellType.Cmd)
            ? Path.Combine(Path.GetTempPath(), $"vpet_terminal_cwd_{Guid.NewGuid():N}.txt")
            : null;

        try
        {
            var startInfo = BuildStartInfo(request, shell, workingDirectory, cwdCapturePath);

            using var job = JobObject.Create();
            using var process = new Process { StartInfo = startInfo };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();

            // 进程一起来立刻收进 job：句柄释放时整棵树必被回收，
            // 哪怕本进程崩溃也不会留下孤儿进程。
            job?.Assign(process);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 关掉 stdin：命令一旦想交互（pause / Read-Host / 各种登录提示）
            // 会立刻拿到 EOF 而不是挂到超时。
            try { process.StandardInput.Close(); } catch { }

            var timedOut = false;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds)))
            {
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    try { process.Kill(true); } catch { }

                    // 孙进程可能还握着输出管道，不能无限等排空
                    try
                    {
                        using var drainCts = new CancellationTokenSource(IoDrainTimeoutMs);
                        await process.WaitForExitAsync(drainCts.Token);
                    }
                    catch { }
                }
            }

            var exitCode = TimeoutExitCode;
            if (!timedOut)
            {
                try { exitCode = process.ExitCode; } catch { exitCode = -1; }
            }

            return new HostResponse
            {
                Ok = true,
                Stdout = outputBuilder.ToString().Trim(),
                Stderr = errorBuilder.ToString().Trim(),
                ExitCode = exitCode,
                TimedOut = timedOut,
                Cwd = ReadCapturedWorkingDirectory(cwdCapturePath),
                HasPwsh = shell.HasPwsh,
                HasPowerShell = shell.HasPowerShell,
                SelectedShell = shell.Type.ToString(),
                ShellPath = shell.Path
            };
        }
        finally
        {
            if (cwdCapturePath is not null)
            {
                try { if (File.Exists(cwdCapturePath)) File.Delete(cwdCapturePath); } catch { }
            }
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        HostRequest request, ShellSelection shell, string workingDirectory, string? cwdCapturePath)
    {
        var outputEncoding = shell.Type == ShellType.Cmd ? GetCmdOutputEncoding() : Encoding.UTF8;

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
            WorkingDirectory = workingDirectory
        };

        if (shell.Type == ShellType.Cmd)
        {
            startInfo.FileName = string.IsNullOrEmpty(shell.Path) ? "cmd.exe" : shell.Path;

            // cmd.exe 不遵循 CRT 的参数拆分规则，ArgumentList 那套转义在这里是错的，
            // 只能自己拼原始命令行。/d 跳过 AutoRun 注册表项，/s 让 cmd 只剥掉首尾各一个
            // 引号、中间原样保留（这是唯一可靠的传法）。
            //
            // 这里刻意不再拼 `chcp 65001`：实测 cmd 的内建命令（echo/dir/type）无论
            // chcp 设成什么都按 OEM 代码页写字节，加了 chcp 反而让 UTF-8 解码把
            // `dir` 的中文文件名读成乱码。改成"不动代码页 + 按 OEM 解码"，
            // 内建命令和普通外部程序就都对了（见 GetCmdOutputEncoding）。
            startInfo.Arguments = $"/d /s /c \"{request.Command}\"";
        }
        else
        {
            startInfo.FileName = shell.Path;

            // 脚本作为独立的一个参数交给 -Command，由 .NET 负责引号处理。
            // 不再走 `Invoke-Expression '...'` 那条路：那里既要给外层 -Command 转义双引号、
            // 又要给内层单引号字符串转义单引号，还要被 Invoke-Expression 二次解析，
            // 同时含两种引号或跨行的命令必坏。做法取自 codex 的 shell-command/src/powershell.rs。
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(BuildPowerShellScript(request.Command, cwdCapturePath));
        }

        if (request.FilterSecrets)
        {
            FilterSecretEnvironment(startInfo);
        }

        return startInfo;
    }

    private static Encoding? _cmdOutputEncoding;

    /// <summary>
    /// CMD 输出该用哪个编码解码。
    ///
    /// cmd 的内建命令始终按系统 OEM 代码页（简中系统是 936）写字节，chcp 改不动它；
    /// 不带 chcp 时普通外部程序也用同一个代码页。所以"不动代码页 + 按 OEM 解码"
    /// 是覆盖面最大的组合。拿不到该代码页时退回 UTF-8。
    /// </summary>
    private static Encoding GetCmdOutputEncoding()
    {
        if (_cmdOutputEncoding is not null) return _cmdOutputEncoding;

        try
        {
            // GBK/Big5 等代码页不在 .NET 默认编码表里，需要先注册
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            _cmdOutputEncoding = Encoding.GetEncoding(
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            _cmdOutputEncoding = Encoding.UTF8;
        }

        return _cmdOutputEncoding;
    }

    /// <summary>
    /// 拼出交给 <c>-Command</c> 的脚本体：UTF-8 前缀 + 用户命令 + 可选的 cd 回传尾巴。
    /// </summary>
    private static string BuildPowerShellScript(string command, string? cwdCapturePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("try { [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $OutputEncoding=[System.Text.Encoding]::UTF8 } catch {}");
        sb.AppendLine(command);

        if (cwdCapturePath is not null)
        {
            // 先把退出码固定下来 —— 后面还要执行写文件语句，不定住的话
            // 进程退出码会变成写文件那一步的结果。
            sb.AppendLine("$__vpetExit = if ($?) { if ($LASTEXITCODE) { $LASTEXITCODE } else { 0 } } else { if ($LASTEXITCODE) { $LASTEXITCODE } else { 1 } }");
            var literal = cwdCapturePath.Replace("'", "''");
            sb.AppendLine($"try {{ [System.IO.File]::WriteAllText('{literal}', $PWD.Path, [System.Text.Encoding]::UTF8) }} catch {{}}");
            sb.AppendLine("exit $__vpetExit");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把名字里带 KEY/SECRET/TOKEN/PASSWORD 的环境变量从子进程环境里摘掉。
    ///
    /// AI 跑的命令一句 <c>Get-ChildItem env:</c> 就能把宿主进程的全部凭据读走并回传给模型，
    /// 这个过滤是最低成本的止血。规则取自 codex 的 protocol/src/shell_environment.rs。
    /// </summary>
    private static void FilterSecretEnvironment(ProcessStartInfo startInfo)
    {
        var toRemove = startInfo.Environment.Keys
            .Where(k => SecretEnvFragments.Any(f => k.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();

        foreach (var key in toRemove)
        {
            startInfo.Environment.Remove(key);
        }
    }

    private static string? ReadCapturedWorkingDirectory(string? capturePath)
    {
        if (capturePath is null) return null;

        try
        {
            if (!File.Exists(capturePath)) return null;   // 命令中途终止，调用方保留原目录
            var captured = File.ReadAllText(capturePath, Encoding.UTF8).Trim();
            return !string.IsNullOrEmpty(captured) && Directory.Exists(captured) ? captured : null;
        }
        catch
        {
            return null;
        }
    }
}

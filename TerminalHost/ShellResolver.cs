namespace VPetLLM.TerminalHost;

public enum ShellType
{
    Cmd,
    PowerShell,
    Pwsh
}

public sealed record ShellSelection(ShellType Type, string Path, bool HasPwsh, bool HasPowerShell);

/// <summary>
/// 找出机器上可用的 shell 并按偏好选一个。
///
/// 这部分连同所有 <c>*.exe</c> 名字一起从插件搬到了本进程：宿主 DLL 里不再出现
/// cmd.exe / powershell.exe / pwsh.exe 这些字面量，也不再由桌宠进程直接拉起 shell。
/// </summary>
public static class ShellResolver
{
    public static ShellSelection Resolve(string? preferred)
    {
        var pwshPath = FindExecutableInPath("pwsh.exe");
        var hasPwsh = !string.IsNullOrEmpty(pwshPath);

        var psPath = FindExecutableInPath("powershell.exe");
        var hasPowerShell = !string.IsNullOrEmpty(psPath);

        var cmdPath = FindExecutableInPath("cmd.exe") ?? "cmd.exe";

        // 用户显式指定的优先；指定的那个不可用时退回自动选择
        switch (preferred?.ToLowerInvariant())
        {
            case "pwsh" when hasPwsh:
                return new ShellSelection(ShellType.Pwsh, pwshPath!, hasPwsh, hasPowerShell);
            case "powershell" when hasPowerShell:
                return new ShellSelection(ShellType.PowerShell, psPath!, hasPwsh, hasPowerShell);
            case "cmd":
                return new ShellSelection(ShellType.Cmd, cmdPath, hasPwsh, hasPowerShell);
        }

        // 自动：pwsh > powershell > cmd
        if (hasPwsh) return new ShellSelection(ShellType.Pwsh, pwshPath!, hasPwsh, hasPowerShell);
        if (hasPowerShell) return new ShellSelection(ShellType.PowerShell, psPath!, hasPwsh, hasPowerShell);
        return new ShellSelection(ShellType.Cmd, cmdPath, hasPwsh, hasPowerShell);
    }

    /// <summary>先查已知固定路径（快），再查 PATH，最后按 PATHEXT 补扩展名。</summary>
    private static string? FindExecutableInPath(string executableName)
    {
        foreach (var path in GetKnownPaths(executableName))
        {
            if (File.Exists(path)) return path;
        }

        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var trimmedDir = dir.Trim();
                    if (string.IsNullOrEmpty(trimmedDir)) continue;

                    var fullPath = Path.Combine(trimmedDir, executableName);
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch { /* 忽略无效路径 */ }
            }
        }
        catch { /* PATH 读不到就算了 */ }

        if (!executableName.Contains('.'))
        {
            return FindExecutableWithExtensions(executableName);
        }

        return null;
    }

    private static string[] GetKnownPaths(string executableName)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return executableName.ToLowerInvariant() switch
        {
            "pwsh.exe" => new[]
            {
                Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
                Path.Combine(programFilesX86, "PowerShell", "7", "pwsh.exe"),
                Path.Combine(programFiles, "PowerShell", "7-preview", "pwsh.exe"),
                Path.Combine(userProfile, ".dotnet", "tools", "pwsh.exe"),
                Path.Combine(userProfile, "scoop", "shims", "pwsh.exe"),
                Path.Combine(programFiles, "PowerShell", "pwsh.exe"),
            },
            "powershell.exe" => new[]
            {
                Path.Combine(winDir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                Path.Combine(winDir, "SysWOW64", "WindowsPowerShell", "v1.0", "powershell.exe"),
            },
            "cmd.exe" => new[]
            {
                Path.Combine(winDir, "System32", "cmd.exe"),
                Path.Combine(winDir, "SysWOW64", "cmd.exe"),
            },
            _ => Array.Empty<string>()
        };
    }

    private static string? FindExecutableWithExtensions(string executableName)
    {
        try
        {
            var pathExtEnv = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
            var extensions = pathExtEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";

            foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var ext in extensions)
                {
                    try
                    {
                        var fullPath = Path.Combine(dir.Trim(), executableName + ext);
                        if (File.Exists(fullPath)) return fullPath;
                    }
                    catch { /* 忽略无效路径 */ }
                }
            }
        }
        catch { }

        return null;
    }
}

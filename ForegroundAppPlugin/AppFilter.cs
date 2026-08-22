using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ForegroundAppPlugin
{
    /// <summary>名单过滤模式。存进配置的是整数，别改已有取值。</summary>
    internal enum AppFilterMode
    {
        Off = 0,
        Whitelist = 1,
        Blacklist = 2
    }

    /// <summary>一次过滤的结论。区分原因是为了能在设置面板里说清「为什么这个应用没上报」。</summary>
    internal enum FilterDecision
    {
        Allow,
        BlockedByKeyword,
        BlockedByList
    }

    /// <summary>可供用户挑选的应用条目。标识域是进程名——和前台监视捕获到的是同一个东西。</summary>
    internal sealed class AppEntry
    {
        public string ProcessName = string.Empty;
        public string DisplayName = string.Empty;
        public bool IsRunning;

        public string Label => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == ProcessName
            ? ProcessName
            : $"{ProcessName}  ({DisplayName})";
    }

    /// <summary>
    /// 隐私过滤：决定某个前台应用能不能上报给 AI。
    ///
    /// 两道闸门，顺序固定：
    /// 1. 关键词——命中窗口标题或进程名就整条静默。这是隐私闸门，永远优先。
    /// 2. 黑/白名单——按进程名匹配。
    /// </summary>
    internal static class AppFilter
    {
        /// <summary>统一成不带 .exe 的小写形式，好让 "Chrome.exe" 和 "chrome" 互相匹配。</summary>
        public static string NormalizeProcessName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var normalized = name.Trim();
            if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);
            return normalized.ToLowerInvariant();
        }

        /// <summary>把用户在设置里填的一坨文本拆成条目。换行、逗号、中文逗号、分号都当分隔符。</summary>
        public static List<string> ParseList(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text
                .Split(new[] { '\n', '\r', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string FormatList(IEnumerable<string>? items)
            => items == null ? string.Empty : string.Join(Environment.NewLine, items);

        /// <summary>任一关键词出现在任一文本里（不分大小写）就算命中。</summary>
        public static bool MatchesAnyKeyword(IEnumerable<string>? keywords, params string?[] haystacks)
        {
            if (keywords == null) return false;
            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                var needle = keyword.Trim();
                foreach (var hay in haystacks)
                {
                    if (string.IsNullOrEmpty(hay)) continue;
                    if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        public static bool ListContains(IEnumerable<string>? list, string? processName)
        {
            if (list == null) return false;
            var target = NormalizeProcessName(processName);
            if (target.Length == 0) return false;
            return list.Any(x => NormalizeProcessName(x) == target);
        }

        /// <summary>前台应用是否可以上报。关键词闸门优先于名单。</summary>
        public static FilterDecision Evaluate(
            string? processName,
            string? windowTitle,
            IEnumerable<string>? privacyKeywords,
            AppFilterMode mode,
            IEnumerable<string>? filterList)
        {
            if (MatchesAnyKeyword(privacyKeywords, windowTitle, processName))
                return FilterDecision.BlockedByKeyword;

            switch (mode)
            {
                case AppFilterMode.Whitelist:
                    // 白名单为空时等于「全部拦下」——这是用户明确选了白名单模式却还没加东西，
                    // 宁可什么都不报，也不能默默按全放行处理。
                    return ListContains(filterList, processName) ? FilterDecision.Allow : FilterDecision.BlockedByList;
                case AppFilterMode.Blacklist:
                    return ListContains(filterList, processName) ? FilterDecision.BlockedByList : FilterDecision.Allow;
                default:
                    return FilterDecision.Allow;
            }
        }
    }

    /// <summary>
    /// 给设置面板凑一份可选应用清单，省得用户自己去查进程名。
    ///
    /// 取值方式参考 AppLauncherPlugin，但换了数据源：AppLauncher 扫开始菜单 .lnk 拿到的是
    /// **显示名**（"Visual Studio Code"），而前台监视比对的是**进程名**（"Code"），两者对不上。
    /// 所以这里取两个标识域一致的来源：正在运行的带窗口进程，以及注册表 App Paths 里登记的 exe。
    /// </summary>
    internal static class AppInventory
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_OWNER = 4;

        /// <summary>正在运行的 + 已安装的应用，按进程名去重，正在运行的排前面。</summary>
        public static List<AppEntry> GetApps()
        {
            var byProcess = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in GetRunningWindowedApps())
            {
                var key = AppFilter.NormalizeProcessName(entry.ProcessName);
                if (key.Length == 0) continue;
                byProcess[key] = entry;    // 运行中的信息更全（带真实窗口标题），优先留着
            }

            foreach (var entry in GetInstalledApps())
            {
                var key = AppFilter.NormalizeProcessName(entry.ProcessName);
                if (key.Length == 0 || byProcess.ContainsKey(key)) continue;
                byProcess[key] = entry;
            }

            return byProcess.Values
                .OrderByDescending(x => x.IsRunning)
                .ThenBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>枚举有可见顶层窗口的进程——也就是用户真能切到前台的那些。</summary>
        public static List<AppEntry> GetRunningWindowedApps()
        {
            var result = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                EnumWindows((hWnd, _) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hWnd)) return true;
                        // 有 owner 的多半是对话框/工具窗，不是应用主窗口
                        if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;

                        int length = GetWindowTextLength(hWnd);
                        if (length == 0) return true;

                        var sb = new StringBuilder(length + 1);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        var title = sb.ToString();
                        if (string.IsNullOrWhiteSpace(title)) return true;

                        GetWindowThreadProcessId(hWnd, out uint processId);
                        if (processId == 0) return true;

                        using var process = Process.GetProcessById((int)processId);
                        var name = process.ProcessName;
                        if (string.IsNullOrWhiteSpace(name)) return true;

                        var key = AppFilter.NormalizeProcessName(name);
                        if (!result.ContainsKey(key))
                        {
                            result[key] = new AppEntry
                            {
                                ProcessName = name,
                                DisplayName = title,
                                IsRunning = true
                            };
                        }
                    }
                    catch
                    {
                        // 单个窗口取不到就跳过：进程可能刚退出，或是权限不够的系统窗口
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                // 整体枚举失败就返回已经收集到的部分
            }

            return result.Values.ToList();
        }

        /// <summary>
        /// 注册表 App Paths 里登记的已安装程序。让用户能给「现在没开着」的应用先加好过滤，
        /// 比如银行、聊天软件——真等它出现在前台再去加就已经上报过一次了。
        /// </summary>
        public static List<AppEntry> GetInstalledApps()
        {
            var result = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var roots = new (RegistryKey Hive, string Path)[]
            {
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"),
                (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            };

            foreach (var (hive, path) in roots)
            {
                try
                {
                    using var key = hive.OpenSubKey(path);
                    if (key == null) continue;

                    foreach (var name in key.GetSubKeyNames())
                    {
                        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                        var processName = name.Substring(0, name.Length - 4);
                        if (processName.Length == 0 || !seen.Add(processName)) continue;

                        result.Add(new AppEntry
                        {
                            ProcessName = processName,
                            DisplayName = string.Empty,
                            IsRunning = false
                        });
                    }
                }
                catch
                {
                    // 某个注册表根读不到就跳过，不影响其他来源
                }
            }

            return result;
        }
    }
}

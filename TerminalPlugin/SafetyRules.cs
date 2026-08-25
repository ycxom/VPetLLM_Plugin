using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TerminalPlugin
{
    /// <summary>
    /// 一条规则的命中条件。所有字段都为空时表示"命令名匹配即命中"。
    ///
    /// 多个条件同时给出时是「或」的关系——原本 switch 里的 <c>when</c> 子句本身就是
    /// 若干 <c>Any(...)</c> 的或组合，这里保持一致。
    /// </summary>
    public sealed class RuleCondition
    {
        /// <summary>参数中存在与列表中任一项完全相等的 token（忽略大小写）。</summary>
        public List<string>? HasWord { get; set; }

        /// <summary>CMD 风格开关 <c>/s</c> <c>/q</c>，也接受合写的 <c>/sq</c>。</summary>
        public List<string>? CmdFlag { get; set; }

        /// <summary>PowerShell 开关参数，支持任意长度的无歧义缩写。</summary>
        public List<string>? PsSwitch { get; set; }

        /// <summary>任一参数以列表中某项开头（忽略大小写）。</summary>
        public List<string>? ArgStartsWith { get; set; }

        /// <summary>任一 token 含有列表中某个子串（忽略大小写）。用于全局规则。</summary>
        public List<string>? AnyTokenContains { get; set; }

        /// <summary>任一参数匹配该正则。</summary>
        public string? ArgMatches { get; set; }

        /// <summary>unix 风格 rm 同时带上强制与递归。</summary>
        public bool UnixRmForced { get; set; }

        /// <summary>任一参数指向盘符根或系统关键路径。</summary>
        public bool ProtectedPathArg { get; set; }

        /// <summary>任一参数指向 HKLM 注册表配置单元。</summary>
        public bool RegistryHiveArg { get; set; }

        /// <summary>是否至少给出了一个判定条件。全空的 when 一定是写错了。</summary>
        internal bool HasAnyCondition =>
            HasWord is { Count: > 0 } || CmdFlag is { Count: > 0 } || PsSwitch is { Count: > 0 }
            || ArgStartsWith is { Count: > 0 } || AnyTokenContains is { Count: > 0 }
            || !string.IsNullOrEmpty(ArgMatches)
            || UnixRmForced || ProtectedPathArg || RegistryHiveArg;

        private Regex? _compiled;
        private bool _compileFailed;

        /// <summary>编译好的 <see cref="ArgMatches"/>；表达式非法时返回 null。</summary>
        public Regex? CompiledArgMatches
        {
            get
            {
                if (_compiled is not null || _compileFailed || string.IsNullOrEmpty(ArgMatches)) return _compiled;
                try
                {
                    _compiled = new Regex(ArgMatches, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException)
                {
                    _compileFailed = true;
                }
                return _compiled;
            }
        }
    }

    /// <summary>一条命令安全规则。</summary>
    public sealed class SafetyRule
    {
        /// <summary>适用的命令名（已归一化的 LookupKey 形式：小写、去路径、去扩展名）。</summary>
        public List<string> Keys { get; set; } = new();

        /// <summary>裁决等级，取 <see cref="SafetyDecision"/> 的名字。</summary>
        public string Decision { get; set; } = "Prompt";

        /// <summary>本地化理由键。</summary>
        public string Reason { get; set; } = "";

        /// <summary>展示给用户的片段；省略时用命中的命令名。</summary>
        public string? Detail { get; set; }

        /// <summary>用实际命中的那个参数当 Detail（如被删除的系统路径）。</summary>
        public bool DetailFromMatch { get; set; }

        public RuleCondition? When { get; set; }

        internal HashSet<string> KeySet = new(StringComparer.OrdinalIgnoreCase);
        internal SafetyDecision ParsedDecision = SafetyDecision.Prompt;
    }

    /// <summary>包装器命令名。这些命令本身不危险，危险的是它们包裹的内容。</summary>
    public sealed class WrapperRules
    {
        /// <summary>完全透明的前缀，直接剥掉再判（sudo 等）。</summary>
        public List<string> Transparent { get; set; } = new();

        /// <summary>形如 <c>env A=1 cmd</c>，跳过赋值再判。</summary>
        public List<string> EnvPrefix { get; set; } = new();

        /// <summary>提权包装器，自身即高危。</summary>
        public List<string> Elevation { get; set; } = new();

        /// <summary><c>cmd /c &lt;body&gt;</c>。</summary>
        public List<string> CmdShell { get; set; } = new();

        /// <summary><c>powershell -Command &lt;script&gt;</c>。</summary>
        public List<string> PowerShell { get; set; } = new();

        /// <summary>终端宿主，会拉起新 shell。</summary>
        public List<string> TerminalHosts { get; set; } = new();

        internal HashSet<string> TransparentSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> EnvPrefixSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> ElevationSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> CmdShellSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> PowerShellSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> TerminalHostsSet = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>只读判定所需的名单。</summary>
    public sealed class ReadOnlyRules
    {
        public List<string> Commands { get; set; } = new();
        public List<string> NoSubcommandCheck { get; set; } = new();
        public Dictionary<string, List<string>> Subcommands { get; set; } = new();

        /// <summary>整体视为只读的 PowerShell 动词前缀（get- / test- / show-）。</summary>
        public List<string> VerbPrefixes { get; set; } = new();

        /// <summary>上述前缀里的例外（会弹交互框或抓凭据的）。</summary>
        public List<string> UnsafeVerbExceptions { get; set; } = new();

        internal HashSet<string> CommandSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> NoSubcommandCheckSet = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, HashSet<string>> SubcommandSets = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> UnsafeVerbExceptionSet = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 整套命令安全规则，从 <c>TerminalPlugin.rules.json</c> 反序列化而来。
    ///
    /// 把这些名单从 IL 里挪到数据文件有两个原因：
    /// 一是 vssadmin / bcdedit / certutil 这类字面量成片出现在程序集里时，杀软的
    /// 静态签名和 ML 模型会把这张「黑名单」当成勒索软件的「载荷清单」来打分；
    /// 二是规则从此可以独立于插件版本更新。
    ///
    /// 注意这里只搬数据，不搬逻辑：词法切分、包装器拆解、各个谓词仍在
    /// <see cref="CommandSafety"/> 里，规则文件无法引入新的判定方式。
    /// </summary>
    public sealed class SafetyRuleSet
    {
        public int Version { get; set; }
        public string? Description { get; set; }

        public WrapperRules Wrappers { get; set; } = new();
        public ReadOnlyRules ReadOnly { get; set; } = new();

        public List<string> Downloaders { get; set; } = new();
        public List<string> DownloadApiFragments { get; set; } = new();
        public List<string> Evaluators { get; set; } = new();
        public List<string> Browsers { get; set; } = new();
        public List<string> UrlLaunchers { get; set; } = new();

        /// <summary>
        /// 只有带上特定参数片段才算 URL 启动器的命令。
        ///
        /// <c>rundll32 url.dll,FileProtocolHandler http://x</c> 是已知的 LOLBin 打法：
        /// rundll32 本身用途很广，只有配上这个入口点才是"借系统程序打开链接"。
        /// </summary>
        public Dictionary<string, List<string>> UrlLauncherFragments { get; set; } = new();
        public List<string> ProtectedPaths { get; set; } = new();

        /// <summary>不针对具体命令名、对整条命令的所有 token 生效的规则。</summary>
        public List<SafetyRule> GlobalRules { get; set; } = new();

        /// <summary>按顺序求值，首个命中者胜出。Forbidden 规则排在 Dangerous 之前。</summary>
        public List<SafetyRule> Rules { get; set; } = new();

        internal HashSet<string> DownloaderSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> EvaluatorSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> BrowserSet = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> UrlLauncherSet = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, List<string>> UrlLauncherFragmentMap =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>把 List 展开成查找集合，并把 Decision 字符串解析成枚举。</summary>
        internal void Prepare()
        {
            static HashSet<string> Set(IEnumerable<string>? items) =>
                new(items ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            DownloaderSet = Set(Downloaders);
            EvaluatorSet = Set(Evaluators);
            BrowserSet = Set(Browsers);
            UrlLauncherSet = Set(UrlLaunchers);
            UrlLauncherFragmentMap = new Dictionary<string, List<string>>(
                UrlLauncherFragments, StringComparer.OrdinalIgnoreCase);

            Wrappers.TransparentSet = Set(Wrappers.Transparent);
            Wrappers.EnvPrefixSet = Set(Wrappers.EnvPrefix);
            Wrappers.ElevationSet = Set(Wrappers.Elevation);
            Wrappers.CmdShellSet = Set(Wrappers.CmdShell);
            Wrappers.PowerShellSet = Set(Wrappers.PowerShell);
            Wrappers.TerminalHostsSet = Set(Wrappers.TerminalHosts);

            ReadOnly.CommandSet = Set(ReadOnly.Commands);
            ReadOnly.NoSubcommandCheckSet = Set(ReadOnly.NoSubcommandCheck);
            ReadOnly.UnsafeVerbExceptionSet = Set(ReadOnly.UnsafeVerbExceptions);
            ReadOnly.SubcommandSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in ReadOnly.Subcommands)
            {
                ReadOnly.SubcommandSets[kv.Key] = Set(kv.Value);
            }

            foreach (var rule in Rules.Concat(GlobalRules))
            {
                rule.KeySet = Set(rule.Keys);
                rule.ParsedDecision = Enum.TryParse<SafetyDecision>(rule.Decision, ignoreCase: true, out var d)
                    ? d
                    : SafetyDecision.Dangerous;   // 认不出的等级按高危处理，绝不放松
            }

            // 保护路径统一小写，比对时也统一小写
            ProtectedPaths = ProtectedPaths.Select(p => p.ToLowerInvariant()).ToList();
        }

        /// <summary>
        /// 最低限度的完整性检查。
        ///
        /// 空文件、被截断的文件、以及"规则表为空"都必须视为加载失败——否则一个损坏的
        /// 规则文件会让所有危险命令一路畅通，比没有规则文件更糟。
        /// </summary>
        internal bool IsUsable(out string reason)
        {
            if (Version <= 0) { reason = "version 字段缺失或非法"; return false; }
            if (Rules.Count == 0) { reason = "rules 为空"; return false; }

            // 写了 when 却一个条件都没给，规则会永远不命中——等于悄悄少了一条防线，
            // 所以整份文件按损坏处理，而不是带着一条哑规则继续跑。
            foreach (var rule in Rules.Concat(GlobalRules))
            {
                if (rule.KeySet.Count == 0 && rule.When is null)
                {
                    reason = $"规则 '{rule.Reason}' 既没有 keys 也没有 when";
                    return false;
                }
                if (rule.When is not null && !rule.When.HasAnyCondition)
                {
                    reason = $"规则 '{rule.Reason}' 的 when 没有任何有效条件";
                    return false;
                }
                if (!string.IsNullOrEmpty(rule.When?.ArgMatches) && rule.When!.CompiledArgMatches is null)
                {
                    reason = $"规则 '{rule.Reason}' 的 argMatches 不是合法正则";
                    return false;
                }
            }
            if (ReadOnly.CommandSet.Count == 0) { reason = "readOnly.commands 为空"; return false; }
            if (Wrappers.CmdShellSet.Count == 0 || Wrappers.PowerShellSet.Count == 0)
            {
                reason = "wrappers 缺少 shell 包装器定义";
                return false;
            }
            reason = "";
            return true;
        }
    }

    /// <summary>规则文件加载器。</summary>
    public static class SafetyRuleLoader
    {
        /// <summary>规则文件名，与插件 DLL 同目录。</summary>
        public const string FileName = "TerminalPlugin.rules.json";

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// 从指定路径加载规则。失败时返回 null 并给出原因，调用方必须据此进入
        /// fail-closed 状态，绝不能退回"无规则=全部放行"。
        /// </summary>
        public static SafetyRuleSet? TryLoad(string path, out string error)
        {
            try
            {
                if (!File.Exists(path))
                {
                    error = $"规则文件不存在: {path}";
                    return null;
                }

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "规则文件为空";
                    return null;
                }

                var set = JsonSerializer.Deserialize<SafetyRuleSet>(json, Options);
                if (set is null)
                {
                    error = "规则文件反序列化结果为 null";
                    return null;
                }

                set.Prepare();
                if (!set.IsUsable(out var reason))
                {
                    error = $"规则文件内容不完整: {reason}";
                    return null;
                }

                error = "";
                return set;
            }
            catch (JsonException ex)
            {
                error = $"规则文件 JSON 解析失败: {ex.Message}";
                return null;
            }
            catch (Exception ex)
            {
                error = $"规则文件读取失败: {ex.Message}";
                return null;
            }
        }

        /// <summary>根据插件 DLL 的原始路径推出规则文件路径。</summary>
        public static string ResolvePath(string pluginFilePath)
        {
            var dir = string.IsNullOrEmpty(pluginFilePath)
                ? AppContext.BaseDirectory
                : (Path.GetDirectoryName(pluginFilePath) ?? AppContext.BaseDirectory);
            return Path.Combine(dir, FileName);
        }
    }
}

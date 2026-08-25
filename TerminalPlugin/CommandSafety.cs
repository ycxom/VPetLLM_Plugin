using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TerminalPlugin
{
    /// <summary>
    /// 命令安全裁决等级。取值越大越严格，多条规则命中时取最严者。
    /// </summary>
    public enum SafetyDecision
    {
        /// <summary>只读/无副作用，具备自动放行资格。</summary>
        Allow = 0,
        /// <summary>普通命令，需要用户确认。</summary>
        Prompt = 1,
        /// <summary>高危命令，需要用户在红色告警弹窗中显式放行。</summary>
        Dangerous = 2,
        /// <summary>不可挽回或无法审查，直接拒绝，不提供放行入口。</summary>
        Forbidden = 3
    }

    /// <summary>一次安全评估的结果。</summary>
    public sealed class SafetyVerdict
    {
        public SafetyDecision Decision { get; init; } = SafetyDecision.Prompt;

        /// <summary>本地化理由键，由 <c>TerminalPlugin.GetSafetyReason</c> 翻译。</summary>
        public string ReasonKey { get; init; } = "";

        /// <summary>命中的具体片段（命令名/参数），原样展示给用户。</summary>
        public string Detail { get; init; } = "";

        public static readonly SafetyVerdict Allowed = new() { Decision = SafetyDecision.Allow };

        public static SafetyVerdict Of(SafetyDecision decision, string reasonKey, string detail = "")
            => new() { Decision = decision, ReasonKey = reasonKey, Detail = detail };

        /// <summary>取更严格的一个。</summary>
        public static SafetyVerdict Max(SafetyVerdict a, SafetyVerdict b)
            => b.Decision > a.Decision ? b : a;
    }

    /// <summary>
    /// 结构化的命令安全评估。
    ///
    /// 相比"对整条字符串跑一串正则"，这里先做一次带引号感知的词法切分，把命令拆成
    /// 由 <c>;</c> <c>|</c> <c>&amp;</c> <c>&amp;&amp;</c> <c>||</c> 分隔的若干段，
    /// 再逐段取首 token 判定，并递归拆开 <c>sudo</c> / <c>cmd /c</c> / <c>powershell -Command</c>
    /// 这类包装器（带深度上限，防止无限套娃）。
    ///
    /// 思路取自 codex 的 shell-command/src/command_safety/。
    /// </summary>
    public static class CommandSafety
    {
        /// <summary>包装器递归拆解的最大层数，超过即判定为不可审查。</summary>
        private const int MaxWrapperDepth = 8;

        private static readonly Regex UrlPattern =
            new(@"(?:https?|ftps?|file)://|\bwww\.[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #region 词法切分

        /// <summary>一条命令切分后的结构化形态。</summary>
        public sealed class LexResult
        {
            /// <summary>按控制操作符切开的若干段，每段是一组 token。</summary>
            public List<List<string>> Segments { get; } = new();

            /// <summary>出现了 &gt; &gt;&gt; &lt; 重定向（会写文件）。</summary>
            public bool HasRedirect { get; set; }

            /// <summary>出现了 $( ) 或 @( ) 子表达式（内容不可静态审查）。</summary>
            public bool HasSubExpression { get; set; }

            /// <summary>出现了反引号转义（PowerShell 常见混淆手段）。</summary>
            public bool HasBacktick { get; set; }

            /// <summary>出现了管道以外的分隔符（; &amp; &amp;&amp; ||、换行）。</summary>
            public bool HasNonPipeSeparator { get; set; }

            /// <summary>出现了 --% 停止解析符，其后内容不受 PowerShell 解析。</summary>
            public bool HasStopParsing { get; set; }

            public IEnumerable<string> AllTokens => Segments.SelectMany(s => s);
        }

        /// <summary>
        /// 引号感知的切分。尽力而为：CMD 与 PowerShell 的转义规则各不相同，
        /// 这里只保证"看不懂的东西不会被误判成安全"，而不保证完全还原 shell 语义。
        /// </summary>
        public static LexResult Lex(string command)
        {
            var result = new LexResult();
            var current = new List<string>();
            var token = new StringBuilder();
            var hasToken = false;
            var parenDepth = 0;
            char quote = '\0';

            void FlushToken()
            {
                if (hasToken)
                {
                    current.Add(token.ToString());
                    token.Clear();
                    hasToken = false;
                }
            }

            void FlushSegment()
            {
                FlushToken();
                if (current.Count > 0)
                {
                    result.Segments.Add(new List<string>(current));
                    current.Clear();
                }
            }

            for (int i = 0; i < command.Length; i++)
            {
                var c = command[i];

                // 引号内：只处理闭合与双引号内的反引号转义
                if (quote != '\0')
                {
                    if (c == '`' && quote == '"' && i + 1 < command.Length)
                    {
                        result.HasBacktick = true;
                        token.Append(c).Append(command[i + 1]);
                        i++;
                        continue;
                    }
                    if (c == quote)
                    {
                        // '' 与 "" 在 shell 里是转义后的字面量引号
                        if (i + 1 < command.Length && command[i + 1] == quote)
                        {
                            token.Append(c);
                            i++;
                            continue;
                        }
                        quote = '\0';
                        continue;
                    }
                    token.Append(c);
                    hasToken = true;
                    continue;
                }

                switch (c)
                {
                    case '\'':
                    case '"':
                        quote = c;
                        hasToken = true;   // 空字符串也算一个 token
                        continue;

                    case '`':
                        result.HasBacktick = true;
                        if (i + 1 < command.Length)
                        {
                            token.Append(command[i + 1]);
                            hasToken = true;
                            i++;
                        }
                        continue;

                    case '^':
                        // CMD 的转义符
                        if (i + 1 < command.Length)
                        {
                            token.Append(command[i + 1]);
                            hasToken = true;
                            i++;
                        }
                        continue;

                    case '(':
                        parenDepth++;
                        FlushToken();
                        continue;

                    case ')':
                        if (parenDepth > 0) parenDepth--;
                        FlushToken();
                        continue;

                    case '$':
                    case '@':
                        if (i + 1 < command.Length && command[i + 1] == '(')
                        {
                            result.HasSubExpression = true;
                        }
                        token.Append(c);
                        hasToken = true;
                        continue;

                    case '>':
                    case '<':
                        result.HasRedirect = true;
                        FlushToken();
                        continue;

                    case '\r':
                        continue;

                    case '\n':
                    case ';':
                        result.HasNonPipeSeparator = true;
                        if (parenDepth == 0) FlushSegment(); else FlushToken();
                        continue;

                    case '|':
                        if (i + 1 < command.Length && command[i + 1] == '|')
                        {
                            result.HasNonPipeSeparator = true;
                            i++;
                        }
                        if (parenDepth == 0) FlushSegment(); else FlushToken();
                        continue;

                    case '&':
                        result.HasNonPipeSeparator = true;
                        if (i + 1 < command.Length && command[i + 1] == '&') i++;
                        if (parenDepth == 0) FlushSegment(); else FlushToken();
                        continue;

                    default:
                        if (char.IsWhiteSpace(c))
                        {
                            FlushToken();
                            continue;
                        }
                        token.Append(c);
                        hasToken = true;
                        continue;
                }
            }

            FlushSegment();

            if (command.Contains("--%")) result.HasStopParsing = true;

            return result;
        }

        /// <summary>
        /// 取可执行文件的查找键：只留文件名、转小写、去掉常见可执行扩展名。
        /// 这样 <c>C:\Windows\System32\cmd.exe</c> 与 <c>cmd</c> 命中同一条规则。
        /// </summary>
        public static string LookupKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var name = raw.Trim().Trim('\'', '"');
            var slash = name.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0 && slash + 1 < name.Length) name = name.Substring(slash + 1);
            name = name.ToLowerInvariant();
            foreach (var ext in new[] { ".exe", ".cmd", ".bat", ".com", ".ps1" })
            {
                if (name.EndsWith(ext, StringComparison.Ordinal))
                {
                    return name.Substring(0, name.Length - ext.Length);
                }
            }
            return name;
        }

        #endregion

        #region 规则数据

        /// <summary>
        /// 当前生效的规则集。为 null 表示规则文件加载失败，此时整体进入 fail-closed。
        /// </summary>
        private static SafetyRuleSet? _rules;

        /// <summary>规则加载失败的原因，供插件写日志和在设置界面里提示用户。</summary>
        public static string RuleLoadError { get; private set; } = "规则尚未加载";

        public static bool RulesLoaded => _rules is not null;

        /// <summary>
        /// 加载规则文件。必须在任何一次 <see cref="Evaluate"/> 之前调用。
        ///
        /// 失败不抛异常，而是让 <see cref="_rules"/> 保持 null —— 所有命令随之降级为
        /// 高危确认、自动放行全部关闭。"读不到规则就放行"是绝对不能有的行为：
        /// 那等于删掉规则文件就能拿到完整的命令执行权。
        /// </summary>
        public static void LoadRules(string path)
        {
            _rules = SafetyRuleLoader.TryLoad(path, out var error);
            RuleLoadError = error;
        }

        #endregion

        #region 评估入口

        /// <summary>评估一条命令的安全等级。</summary>
        public static SafetyVerdict Evaluate(string command)
        {
            // fail-closed：规则加载失败时一切命令降级为高危确认，交给用户在红色告警
            // 弹窗里逐条判断。绝不能因为读不到规则文件就当成"没有危险规则"。
            if (_rules is null) return SafetyVerdict.Of(SafetyDecision.Dangerous, "rules_unavailable");
            return EvaluateInternal(command, 0);
        }

        private static SafetyVerdict EvaluateInternal(string command, int depth)
        {
            if (depth > MaxWrapperDepth)
            {
                return SafetyVerdict.Of(SafetyDecision.Forbidden, "too_nested");
            }
            if (string.IsNullOrWhiteSpace(command))
            {
                return SafetyVerdict.Allowed;
            }

            var lex = Lex(command);
            var verdict = SafetyVerdict.Allowed;

            foreach (var segment in lex.Segments)
            {
                verdict = SafetyVerdict.Max(verdict, EvaluateSegment(segment, depth));
                if (verdict.Decision == SafetyDecision.Forbidden) return verdict;
            }

            var tokens = lex.AllTokens.ToList();

            // 全局规则：不针对具体命令名，对整条命令的所有 token 生效（如 FromBase64String）。
            // 放在分段裁决之后，保持与原先"同一段里 Forbidden 规则先于它命中"的顺序一致。
            var global = MatchGlobalRules(tokens);
            if (global is not null)
            {
                verdict = SafetyVerdict.Max(verdict, global);
                if (verdict.Decision == SafetyDecision.Forbidden) return verdict;
            }

            // 跨段检查：`irm http://x | iex` 这类下载即执行，单看任何一段都是无害的
            var keys = tokens.Select(LookupKey).ToList();
            var hasDownloader = keys.Any(k => _rules!.DownloaderSet.Contains(k))
                || tokens.Any(t => _rules!.DownloadApiFragments.Any(f => t.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0));
            if (hasDownloader && keys.Any(k => _rules!.EvaluatorSet.Contains(k)))
            {
                return SafetyVerdict.Of(SafetyDecision.Forbidden, "download_and_execute");
            }

            // --% 之后的内容不受解析，等于放弃审查
            if (lex.HasStopParsing)
            {
                verdict = SafetyVerdict.Max(verdict, SafetyVerdict.Of(SafetyDecision.Dangerous, "stop_parsing", "--%"));
            }

            return verdict;
        }

        private static SafetyVerdict EvaluateSegment(List<string> tokens, int depth)
        {
            if (tokens.Count == 0) return SafetyVerdict.Allowed;

            var key = LookupKey(tokens[0]);
            var rest = tokens.Skip(1).ToList();

            // ---------- 包装器：拆开再判 ----------
            var wrappers = _rules!.Wrappers;

            if (wrappers.TransparentSet.Contains(key))
            {
                return EvaluateSegment(rest, depth + 1);
            }

            if (wrappers.ElevationSet.Contains(key))
            {
                // runas /user:Administrator "<cmd>" —— 提权本身就该走高危确认
                return SafetyVerdict.Max(
                    SafetyVerdict.Of(SafetyDecision.Dangerous, "elevation", key),
                    EvaluateInternal(string.Join(" ", rest.Where(t => !t.StartsWith("/", StringComparison.Ordinal))), depth + 1));
            }

            if (wrappers.EnvPrefixSet.Contains(key))
            {
                return EvaluateSegment(SkipEnvAssignments(rest), depth + 1);
            }

            if (wrappers.CmdShellSet.Contains(key))
            {
                return EvaluateWrappedShell(rest, depth, key);
            }

            if (wrappers.PowerShellSet.Contains(key))
            {
                return EvaluatePowerShellWrapper(rest, depth, key);
            }

            if (wrappers.TerminalHostsSet.Contains(key))
            {
                return SafetyVerdict.Max(
                    SafetyVerdict.Of(SafetyDecision.Prompt, "spawns_shell", key),
                    EvaluateSegment(rest, depth + 1));
            }

            // ---------- URL 启动器（ShellExecute 逃逸） ----------
            if (_rules.UrlLauncherSet.Contains(key) && ArgsHaveUrl(rest))
            {
                return SafetyVerdict.Of(SafetyDecision.Dangerous, "url_launch", key);
            }
            if (_rules.BrowserSet.Contains(key) && ArgsHaveUrl(rest))
            {
                return SafetyVerdict.Of(SafetyDecision.Dangerous, "url_launch", key);
            }
            // 需要额外参数片段才成立的启动器（rundll32 那类 LOLBin）
            if (_rules.UrlLauncherFragmentMap.TryGetValue(key, out var fragments)
                && rest.Any(t => fragments.Any(f => t.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                && ArgsHaveUrl(rest))
            {
                return SafetyVerdict.Of(SafetyDecision.Dangerous, "url_launch", key);
            }

            // ---------- 规则表（Forbidden 条目排在 Dangerous 之前，首个命中者胜出）----------
            var ruled = MatchRules(key, rest);
            if (ruled is not null) return ruled;

            // ---------- 只读 ----------
            if (IsReadOnlySegment(key, rest)) return SafetyVerdict.Allowed;

            return SafetyVerdict.Of(SafetyDecision.Prompt, "");
        }

        /// <summary>拆 <c>cmd /c &lt;body&gt;</c>：跳过开关，把余下部分当成一条新命令重新评估。</summary>
        private static SafetyVerdict EvaluateWrappedShell(List<string> rest, int depth, string key)
        {
            var bodyFlags = new[] { "/c", "/k", "-c" };
            var index = rest.FindIndex(t => bodyFlags.Contains(t.ToLowerInvariant()));
            if (index < 0)
            {
                // 没有 /c，只是拉起一个交互 shell
                return SafetyVerdict.Of(SafetyDecision.Prompt, "spawns_shell", key);
            }
            var body = string.Join(" ", rest.Skip(index + 1));
            return EvaluateInternal(body, depth + 1);
        }

        /// <summary>拆 <c>powershell -Command &lt;script&gt;</c>，并拦下 -EncodedCommand 这类无法审查的形态。</summary>
        private static SafetyVerdict EvaluatePowerShellWrapper(List<string> rest, int depth, string key)
        {
            for (int i = 0; i < rest.Count; i++)
            {
                var flag = rest[i].ToLowerInvariant().TrimStart('-', '/');

                // -EncodedCommand / -enc / -e：内容是 base64，命令行审查完全失效
                if (flag.Length > 0 && "encodedcommand".StartsWith(flag, StringComparison.Ordinal))
                {
                    return SafetyVerdict.Of(SafetyDecision.Forbidden, "encoded_command", rest[i]);
                }
                if (flag is "command" or "c")
                {
                    var body = string.Join(" ", rest.Skip(i + 1));
                    return EvaluateInternal(body, depth + 1);
                }
                if (flag is "file" or "f")
                {
                    // 脚本文件内容不可见，只能交给用户判断
                    return SafetyVerdict.Of(SafetyDecision.Dangerous, "opaque_script",
                        rest.Skip(i + 1).FirstOrDefault() ?? "");
                }
            }
            return SafetyVerdict.Of(SafetyDecision.Prompt, "spawns_shell", key);
        }

        /// <summary>
        /// 按规则表顺序求值，首个命中者胜出。
        ///
        /// 顺序即语义：规则文件里 Forbidden 条目必须排在 Dangerous 之前。像
        /// <c>remove-item</c> 这种在两组里都出现的命令，靠的就是这个顺序——
        /// 删系统目录判 Forbidden，只带 -Recurse 才落到 Dangerous。
        /// </summary>
        private static SafetyVerdict? MatchRules(string key, List<string> rest)
        {
            foreach (var rule in _rules!.Rules)
            {
                if (!rule.KeySet.Contains(key)) continue;
                if (!MatchesCondition(rule.When, rest, out var matched)) continue;

                var detail = rule.DetailFromMatch ? matched : rule.Detail;
                return SafetyVerdict.Of(rule.ParsedDecision, rule.Reason,
                    string.IsNullOrEmpty(detail) ? key : detail!);
            }
            return null;
        }

        /// <summary>对整条命令的全部 token 生效、不绑定命令名的规则。</summary>
        private static SafetyVerdict? MatchGlobalRules(List<string> tokens)
        {
            foreach (var rule in _rules!.GlobalRules)
            {
                if (!MatchesCondition(rule.When, tokens, out var matched)) continue;

                var detail = rule.DetailFromMatch ? matched : rule.Detail;
                return SafetyVerdict.Of(rule.ParsedDecision, rule.Reason, detail ?? "");
            }
            return null;
        }

        /// <summary>
        /// 判断一组 token 是否满足规则条件。
        ///
        /// 多个条件之间是「或」——这是照搬原先 <c>when</c> 子句的语义，那些子句本身
        /// 就是若干 <c>Any(...)</c> 的或组合。<paramref name="matched"/> 只在按路径
        /// 匹配时有值，用于把命中的那个参数原样展示给用户。
        /// </summary>
        private static bool MatchesCondition(RuleCondition? when, List<string> tokens, out string? matched)
        {
            matched = null;
            if (when is null) return true;   // 无条件规则：命令名匹配即命中

            if (when.HasWord is { Count: > 0 } words
                && words.Any(w => HasWord(tokens, w))) return true;

            if (when.CmdFlag is { Count: > 0 } cmdFlags
                && HasCmdFlag(tokens, cmdFlags.ToArray())) return true;

            if (when.PsSwitch is { Count: > 0 } psSwitches
                && HasPsSwitch(tokens, psSwitches.ToArray())) return true;

            if (when.ArgStartsWith is { Count: > 0 } prefixes
                && tokens.Any(t => prefixes.Any(pfx => t.StartsWith(pfx, StringComparison.OrdinalIgnoreCase)))) return true;

            if (when.AnyTokenContains is { Count: > 0 } fragments
                && tokens.Any(t => fragments.Any(f => t.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))) return true;

            if (when.CompiledArgMatches is { } regex && tokens.Any(t => regex.IsMatch(t))) return true;

            if (when.UnixRmForced && UnixRmIsForced(tokens)) return true;

            if (when.RegistryHiveArg && tokens.Any(IsRegistryHive)) return true;

            if (when.ProtectedPathArg)
            {
                var hit = tokens.FirstOrDefault(IsProtectedPath);
                if (hit is not null)
                {
                    matched = hit;
                    return true;
                }
            }

            return false;
        }

        /// <summary>参数指向 HKLM 配置单元（改这里影响的是整机而非当前用户）。</summary>
        private static bool IsRegistryHive(string token)
            => token.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
               || token.IndexOf("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) >= 0;

        #endregion

        #region 自动放行

        /// <summary>
        /// 判断一条命令是否可以跳过确认弹窗直接执行。
        ///
        /// 这里是整个插件唯一绕过用户确认的入口，所以判定条件刻意保守：
        /// 任何看不懂、有副作用、或者可能被改写的形态一律返回 false。
        /// </summary>
        public static bool IsAutoApprovable(
            string command,
            SafetyVerdict verdict,
            IReadOnlyCollection<string> allowedPrefixes,
            bool allowReadOnly)
        {
            // 规则没加载起来时一律不自动放行
            if (_rules is null) return false;

            // 高危和硬拒绝永远不能自动放行，用户的前缀规则也不能覆盖
            if (verdict.Decision >= SafetyDecision.Dangerous) return false;
            if (string.IsNullOrWhiteSpace(command)) return false;

            var lex = Lex(command);
            if (lex.Segments.Count == 0) return false;

            // 重定向会写文件、子表达式和反引号让静态审查失效、--% 直接放弃解析
            if (lex.HasRedirect || lex.HasSubExpression || lex.HasBacktick || lex.HasStopParsing) return false;

            // ; & && || 可以把一条只读命令和一条破坏性命令串起来，一律不自动放行
            if (lex.HasNonPipeSeparator) return false;

            // 用户显式配置的前缀规则（token 级前缀匹配，`git status` 覆盖 `git status --short`）。
            // 这条对 Prompt 也生效 —— 前缀规则存在的意义正是让用户信任内置只读名单不认识的
            // 工具（docker ps、kubectl get ...），否则它只能重复内置名单已经放行的东西。
            if (allowedPrefixes.Count > 0 && lex.Segments.Count == 1)
            {
                var tokens = lex.Segments[0];
                foreach (var prefix in allowedPrefixes)
                {
                    if (MatchesPrefix(tokens, prefix)) return true;
                }
            }

            // 内置只读名单只放行确定无副作用的命令
            if (!allowReadOnly || verdict.Decision != SafetyDecision.Allow) return false;

            // 只剩纯管道，要求每一段的首 token 都是只读命令
            return lex.Segments.All(seg => IsReadOnlySegment(LookupKey(seg[0]), seg.Skip(1).ToList()));
        }

        /// <summary>token 级前缀匹配：规则的每个 token 都要和命令开头逐一相等。</summary>
        public static bool MatchesPrefix(List<string> tokens, string prefix)
        {
            var prefixTokens = Lex(prefix).Segments.FirstOrDefault();
            if (prefixTokens is null || prefixTokens.Count == 0) return false;
            if (prefixTokens.Count > tokens.Count) return false;

            for (int i = 0; i < prefixTokens.Count; i++)
            {
                var expected = i == 0 ? LookupKey(prefixTokens[0]) : prefixTokens[i];
                var actual = i == 0 ? LookupKey(tokens[0]) : tokens[i];
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static bool IsReadOnlySegment(string key, List<string> rest)
        {
            if (string.IsNullOrEmpty(key) || _rules is null) return false;

            var ro = _rules.ReadOnly;

            // Get-* / Test-* / Show-* 整体视为只读，排除少数会弹交互框或抓凭据的
            if (ro.VerbPrefixes.Any(pfx => key.StartsWith(pfx, StringComparison.Ordinal))
                && !ro.UnsafeVerbExceptionSet.Contains(key))
            {
                return true;
            }

            if (!ro.CommandSet.Contains(key)) return false;
            if (ro.NoSubcommandCheckSet.Contains(key)) return true;

            // 需要看子命令的（git / dotnet / npm ...）
            if (ro.SubcommandSets.TryGetValue(key, out var allowedSubs))
            {
                var sub = rest.FirstOrDefault(t => !t.StartsWith("-", StringComparison.Ordinal))
                          ?? rest.FirstOrDefault();
                return sub is not null && allowedSubs.Contains(sub);
            }

            return true;
        }

        #endregion

        #region 小工具

        private static bool ArgsHaveUrl(IEnumerable<string> args)
            => args.Any(a => UrlPattern.IsMatch(a));

        private static bool HasWord(IEnumerable<string> tokens, string word)
            => tokens.Any(t => t.Equals(word, StringComparison.OrdinalIgnoreCase));

        /// <summary>CMD 风格开关：<c>/s</c> <c>/q</c>，也接受合写的 <c>/sq</c>。</summary>
        private static bool HasCmdFlag(IEnumerable<string> tokens, params string[] flags)
            => tokens.Any(t =>
            {
                if (t.Length < 2 || (t[0] != '/' && t[0] != '-')) return false;
                var body = t.Substring(1).ToLowerInvariant();
                return flags.Any(f => body.Contains(f, StringComparison.Ordinal));
            });

        /// <summary>
        /// PowerShell 开关参数，支持任意长度的缩写：<c>-Rec</c> 和 <c>-r</c> 都命中 <c>-Recurse</c>。
        /// PowerShell 本身就接受不产生歧义的前缀，只按全名匹配会漏掉一半写法。
        /// </summary>
        private static bool HasPsSwitch(IEnumerable<string> tokens, params string[] names)
            => tokens.Any(t =>
            {
                if (!t.StartsWith("-", StringComparison.Ordinal) || t.Length < 2) return false;
                if (t.StartsWith("--", StringComparison.Ordinal)) return false;
                var body = t.Substring(1).ToLowerInvariant();
                return names.Any(n => n.StartsWith(body, StringComparison.Ordinal));
            });

        /// <summary>unix 风格 <c>rm</c>：同时带上强制与递归才算高危（<c>-rf</c> / <c>-fr</c> / <c>-r -f</c>）。</summary>
        private static bool UnixRmIsForced(IEnumerable<string> tokens)
        {
            bool force = false, recurse = false;
            foreach (var t in tokens)
            {
                if (!t.StartsWith("-", StringComparison.Ordinal)) continue;
                if (t.Equals("--force", StringComparison.OrdinalIgnoreCase)) force = true;
                else if (t.Equals("--recursive", StringComparison.OrdinalIgnoreCase)) recurse = true;
                else if (!t.StartsWith("--", StringComparison.Ordinal))
                {
                    var body = t.Substring(1);
                    if (body.Contains('f')) force = true;
                    if (body.Contains('r') || body.Contains('R')) recurse = true;
                }
            }
            return force && recurse;
        }

        /// <summary>系统关键路径 / 盘符根。</summary>
        private static bool IsProtectedPath(string token)
        {
            var t = token.Trim().Trim('\'', '"');
            if (t.Length == 0) return false;

            // 根目录必须在削尾之前判：TrimEnd 会把 "/" 和 "\" 直接削成空串，于是下面
            // 那句判断永远走不到，`rm -rf /` 就只剩"递归删除"这层高危确认而非硬拒绝。
            if (t is "/" or "\\") return true;

            t = t.TrimEnd('\\', '/', '*');
            if (t.Length == 0) return false;

            // C:  或  C:\
            if (Regex.IsMatch(t, @"^[a-z]:$", RegexOptions.IgnoreCase)) return true;

            if (_rules is null) return false;

            var lower = t.ToLowerInvariant();
            return _rules.ProtectedPaths.Contains(lower);
        }

        private static List<string> SkipEnvAssignments(List<string> tokens)
        {
            int i = 0;
            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (t == "--") { i++; break; }
                if (t.StartsWith("-", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }
                var eq = t.IndexOf('=');
                if (eq > 0)
                {
                    i++;
                    continue;
                }
                break;
            }
            return tokens.Skip(i).ToList();
        }

        #endregion
    }
}

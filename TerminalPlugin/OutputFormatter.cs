using System;
using System.Text;

namespace TerminalPlugin
{
    /// <summary>
    /// 命令输出的裁剪与拼装。
    ///
    /// 关键点是"掐中间"而不是"掐尾"：命令的结论、报错、退出摘要几乎都在输出末尾，
    /// 只保留开头会把最有价值的部分正好丢掉。做法取自 codex 的
    /// utils/string/src/truncate.rs（truncate_middle_chars）。
    /// </summary>
    public static class OutputFormatter
    {
        /// <summary>保留头部的占比，其余留给尾部。</summary>
        private const double HeadRatio = 0.55;

        /// <summary>
        /// 把字符串裁到 <paramref name="maxChars"/> 以内，保头保尾，中间用标记替换。
        /// 裁切点会对齐到最近的换行，避免把一行截断成乱码。
        /// </summary>
        public static string TruncateMiddle(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return text ?? "";
            if (text.Length <= maxChars) return text;

            var headBudget = (int)(maxChars * HeadRatio);
            var tailBudget = maxChars - headBudget;

            var headEnd = AlignToLineEnd(text, headBudget);
            var tailStart = AlignToLineStart(text, text.Length - tailBudget);
            if (tailStart <= headEnd)
            {
                // 对齐后两段重叠（比如整个输出就是一行），退回按字符硬切
                headEnd = headBudget;
                tailStart = text.Length - tailBudget;
            }

            var omittedChars = tailStart - headEnd;
            var omittedLines = CountLines(text, headEnd, tailStart);

            var sb = new StringBuilder(maxChars + 64);
            sb.Append(text, 0, headEnd);
            sb.Append("\n\n... [省略 ").Append(omittedLines).Append(" 行 / ")
              .Append(omittedChars).Append(" 字符] ...\n\n");
            sb.Append(text, tailStart, text.Length - tailStart);
            return sb.ToString();
        }

        /// <summary>
        /// 在 stdout 与 stderr 之间分配一个共享的字符预算。
        ///
        /// 旧实现是两边各自 4000，最坏一次往上下文里塞 8000+ 字符。这里先把预算按
        /// 实际长度比例分给两边，任一边没用满的额度让给另一边。
        /// </summary>
        public static (string Stdout, string Stderr) TruncatePair(string stdout, string stderr, int totalBudget)
        {
            stdout ??= "";
            stderr ??= "";
            if (totalBudget <= 0) return (stdout, stderr);
            if (stdout.Length + stderr.Length <= totalBudget) return (stdout, stderr);

            // stderr 通常更短也更关键，先保证它拿到至少 1/3 预算（用不完再还回去）
            var stderrBudget = Math.Min(stderr.Length, Math.Max(totalBudget / 3, 1));
            var stdoutBudget = totalBudget - stderrBudget;

            if (stdout.Length < stdoutBudget)
            {
                stderrBudget += stdoutBudget - stdout.Length;
                stdoutBudget = stdout.Length;
            }

            return (TruncateMiddle(stdout, stdoutBudget), TruncateMiddle(stderr, stderrBudget));
        }

        private static int AlignToLineEnd(string text, int index)
        {
            if (index >= text.Length) return text.Length;
            var nl = text.LastIndexOf('\n', Math.Min(index, text.Length - 1));
            return nl > 0 ? nl : index;
        }

        private static int AlignToLineStart(string text, int index)
        {
            if (index <= 0) return 0;
            if (index >= text.Length) return text.Length;
            var nl = text.IndexOf('\n', index);
            return nl >= 0 && nl + 1 < text.Length ? nl + 1 : index;
        }

        private static int CountLines(string text, int start, int end)
        {
            var count = 0;
            for (int i = start; i < end; i++)
            {
                if (text[i] == '\n') count++;
            }
            return count;
        }
    }
}

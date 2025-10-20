using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MarkdownViewerPlugin
{
    // 轻量级 Markdown 渲染器（无外部依赖），将 Markdown 转为 WPF FlowDocument
    // 支持：# 标题、**粗体**、*斜体*、`行内代码`、```代码块```、-/* 列表、> 引用、[文本](链接)、水平线、表格
    internal static class MinimalMarkdownRenderer
    {
        private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex CodeFence = new(@"^```(.*)$", RegexOptions.Compiled);
        private static readonly Regex UnorderedList = new(@"^\s*[-\*\+]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex OrderedList = new(@"^\s*\d+\.\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex TaskList = new(@"^\s*[-\*\+]\s+\[([ xX])\]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex BlockQuote = new(@"^\s*>\s?(.*)$", RegexOptions.Compiled);
        private static readonly Regex HorizontalRule = new(@"^\s{0,3}(-{3,}|_{3,}|\*{3,}|={3,})\s*$", RegexOptions.Compiled);

        // 表格：检测 header 分隔行，如 |:---|:---:|---:|
        private static readonly Regex TableSeparator = new(@"^\s*\|?\s*(:?-{3,}:?)\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", RegexOptions.Compiled);
        
        // 增强的标题识别（支持 Setext 风格）
        private static readonly Regex SetextHeading1 = new(@"^=+\s*$", RegexOptions.Compiled);
        private static readonly Regex SetextHeading2 = new(@"^-+\s*$", RegexOptions.Compiled);

        // 行内：粗体、斜体、行内代码、链接、删除线、高亮
        private static readonly Regex InlineCode = new(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex Bold = new(@"\*\*([^*]+?)\*\*|__([^_]+?)__", RegexOptions.Compiled);
        private static readonly Regex Italic = new(@"(?<!\*)\*(?!\*)([^*]+?)(?<!\*)\*(?!\*)|(?<!_)_(?!_)([^_]+?)(?<!_)_(?!_)", RegexOptions.Compiled);
        private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex Strikethrough = new(@"~~([^~]+?)~~", RegexOptions.Compiled);
        private static readonly Regex Highlight = new(@"==([^=]+?)==", RegexOptions.Compiled);
        private static readonly Regex Emoji = new(@":([a-z0-9_+-]+):", RegexOptions.Compiled);
        
        // 常见 emoji 映射
        private static readonly Dictionary<string, string> EmojiMap = new Dictionary<string, string>
        {
            { "smile", "😊" }, { "grin", "😁" }, { "joy", "😂" }, { "heart", "❤️" },
            { "thumbsup", "👍" }, { "thumbsdown", "👎" }, { "fire", "🔥" }, { "star", "⭐" },
            { "check", "✅" }, { "x", "❌" }, { "warning", "⚠️" }, { "info", "ℹ️" },
            { "rocket", "🚀" }, { "tada", "🎉" }, { "sparkles", "✨" }, { "bulb", "💡" },
            { "book", "📖" }, { "pencil", "✏️" }, { "memo", "📝" }, { "computer", "💻" },
            { "phone", "📱" }, { "email", "📧" }, { "calendar", "📅" }, { "clock", "🕐" },
            { "hourglass", "⏳" }, { "lock", "🔒" }, { "unlock", "🔓" }, { "key", "🔑" },
            { "hammer", "🔨" }, { "wrench", "🔧" }, { "gear", "⚙️" }, { "link", "🔗" },
            { "chart", "📊" }, { "graph", "📈" }, { "folder", "📁" }, { "file", "📄" },
            { "bug", "🐛" }, { "package", "📦" }, { "gift", "🎁" }, { "bell", "🔔" }
        };

        public static FlowDocument Render(string markdown)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = Brushes.Black,
                PagePadding = new Thickness(16)
            };

            if (string.IsNullOrWhiteSpace(markdown))
            {
                doc.Blocks.Add(new Paragraph(new Run("（无内容）")));
                return doc;
            }

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            string codeLang = "";
            var codeBuffer = new List<string>();
            List listItems = null;
            bool currentListOrdered = false;

            void FlushParagraph(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                var p = new Paragraph();
                AddInlines(p.Inlines, text);
                doc.Blocks.Add(p);
            }

            void FlushList()
            {
                if (listItems != null)
                {
                    doc.Blocks.Add(listItems);
                    listItems = null;
                }
            }

            int i = 0;
            while (i < lines.Length)
            {
                var raw = lines[i];
                var line = raw.Replace("\t", "    ");

                // 代码块开始/结束
                var mFence = CodeFence.Match(line);
                if (mFence.Success)
                {
                    if (!inCode)
                    {
                        inCode = true;
                        codeLang = mFence.Groups[1].Value.Trim();
                        codeBuffer.Clear();
                    }
                    else
                    {
                        // 结束代码块
                        var codeText = string.Join("\n", codeBuffer);
                        var para = new Paragraph
                        {
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 13,
                            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(8)
                        };
                        para.Inlines.Add(new Run(codeText));
                        doc.Blocks.Add(para);
                        inCode = false;
                        codeLang = "";
                    }
                    i++;
                    continue;
                }

                if (inCode)
                {
                    codeBuffer.Add(line);
                    i++;
                    continue;
                }

                // Setext 风格标题（需要检查下一行）
                if (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(line))
                {
                    var nextLine = lines[i + 1];
                    if (SetextHeading1.IsMatch(nextLine))
                    {
                        FlushList();
                        var p = new Paragraph();
                        AddInlines(p.Inlines, line.Trim());
                        p.FontSize = 24;
                        p.FontWeight = FontWeights.SemiBold;
                        p.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                        p.BorderThickness = new Thickness(0, 0, 0, 1);
                        p.Padding = new Thickness(0, 0, 0, 4);
                        p.Margin = new Thickness(0, 12, 0, 8);
                        doc.Blocks.Add(p);
                        i += 2; // 跳过标题和下划线
                        continue;
                    }
                    else if (SetextHeading2.IsMatch(nextLine) && !HorizontalRule.IsMatch(nextLine))
                    {
                        FlushList();
                        var p = new Paragraph();
                        AddInlines(p.Inlines, line.Trim());
                        p.FontSize = 20;
                        p.FontWeight = FontWeights.SemiBold;
                        p.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                        p.BorderThickness = new Thickness(0, 0, 0, 1);
                        p.Padding = new Thickness(0, 0, 0, 4);
                        p.Margin = new Thickness(0, 12, 0, 8);
                        doc.Blocks.Add(p);
                        i += 2; // 跳过标题和下划线
                        continue;
                    }
                }

                // 水平线
                if (HorizontalRule.IsMatch(line))
                {
                    FlushList();
                    var hr = new Paragraph
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Margin = new Thickness(0, 8, 0, 8)
                    };
                    doc.Blocks.Add(hr);
                    i++;
                    continue;
                }

                // 表格
                if (IsTableHeader(lines, i))
                {
                    FlushList();
                    i = ParseAndAddTable(doc, lines, i);
                    continue;
                }

                // ATX 风格标题 (# ## ### 等)
                var mh = Heading.Match(line);
                if (mh.Success)
                {
                    FlushList();
                    var level = mh.Groups[1].Value.Length;
                    var text = mh.Groups[2].Value.Trim();
                    var p = new Paragraph();
                    AddInlines(p.Inlines, text);
                    var size = level switch
                    {
                        1 => 24,
                        2 => 20,
                        3 => 18,
                        4 => 16,
                        5 => 15,
                        _ => 14
                    };
                    p.FontSize = size;
                    p.FontWeight = FontWeights.SemiBold;
                    if (level <= 2)
                    {
                        p.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                        p.BorderThickness = new Thickness(0, 0, 0, 1);
                        p.Padding = new Thickness(0, 0, 0, 4);
                        p.Margin = new Thickness(0, 12, 0, 8);
                    }
                    else
                    {
                        p.Margin = new Thickness(0, 8, 0, 4);
                    }
                    doc.Blocks.Add(p);
                    i++;
                    continue;
                }

                // 引用
                var mq = BlockQuote.Match(line);
                if (mq.Success)
                {
                    FlushList();
                    var p = new Paragraph();
                    p.Margin = new Thickness(8, 4, 0, 4);
                    p.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                    p.BorderThickness = new Thickness(4, 0, 0, 0);
                    p.Padding = new Thickness(8, 0, 0, 0);
                    AddInlines(p.Inlines, mq.Groups[1].Value);
                    doc.Blocks.Add(p);
                    i++;
                    continue;
                }

                // 任务列表（优先检查）
                var mt = TaskList.Match(line);
                if (mt.Success)
                {
                    FlushList();
                    var isChecked = mt.Groups[1].Value.ToLower() == "x";
                    var itemText = mt.Groups[2].Value.Trim();
                    
                    var p = new Paragraph();
                    p.Margin = new Thickness(0, 2, 0, 2);
                    
                    // 添加复选框符号
                    var checkbox = new Run(isChecked ? "☑ " : "☐ ")
                    {
                        FontSize = 16,
                        Foreground = isChecked ? Brushes.Green : Brushes.Gray
                    };
                    p.Inlines.Add(checkbox);
                    
                    // 添加任务文本
                    AddInlines(p.Inlines, itemText);
                    if (isChecked)
                    {
                        // 已完成的任务添加删除线
                        foreach (var inline in p.Inlines.ToList())
                        {
                            if (inline is Run run && run != checkbox)
                            {
                                run.TextDecorations.Add(TextDecorations.Strikethrough);
                                run.Foreground = Brushes.Gray;
                            }
                        }
                    }
                    
                    doc.Blocks.Add(p);
                    i++;
                    continue;
                }

                // 列表（无序/有序）
                var mu = UnorderedList.Match(line);
                var mo = OrderedList.Match(line);
                if (mu.Success || mo.Success)
                {
                    var itemText = (mu.Success ? mu.Groups[1].Value : mo.Groups[1].Value).Trim();
                    var ordered = mo.Success;

                    if (listItems == null || ordered != currentListOrdered)
                    {
                        FlushList();
                        listItems = ordered ? new List { MarkerStyle = TextMarkerStyle.Decimal } : new List { MarkerStyle = TextMarkerStyle.Disc };
                        currentListOrdered = ordered;
                    }
                    var li = new ListItem();
                    var p = new Paragraph();
                    AddInlines(p.Inlines, itemText);
                    li.Blocks.Add(p);
                    listItems.ListItems.Add(li);
                    i++;
                    continue;
                }
                else
                {
                    FlushList();
                }

                // 空行
                if (string.IsNullOrWhiteSpace(line))
                {
                    doc.Blocks.Add(new Paragraph());
                    i++;
                    continue;
                }

                // 普通段落
                FlushParagraph(line);
                i++;
            }

            FlushList();
            if (inCode)
            {
                var para = new Paragraph(new Run(string.Join("\n", codeBuffer)))
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8)
                };
                doc.Blocks.Add(para);
            }

            return doc;
        }

        private static bool IsTableHeader(string[] lines, int index)
        {
            if (index + 1 >= lines.Length) return false;
            var header = lines[index];
            var sep = lines[index + 1];
            if (!header.Contains("|")) return false;
            return TableSeparator.IsMatch(sep);
        }

        private static int ParseAndAddTable(FlowDocument doc, string[] lines, int start)
        {
            // 解析 header、分隔行、数据行
            var headerLine = lines[start];
            var sepLine = lines[start + 1];

            var headers = SplitTableRow(headerLine);
            var aligns = ParseAlignments(SplitTableRow(sepLine));

            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 12)
            };

            int colCount = headers.Count;
            for (int c = 0; c < colCount; c++)
            {
                table.Columns.Add(new TableColumn());
            }

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            // header row
            var headerRow = new TableRow();
            for (int c = 0; c < colCount; c++)
            {
                var cellPara = new Paragraph { Margin = new Thickness(4), FontWeight = FontWeights.SemiBold };
                ApplyAlignment(cellPara, aligns, c);
                AddInlines(cellPara.Inlines, headers[c]);
                var cell = new TableCell(cellPara)
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(0)
                };
                headerRow.Cells.Add(cell);
            }
            group.Rows.Add(headerRow);

            int i = start + 2;
            while (i < lines.Length)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) { i++; break; }
                // 表格行需要包含 |，否则终止
                if (!line.Contains("|")) break;

                // 允许代码围栏或其他块终止表格
                if (CodeFence.IsMatch(line) || Heading.IsMatch(line) || BlockQuote.IsMatch(line) || UnorderedList.IsMatch(line) || OrderedList.IsMatch(line) || HorizontalRule.IsMatch(line))
                    break;

                var cells = SplitTableRow(line);
                // 补足列
                while (cells.Count < colCount) cells.Add("");
                if (cells.Count > colCount) cells = cells.Take(colCount).ToList();

                var row = new TableRow();
                for (int c = 0; c < colCount; c++)
                {
                    var cellPara = new Paragraph { Margin = new Thickness(4) };
                    ApplyAlignment(cellPara, aligns, c);
                    AddInlines(cellPara.Inlines, cells[c]);
                    var cell = new TableCell(cellPara)
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(0)
                    };
                    row.Cells.Add(cell);
                }
                group.Rows.Add(row);
                i++;
            }

            doc.Blocks.Add(table);
            return i;
        }

        private static List<string> SplitTableRow(string line)
        {
            // 去掉首尾竖线，并按 | 分割（不处理转义竖线，作为简化）
            var s = line.Trim();
            if (s.StartsWith("|")) s = s.Substring(1);
            if (s.EndsWith("|")) s = s.Substring(0, s.Length - 1);
            var parts = s.Split('|').Select(x => x.Trim()).ToList();
            return parts;
        }

        private enum Align { Left, Center, Right }

        private static List<Align> ParseAlignments(List<string> seps)
        {
            var res = new List<Align>(seps.Count);
            foreach (var sep in seps)
            {
                var t = sep.Trim();
                bool left = t.StartsWith(":");
                bool right = t.EndsWith(":");
                if (left && right) res.Add(Align.Center);
                else if (right) res.Add(Align.Right);
                else res.Add(Align.Left);
            }
            return res;
        }

        private static void ApplyAlignment(Paragraph p, List<Align> aligns, int index)
        {
            if (index >= aligns.Count) { p.TextAlignment = TextAlignment.Left; return; }
            p.TextAlignment = aligns[index] switch
            {
                Align.Left => TextAlignment.Left,
                Align.Center => TextAlignment.Center,
                Align.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            };
        }

        private static void AddInlines(InlineCollection inlines, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                inlines.Add(new Run(""));
                return;
            }

            // 链接先处理，拆分成“非链接 + 链接”段
            var parts = new List<object>();
            int last = 0;
            foreach (Match m in Link.Matches(text))
            {
                if (m.Index > last)
                    parts.Add(text.Substring(last, m.Index - last));
                parts.Add(new Tuple<string, string>(m.Groups[1].Value, m.Groups[2].Value));
                last = m.Index + m.Length;
            }
            if (last < text.Length)
                parts.Add(text.Substring(last));

            void EmitInline(string seg)
            {
                // 行内代码
                var cursor = 0;
                foreach (Match mc in InlineCode.Matches(seg))
                {
                    if (mc.Index > cursor) EmitDecor(seg.Substring(cursor, mc.Index - cursor));
                    var codeRun = new Run(mc.Groups[1].Value)
                    {
                        Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                        FontFamily = new FontFamily("Consolas")
                    };
                    inlines.Add(codeRun);
                    cursor = mc.Index + mc.Length;
                }
                if (cursor < seg.Length) EmitDecor(seg.Substring(cursor));
            }

            void EmitDecor(string seg)
            {
                // 删除线
                var buf0 = new List<object>();
                int cur0 = 0;
                foreach (Match ms in Strikethrough.Matches(seg))
                {
                    if (ms.Index > cur0)
                        buf0.Add(seg.Substring(cur0, ms.Index - cur0));
                    buf0.Add(new { Type = "strike", Text = ms.Groups[1].Value });
                    cur0 = ms.Index + ms.Length;
                }
                if (cur0 < seg.Length)
                    buf0.Add(seg.Substring(cur0));

                // 高亮
                var buf1 = new List<object>();
                foreach (var item in buf0)
                {
                    if (item is string str)
                    {
                        int cur1 = 0;
                        foreach (Match mh in Highlight.Matches(str))
                        {
                            if (mh.Index > cur1)
                                buf1.Add(str.Substring(cur1, mh.Index - cur1));
                            buf1.Add(new { Type = "highlight", Text = mh.Groups[1].Value });
                            cur1 = mh.Index + mh.Length;
                        }
                        if (cur1 < str.Length)
                            buf1.Add(str.Substring(cur1));
                    }
                    else
                    {
                        buf1.Add(item);
                    }
                }

                // 粗体
                var buf2 = new List<object>();
                foreach (var item in buf1)
                {
                    if (item is string str)
                    {
                        int cur = 0;
                        foreach (Match mb in Bold.Matches(str))
                        {
                            if (mb.Index > cur)
                                buf2.Add(str.Substring(cur, mb.Index - cur));
                            var boldText = mb.Groups[1].Success ? mb.Groups[1].Value : mb.Groups[2].Value;
                            buf2.Add(new { Type = "bold", Text = boldText });
                            cur = mb.Index + mb.Length;
                        }
                        if (cur < str.Length)
                            buf2.Add(str.Substring(cur));
                    }
                    else
                    {
                        buf2.Add(item);
                    }
                }

                // 斜体
                var buf3 = new List<object>();
                foreach (var item in buf2)
                {
                    if (item is string str)
                    {
                        int c2 = 0;
                        foreach (Match mi in Italic.Matches(str))
                        {
                            if (mi.Index > c2)
                                buf3.Add(str.Substring(c2, mi.Index - c2));
                            var it = mi.Groups[1].Success ? mi.Groups[1].Value : mi.Groups[2].Value;
                            buf3.Add(new { Type = "italic", Text = it });
                            c2 = mi.Index + mi.Length;
                        }
                        if (c2 < str.Length)
                            buf3.Add(str.Substring(c2));
                    }
                    else
                    {
                        buf3.Add(item);
                    }
                }

                // 转换为 Inline
                foreach (var item in buf3)
                {
                    if (item is string str)
                    {
                        if (!string.IsNullOrEmpty(str))
                            inlines.Add(new Run(str));
                    }
                    else
                    {
                        var type = item.GetType().GetProperty("Type")?.GetValue(item)?.ToString();
                        var text = item.GetType().GetProperty("Text")?.GetValue(item)?.ToString();
                        
                        if (type == "bold")
                        {
                            inlines.Add(new Run(text) { FontWeight = FontWeights.Bold });
                        }
                        else if (type == "italic")
                        {
                            inlines.Add(new Run(text) { FontStyle = FontStyles.Italic });
                        }
                        else if (type == "strike")
                        {
                            var run = new Run(text);
                            run.TextDecorations.Add(TextDecorations.Strikethrough);
                            inlines.Add(run);
                        }
                        else if (type == "highlight")
                        {
                            inlines.Add(new Run(text) 
                            { 
                                Background = new SolidColorBrush(Color.FromRgb(255, 255, 0)) 
                            });
                        }
                    }
                }
            }

            foreach (var part in parts)
            {
                if (part is string s)
                {
                    EmitInline(s);
                }
                else if (part is Tuple<string, string> link)
                {
                    var hl = new Hyperlink(new Run(link.Item1));
                    if (Uri.TryCreate(link.Item2, UriKind.Absolute, out var uri))
                        hl.NavigateUri = uri;
                    hl.RequestNavigate += (o, e) =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                        catch { }
                        e.Handled = true;
                    };
                    inlines.Add(hl);
                }
            }
        }

        /// <summary>
        /// 处理 emoji 短代码，转换为实际的 emoji 字符
        /// </summary>
        private static string ProcessEmoji(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return Emoji.Replace(text, match =>
            {
                var emojiName = match.Groups[1].Value.ToLower();
                if (EmojiMap.TryGetValue(emojiName, out var emoji))
                {
                    return emoji;
                }
                return match.Value; // 保持原样
            });
        }
    }
}
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace WebSearchPlugin
{
    public class WebScraper
    {
        private readonly HttpClient _httpClient;
        public int MaxContentLength { get; set; } = 20000;

        // 单页最大下载量，超出部分截断（防止误抓大文件占用内存）
        private const int MaxDownloadBytes = 5 * 1024 * 1024;

        static WebScraper()
        {
            // GBK/GB2312/Big5 等代码页编码不在 .NET 默认编码表中，需注册后才能解码
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public WebScraper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> FetchAsMarkdown(string url)
        {
            try
            {
                VPetLLM.Utils.System.Logger.Log($"WebScraper: Fetching {url} (Local Mode)");

                // 获取网页内容
                var html = await FetchHtml(url);
                if (string.IsNullOrEmpty(html))
                {
                    return "";
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // 标题要在裁剪前提取（<title> 在主内容区域之外）
                var title = ExtractTitle(doc);

                // 清理噪音标签并定位主内容区域
                var contentRoot = ExtractContentRoot(doc);

                // 转换为 Markdown
                var markdown = ConvertToMarkdown(contentRoot, url);

                // 后处理
                markdown = PostProcessMarkdown(markdown, url, title);

                VPetLLM.Utils.System.Logger.Log($"WebScraper: Successfully converted to Markdown ({markdown.Length} chars)");

                return markdown;
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.System.Logger.Log($"WebScraper error: {ex.Message}");
                return $"错误：无法获取网页内容 - {ex.Message}";
            }
        }

        private async Task<string> FetchHtml(string url)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                if (mediaType.Length > 0 && !mediaType.StartsWith("text/") &&
                    mediaType != "application/xhtml+xml" && mediaType != "application/xml")
                {
                    throw new InvalidOperationException($"不支持的内容类型 {mediaType}，仅支持网页");
                }

                var bytes = await ReadBytesWithLimitAsync(response);
                var encoding = DetectEncoding(response.Content.Headers.ContentType?.CharSet, bytes);
                return encoding.GetString(bytes);
            }
            catch (HttpRequestException ex)
            {
                VPetLLM.Utils.System.Logger.Log($"HTTP error fetching {url}: {ex.Message}");
                throw;
            }
        }

        private static async Task<byte[]> ReadBytesWithLimitAsync(HttpResponseMessage response)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length >= MaxDownloadBytes) break; // 截断超大页面，已读部分仍可解析
            }
            return buffer.ToArray();
        }

        private static Encoding DetectEncoding(string? headerCharset, byte[] bytes)
        {
            // BOM 优先
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;

            var charset = headerCharset?.Trim().Trim('"', '\'');

            // HTTP 头没有 charset 时，从 <meta charset> / http-equiv 里嗅探（常见于 GBK 中文站点）
            if (string.IsNullOrEmpty(charset))
            {
                var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 8192));
                var match = Regex.Match(head, @"charset\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
                if (match.Success) charset = match.Groups[1].Value;
            }

            if (!string.IsNullOrEmpty(charset))
            {
                try { return Encoding.GetEncoding(charset); } catch { }
            }
            return Encoding.UTF8;
        }

        private static string ExtractTitle(HtmlDocument doc)
        {
            var title = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "");
            if (string.IsNullOrWhiteSpace(title))
            {
                title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "";
            }
            return NormalizeInlineText(System.Net.WebUtility.HtmlDecode(title));
        }

        private HtmlNode ExtractContentRoot(HtmlDocument doc)
        {
            RemoveNoiseTags(doc);
            return ExtractMainContent(doc)
                ?? doc.DocumentNode.SelectSingleNode("//body")
                ?? doc.DocumentNode;
        }

        private void RemoveNoiseTags(HtmlDocument doc)
        {
            var tagsToRemove = new[] { "script", "style", "nav", "footer", "header", "aside", "iframe", "noscript",
                "form", "svg", "button", "input", "select", "textarea", "canvas", "template" };
            foreach (var tag in tagsToRemove)
            {
                try
                {
                    var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
                    if (nodes is not null && nodes.Count > 0)
                    {
                        foreach (var node in nodes)
                        {
                            node.Remove();
                        }
                    }
                }
                catch (Exception ex)
                {
                    VPetLLM.Utils.System.Logger.Log($"WebScraper: Error removing {tag}: {ex.Message}");
                }
            }
        }

        private HtmlNode? ExtractMainContent(HtmlDocument doc)
        {
            // 尝试常见的主内容选择器（优先级从高到低）
            var selectors = new[]
            {
                "//article",
                "//main",
                "//*[@role='main']",
                "//*[@id='content']",
                "//*[@id='main-content']",
                "//*[@id='main']",
                "//*[@id='article']",
                "//*[contains(@class, 'article-content')]",
                "//*[contains(@class, 'post-content')]",
                "//*[contains(@class, 'entry-content')]",
                "//*[contains(@class, 'content-main')]",
                "//*[contains(@class, 'main-content')]",
                "//*[@class='content']",
                "//*[@class='main']",
                "//*[@class='article']"
            };

            foreach (var selector in selectors)
            {
                try
                {
                    var node = doc.DocumentNode.SelectSingleNode(selector);
                    if (node is not null && node.InnerText.Trim().Length > 200)
                    {
                        VPetLLM.Utils.System.Logger.Log($"WebScraper: Found main content using selector: {selector}");
                        return node;
                    }
                }
                catch { }
            }

            // 智能查找：基于内容密度和文本长度
            return FindContentByDensity(doc);
        }

        private HtmlNode? FindContentByDensity(HtmlDocument doc)
        {
            try
            {
                var candidates = doc.DocumentNode.SelectNodes("//div | //section | //article");
                if (candidates is null || candidates.Count == 0)
                {
                    return null;
                }

                HtmlNode? bestNode = null;
                double bestScore = 0;

                foreach (var node in candidates)
                {
                    var score = CalculateContentScore(node);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestNode = node;
                    }
                }

                if (bestNode is not null && bestScore > 100)
                {
                    VPetLLM.Utils.System.Logger.Log($"WebScraper: Found content by density (score: {bestScore:F2})");
                    return bestNode;
                }
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.System.Logger.Log($"WebScraper: Error in density analysis: {ex.Message}");
            }

            return null;
        }

        private double CalculateContentScore(HtmlNode node)
        {
            try
            {
                var text = node.InnerText.Trim();
                var textLength = text.Length;

                // 文本太短，不是主内容
                if (textLength < 200) return 0;

                // 计算文本密度（文本长度 / HTML 长度）
                var htmlLength = node.InnerHtml.Length;
                var density = htmlLength > 0 ? (double)textLength / htmlLength : 0;

                // 计算段落数量
                var paragraphs = node.SelectNodes(".//p")?.Count ?? 0;

                // 计算链接密度（链接文本 / 总文本）
                var links = node.SelectNodes(".//a");
                var linkTextLength = 0;
                if (links is not null)
                {
                    foreach (var link in links)
                    {
                        linkTextLength += link.InnerText.Length;
                    }
                }
                var linkDensity = textLength > 0 ? (double)linkTextLength / textLength : 0;

                // 综合评分
                var score = textLength * 0.5 +           // 文本长度权重
                           density * 1000 +              // 文本密度权重
                           paragraphs * 50 -             // 段落数量加分
                           linkDensity * 500;            // 链接密度惩罚

                return score;
            }
            catch
            {
                return 0;
            }
        }

        private string ConvertToMarkdown(HtmlNode root, string baseUrl)
        {
            var sb = new StringBuilder();
            ProcessNode(root, sb, baseUrl);
            return sb.ToString();
        }

        private void ProcessNode(HtmlNode node, StringBuilder sb, string baseUrl)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var text = System.Net.WebUtility.HtmlDecode(node.InnerText);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // 折叠空白但保留词间分隔，避免相邻文本粘连
                    sb.Append(Regex.Replace(text, @"\s+", " "));
                }
                return;
            }

            if (node.NodeType != HtmlNodeType.Element) return;

            switch (node.Name.ToLower())
            {
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    {
                        var text = RenderInline(node, baseUrl);
                        if (text.Length > 0)
                        {
                            var prefix = new string('#', node.Name[1] - '0');
                            sb.AppendLine($"\n{prefix} {text}\n");
                        }
                        break;
                    }
                case "p":
                    {
                        var text = RenderInline(node, baseUrl);
                        if (text.Length > 0) sb.AppendLine($"\n{text}\n");
                        break;
                    }
                case "br":
                    sb.AppendLine();
                    break;
                case "strong":
                case "b":
                case "em":
                case "i":
                case "code":
                case "a":
                case "img":
                    {
                        var tmp = new StringBuilder();
                        AppendInline(node, tmp, baseUrl);
                        sb.Append(NormalizeInlineText(tmp.ToString()));
                        break;
                    }
                case "pre":
                    {
                        // 代码块保留原始换行和缩进，不能走空白折叠
                        var code = System.Net.WebUtility.HtmlDecode(node.InnerText).Trim('\r', '\n');
                        sb.AppendLine($"\n```\n{code}\n```\n");
                        break;
                    }
                case "ul":
                case "ol":
                    sb.AppendLine();
                    ProcessList(node, sb, 0, node.Name == "ol", baseUrl);
                    sb.AppendLine();
                    break;
                case "table":
                    ProcessTable(node, sb, baseUrl);
                    break;
                case "blockquote":
                    {
                        var text = RenderInline(node, baseUrl);
                        if (text.Length > 0) sb.AppendLine($"\n> {text}\n");
                        break;
                    }
                case "hr":
                    sb.AppendLine("\n---\n");
                    break;
                case "div":
                case "section":
                case "article":
                case "main":
                case "tr":
                case "li":
                    foreach (var child in node.ChildNodes)
                    {
                        ProcessNode(child, sb, baseUrl);
                    }
                    // 块级容器结束后补换行，避免相邻块文本粘连
                    if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.AppendLine();
                    break;
                default:
                    foreach (var child in node.ChildNodes)
                    {
                        ProcessNode(child, sb, baseUrl);
                    }
                    break;
            }
        }

        /// <summary>
        /// 渲染节点的子内容为单行 Markdown（保留链接/加粗等行内格式）
        /// </summary>
        private string RenderInline(HtmlNode node, string baseUrl)
        {
            var sb = new StringBuilder();
            foreach (var child in node.ChildNodes)
            {
                AppendInline(child, sb, baseUrl);
            }
            return NormalizeInlineText(sb.ToString());
        }

        private void AppendInline(HtmlNode node, StringBuilder sb, string baseUrl)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                sb.Append(System.Net.WebUtility.HtmlDecode(node.InnerText));
                return;
            }

            if (node.NodeType != HtmlNodeType.Element) return;

            switch (node.Name.ToLower())
            {
                case "strong":
                case "b":
                    {
                        var text = RenderInline(node, baseUrl);
                        if (text.Length > 0) sb.Append($"**{text}**");
                        break;
                    }
                case "em":
                case "i":
                    {
                        var text = RenderInline(node, baseUrl);
                        if (text.Length > 0) sb.Append($"*{text}*");
                        break;
                    }
                case "code":
                    {
                        var text = NormalizeInlineText(System.Net.WebUtility.HtmlDecode(node.InnerText));
                        if (text.Length > 0) sb.Append($"`{text}`");
                        break;
                    }
                case "a":
                    {
                        var text = RenderInline(node, baseUrl);
                        var href = node.GetAttributeValue("href", "").Trim();
                        if (string.IsNullOrEmpty(href) || href.StartsWith("#") ||
                            href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                        {
                            sb.Append(text);
                        }
                        else
                        {
                            var url = ResolveUrl(href, baseUrl);
                            sb.Append(text.Length > 0 ? $"[{text}]({url})" : url);
                        }
                        break;
                    }
                case "img":
                    {
                        var src = node.GetAttributeValue("src", "").Trim();
                        // data: 内联图片是 base64 大块文本，直接丢弃
                        if (!string.IsNullOrEmpty(src) && !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            var alt = node.GetAttributeValue("alt", "image");
                            sb.Append($"![{alt}]({ResolveUrl(src, baseUrl)})");
                        }
                        break;
                    }
                case "br":
                    sb.Append(' ');
                    break;
                default:
                    foreach (var child in node.ChildNodes)
                    {
                        AppendInline(child, sb, baseUrl);
                    }
                    break;
            }
        }

        private static string ResolveUrl(string href, string baseUrl)
        {
            try
            {
                // 必须先基于 base 做相对解析：直接按绝对 URI 解析会把 //host/path 误判为 file:// UNC 路径
                if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
                    Uri.TryCreate(baseUri, href, out var resolved))
                {
                    return resolved.ToString();
                }
                if (Uri.TryCreate(href, UriKind.Absolute, out var abs) &&
                    (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
                {
                    return abs.ToString();
                }
            }
            catch { }
            return href;
        }

        private void ProcessList(HtmlNode listNode, StringBuilder sb, int depth, bool ordered, string baseUrl)
        {
            try
            {
                // 只取直接子级 li，嵌套列表单独递归，避免条目重复
                var items = listNode.SelectNodes("./li");
                if (items is null || items.Count == 0) return;

                int index = 1;
                var indent = new string(' ', depth * 2);
                foreach (var item in items)
                {
                    var itemSb = new StringBuilder();
                    foreach (var child in item.ChildNodes)
                    {
                        if (child.Name == "ul" || child.Name == "ol") continue;
                        AppendInline(child, itemSb, baseUrl);
                    }

                    var text = NormalizeInlineText(itemSb.ToString());
                    var prefix = ordered ? $"{index}. " : "- ";
                    if (text.Length > 0)
                    {
                        sb.AppendLine($"{indent}{prefix}{text}");
                    }
                    index++;

                    foreach (var child in item.ChildNodes)
                    {
                        if (child.Name == "ul" || child.Name == "ol")
                        {
                            ProcessList(child, sb, depth + 1, child.Name == "ol", baseUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.System.Logger.Log($"WebScraper: Error processing list: {ex.Message}");
            }
        }

        private void ProcessTable(HtmlNode table, StringBuilder sb, string baseUrl)
        {
            try
            {
                var rows = table.SelectNodes("./tr | ./thead/tr | ./tbody/tr | ./tfoot/tr");
                if (rows is null || rows.Count == 0) return;

                sb.AppendLine();
                bool headerWritten = false;
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("./td | ./th");
                    if (cells is null || cells.Count == 0) continue;

                    var texts = cells.Select(c => RenderInline(c, baseUrl).Replace("|", "\\|"));
                    sb.AppendLine("| " + string.Join(" | ", texts) + " |");

                    if (!headerWritten)
                    {
                        sb.AppendLine("|" + string.Concat(Enumerable.Repeat(" --- |", cells.Count)));
                        headerWritten = true;
                    }
                }
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.System.Logger.Log($"WebScraper: Error processing table: {ex.Message}");
            }
        }

        private static string NormalizeInlineText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private string PostProcessMarkdown(string markdown, string sourceUrl, string title)
        {
            var sb = new StringBuilder();

            // 添加标题和来源
            sb.AppendLine($"# {(string.IsNullOrEmpty(title) ? "网页内容" : title)}\n");
            sb.AppendLine($"**来源**: {sourceUrl}\n");
            sb.AppendLine("---\n");

            // 清理多余的空行
            var lines = markdown.Split('\n');
            int consecutiveEmptyLines = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    consecutiveEmptyLines++;
                    if (consecutiveEmptyLines <= 1)
                    {
                        sb.AppendLine();
                    }
                }
                else
                {
                    consecutiveEmptyLines = 0;
                    sb.AppendLine(line.TrimEnd());
                }
            }

            // 限制长度（避免内容过长），在行边界截断
            var result = sb.ToString();
            if (result.Length > MaxContentLength)
            {
                var cut = result.LastIndexOf('\n', MaxContentLength - 1);
                if (cut < MaxContentLength / 2) cut = MaxContentLength;
                if (cut > 0 && char.IsHighSurrogate(result[cut - 1])) cut--;
                result = result.Substring(0, cut);
                result += "\n\n---\n\n⚠️ 内容过长，已截断。完整内容请访问原网页。";
            }

            return result;
        }
    }
}

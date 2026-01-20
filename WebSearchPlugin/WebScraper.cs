using System;
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

                // 清理和提取主要内容
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var cleanedHtml = CleanHtml(doc);

                // 转换为 Markdown
                var markdown = ConvertToMarkdown(cleanedHtml);

                // 后处理
                markdown = PostProcessMarkdown(markdown, url);

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
                // 添加随机延迟，避免被识别为爬虫
                await Task.Delay(Random.Shared.Next(500, 1500));

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return content;
            }
            catch (HttpRequestException ex)
            {
                VPetLLM.Utils.System.Logger.Log($"HTTP error fetching {url}: {ex.Message}");
                throw;
            }
        }

        private HtmlDocument CleanHtml(HtmlDocument doc)
        {
            // 移除不需要的标签
            var tagsToRemove = new[] { "script", "style", "nav", "footer", "header", "aside", "iframe", "noscript" };
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

            // 尝试提取主要内容区域
            var mainNode = ExtractMainContent(doc);
            
            if (mainNode is null)
            {
                mainNode = doc.DocumentNode;
            }

            return doc;
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

        private string ConvertToMarkdown(HtmlDocument doc)
        {
            var sb = new StringBuilder();
            ProcessNode(doc.DocumentNode, sb, 0);
            return sb.ToString();
        }

        private void ProcessNode(HtmlNode node, StringBuilder sb, int level)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var text = node.InnerText.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    text = System.Net.WebUtility.HtmlDecode(text);
                    sb.Append(text);
                }
                return;
            }

            if (node.NodeType != HtmlNodeType.Element) return;

            switch (node.Name.ToLower())
            {
                case "h1":
                    sb.AppendLine($"\n# {GetInnerText(node)}\n");
                    break;
                case "h2":
                    sb.AppendLine($"\n## {GetInnerText(node)}\n");
                    break;
                case "h3":
                    sb.AppendLine($"\n### {GetInnerText(node)}\n");
                    break;
                case "h4":
                    sb.AppendLine($"\n#### {GetInnerText(node)}\n");
                    break;
                case "h5":
                    sb.AppendLine($"\n##### {GetInnerText(node)}\n");
                    break;
                case "h6":
                    sb.AppendLine($"\n###### {GetInnerText(node)}\n");
                    break;
                case "p":
                    sb.AppendLine($"\n{GetInnerText(node)}\n");
                    break;
                case "br":
                    sb.AppendLine();
                    break;
                case "strong":
                case "b":
                    sb.Append($"**{GetInnerText(node)}**");
                    break;
                case "em":
                case "i":
                    sb.Append($"*{GetInnerText(node)}*");
                    break;
                case "code":
                    sb.Append($"`{GetInnerText(node)}`");
                    break;
                case "pre":
                    sb.AppendLine($"\n```\n{GetInnerText(node)}\n```\n");
                    break;
                case "a":
                    var href = node.GetAttributeValue("href", "");
                    var linkText = GetInnerText(node);
                    if (!string.IsNullOrEmpty(href))
                    {
                        sb.Append($"[{linkText}]({href})");
                    }
                    else
                    {
                        sb.Append(linkText);
                    }
                    break;
                case "img":
                    var src = node.GetAttributeValue("src", "");
                    var alt = node.GetAttributeValue("alt", "image");
                    if (!string.IsNullOrEmpty(src))
                    {
                        sb.Append($"![{alt}]({src})");
                    }
                    break;
                case "ul":
                case "ol":
                    sb.AppendLine();
                    ProcessList(node, sb, level, node.Name == "ol");
                    sb.AppendLine();
                    break;
                case "blockquote":
                    sb.AppendLine($"\n> {GetInnerText(node)}\n");
                    break;
                case "hr":
                    sb.AppendLine("\n---\n");
                    break;
                default:
                    foreach (var child in node.ChildNodes)
                    {
                        ProcessNode(child, sb, level);
                    }
                    break;
            }
        }

        private void ProcessList(HtmlNode listNode, StringBuilder sb, int level, bool ordered)
        {
            try
            {
                var items = listNode.SelectNodes(".//li");
                if (items is null || items.Count == 0) return;

                int index = 1;
                foreach (var item in items)
                {
                    var prefix = ordered ? $"{index}. " : "- ";
                    var indent = new string(' ', level * 2);
                    sb.AppendLine($"{indent}{prefix}{GetInnerText(item)}");
                    index++;
                }
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.System.Logger.Log($"WebScraper: Error processing list: {ex.Message}");
            }
        }

        private string GetInnerText(HtmlNode node)
        {
            var text = node.InnerText;
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        private string PostProcessMarkdown(string markdown, string sourceUrl)
        {
            var sb = new StringBuilder();

            // 添加标题和来源
            sb.AppendLine($"# 网页内容\n");
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
                    if (consecutiveEmptyLines <= 2)
                    {
                        sb.AppendLine();
                    }
                }
                else
                {
                    consecutiveEmptyLines = 0;
                    sb.AppendLine(line);
                }
            }

            // 限制长度（避免内容过长）
            var result = sb.ToString();
            if (result.Length > MaxContentLength)
            {
                result = result.Substring(0, MaxContentLength);
                result += "\n\n---\n\n⚠️ 内容过长，已截断。完整内容请访问原网页。";
            }

            return result;
        }
    }
}

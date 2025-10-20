using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace WebSearchPlugin
{
    public class SearchResult
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Snippet { get; set; } = "";
    }

    public class SearchEngine
    {
        private readonly HttpClient _httpClient;

        public SearchEngine(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<Dictionary<string, List<SearchResult>>> SearchMultipleEngines(string query)
        {
            var results = new Dictionary<string, List<SearchResult>>();

            // 并行搜索多个引擎
            var tasks = new List<Task<(string engine, List<SearchResult> results)>>
            {
                Task.Run(async () => ("Bing", await SearchBing(query))),
                Task.Run(async () => ("Baidu", await SearchBaidu(query))),
                Task.Run(async () => ("DuckDuckGo", await SearchDuckDuckGo(query)))
            };

            var completedTasks = await Task.WhenAll(tasks);

            foreach (var (engine, engineResults) in completedTasks)
            {
                if (engineResults.Count > 0)
                {
                    results[engine] = engineResults;
                }
            }

            return results;
        }

        private async Task<List<SearchResult>> SearchBing(string query)
        {
            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var url = $"https://www.bing.com/search?q={encodedQuery}";

                var response = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var results = new List<SearchResult>();

                // Bing 搜索结果选择器
                try
                {
                    var resultNodes = doc.DocumentNode.SelectNodes("//li[@class='b_algo']");
                    if (resultNodes != null && resultNodes.Count > 0)
                    {
                        foreach (var node in resultNodes.Take(10))
                        {
                            try
                            {
                                var titleNode = node.SelectSingleNode(".//h2/a");
                                var snippetNode = node.SelectSingleNode(".//p | .//div[@class='b_caption']/p");

                                if (titleNode != null)
                                {
                                    results.Add(new SearchResult
                                    {
                                        Title = CleanText(titleNode.InnerText),
                                        Url = titleNode.GetAttributeValue("href", ""),
                                        Snippet = snippetNode != null ? CleanText(snippetNode.InnerText) : ""
                                    });
                                }
                            }
                            catch (Exception nodeEx)
                            {
                                VPetLLM.Utils.Logger.Log($"Bing node parse error: {nodeEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception selectEx)
                {
                    VPetLLM.Utils.Logger.Log($"Bing SelectNodes error: {selectEx.Message}");
                }

                return results;
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"Bing search error: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private async Task<List<SearchResult>> SearchBaidu(string query)
        {
            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var url = $"https://www.baidu.com/s?wd={encodedQuery}";

                var response = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var results = new List<SearchResult>();

                // Baidu 搜索结果选择器
                try
                {
                    var resultNodes = doc.DocumentNode.SelectNodes("//div[@class='result c-container xpath-log']");
                    if (resultNodes != null && resultNodes.Count > 0)
                    {
                        foreach (var node in resultNodes.Take(10))
                        {
                            try
                            {
                                var titleNode = node.SelectSingleNode(".//h3/a");
                                var snippetNode = node.SelectSingleNode(".//div[contains(@class, 'c-abstract')]");

                                if (titleNode != null)
                                {
                                    var href = titleNode.GetAttributeValue("href", "");
                                    results.Add(new SearchResult
                                    {
                                        Title = CleanText(titleNode.InnerText),
                                        Url = href,
                                        Snippet = snippetNode != null ? CleanText(snippetNode.InnerText) : ""
                                    });
                                }
                            }
                            catch (Exception nodeEx)
                            {
                                VPetLLM.Utils.Logger.Log($"Baidu node parse error: {nodeEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception selectEx)
                {
                    VPetLLM.Utils.Logger.Log($"Baidu SelectNodes error: {selectEx.Message}");
                }

                return results;
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"Baidu search error: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private async Task<List<SearchResult>> SearchDuckDuckGo(string query)
        {
            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var url = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

                var response = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var results = new List<SearchResult>();

                // DuckDuckGo 搜索结果选择器
                try
                {
                    var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'result')]");
                    if (resultNodes != null && resultNodes.Count > 0)
                    {
                        foreach (var node in resultNodes.Take(10))
                        {
                            try
                            {
                                var titleNode = node.SelectSingleNode(".//a[@class='result__a']");
                                var snippetNode = node.SelectSingleNode(".//a[@class='result__snippet']");

                                if (titleNode != null)
                                {
                                    results.Add(new SearchResult
                                    {
                                        Title = CleanText(titleNode.InnerText),
                                        Url = titleNode.GetAttributeValue("href", ""),
                                        Snippet = snippetNode != null ? CleanText(snippetNode.InnerText) : ""
                                    });
                                }
                            }
                            catch (Exception nodeEx)
                            {
                                VPetLLM.Utils.Logger.Log($"DuckDuckGo node parse error: {nodeEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception selectEx)
                {
                    VPetLLM.Utils.Logger.Log($"DuckDuckGo SelectNodes error: {selectEx.Message}");
                }

                return results;
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"DuckDuckGo search error: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            // 移除多余空白
            text = Regex.Replace(text, @"\s+", " ");
            text = text.Trim();
            
            // HTML 解码
            text = System.Net.WebUtility.HtmlDecode(text);
            
            return text;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VPetLLM.Core;

namespace WebSearchPlugin
{
    public class WebSearchPlugin : IActionPlugin
    {
        public string Name => "WebSearch";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM == null) return "搜索互联网内容或获取网页内容（Markdown格式）";
                switch (_vpetLLM.Settings.Language)
                {
                    case "ja": return "インターネットコンテンツを検索するか、Webページのコンテンツ（Markdown形式）を取得します。";
                    case "zh-hans": return "搜索互联网内容或获取网页内容（Markdown格式）";
                    case "zh-hant": return "搜尋互聯網內容或獲取網頁內容（Markdown格式）";
                    case "en":
                    default: return "Search internet content or fetch webpage content (Markdown format)";
                }
            }
        }
        public string Parameters => "action, query/url";
        public string Examples => "Search: [:plugin(WebSearch(search|AMD 9950HX))]\nFetch: [:plugin(WebSearch(fetch|https://example.com))]";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;
        private HttpClient? _httpClient;
        private WebScraper? _scraper;
        private SearchEngine? _searchEngine;

        public WebSearchPlugin()
        {
            // 延迟初始化，避免构造函数中的异常
        }

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            
            try
            {
                _httpClient = new HttpClient();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                
                _scraper = new WebScraper(_httpClient);
                _searchEngine = new SearchEngine(_httpClient);
                
                VPetLLM.Utils.Logger.Log("WebSearch Plugin Initialized!");
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"WebSearch Plugin Initialization Error: {ex.Message}");
                throw;
            }
        }

        public async Task<string> Function(string arguments)
        {
            try
            {
                if (_searchEngine == null || _scraper == null)
                {
                    return "错误：插件未正确初始化";
                }

                if (string.IsNullOrWhiteSpace(arguments))
                {
                    return "错误：请提供参数。格式：search|关键词 或 fetch|网址";
                }

                var parts = arguments.Split(new[] { '|' }, 2);
                if (parts.Length < 2)
                {
                    return "错误：参数格式不正确。格式：search|关键词 或 fetch|网址";
                }

                var action = parts[0].Trim().ToLower();
                var param = parts[1].Trim();

                _vpetLLM?.Log($"WebSearch: Action={action}, Param={param}");

                switch (action)
                {
                    case "search":
                        return await HandleSearch(param);
                    
                    case "fetch":
                        return await HandleFetch(param);
                    
                    default:
                        return $"错误：未知操作 '{action}'。支持的操作：search, fetch";
                }
            }
            catch (Exception ex)
            {
                _vpetLLM?.Log($"WebSearch Error: {ex.Message}");
                return $"错误：{ex.Message}";
            }
        }

        private async Task<string> HandleSearch(string query)
        {
            if (_searchEngine == null)
            {
                return "错误：搜索引擎未初始化";
            }

            _vpetLLM?.Log($"WebSearch: Searching for '{query}'");
            
            var results = await _searchEngine.SearchMultipleEngines(query);
            
            if (results.Count == 0)
            {
                return "未找到搜索结果";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# 搜索结果：{query}\n");
            
            foreach (var engine in results.Keys)
            {
                sb.AppendLine($"## {engine}\n");
                var engineResults = results[engine];
                
                for (int i = 0; i < Math.Min(5, engineResults.Count); i++)
                {
                    var result = engineResults[i];
                    sb.AppendLine($"{i + 1}. **{result.Title}**");
                    sb.AppendLine($"   - URL: {result.Url}");
                    if (!string.IsNullOrEmpty(result.Snippet))
                    {
                        sb.AppendLine($"   - {result.Snippet}");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("\n---");
            sb.AppendLine("💡 使用 `[:plugin(WebSearch(fetch|网址))]` 获取完整网页内容");

            return sb.ToString();
        }

        private async Task<string> HandleFetch(string url)
        {
            if (_scraper == null)
            {
                return "错误：网页抓取器未初始化";
            }

            _vpetLLM?.Log($"WebSearch: Fetching '{url}'");
            
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"错误：无效的URL '{url}'";
            }

            var markdown = await _scraper.FetchAsMarkdown(url);
            
            if (string.IsNullOrEmpty(markdown))
            {
                return "错误：无法获取网页内容";
            }

            return markdown;
        }

        public void Unload()
        {
            _httpClient?.Dispose();
            VPetLLM.Utils.Logger.Log("WebSearch Plugin Unloaded!");
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }
    }
}

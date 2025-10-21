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
        public string Parameters => "search|query, fetch|url, action(setting)";
        public string Examples => "Examples: `[:plugin(WebSearch(search|AMD 9950HX))]`, `[:plugin(WebSearch(fetch|https://example.com))]`";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;
        private HttpClient? _httpClient;
        private WebScraper? _scraper;
        private SearchEngine? _searchEngine;
        private WebSearchSettings _settings;

        public WebSearchPlugin()
        {
            // 加载设置
            _settings = WebSearchSettings.Load();
        }

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            
            try
            {
                // 根据设置决定使用哪个代理配置
                VPetLLM.Setting.ProxySetting proxyToUse;
                if (_settings.Proxy.UseVPetLLMProxy)
                {
                    // 使用 VPetLLM 的代理配置
                    proxyToUse = plugin.Settings.Proxy;
                    VPetLLM.Utils.Logger.Log("WebSearch: Using VPetLLM proxy settings");
                }
                else if (_settings.Proxy.EnableCustomProxy)
                {
                    // 使用自定义代理配置
                    proxyToUse = new VPetLLM.Setting.ProxySetting
                    {
                        IsEnabled = true,
                        FollowSystemProxy = _settings.Proxy.UseSystemProxy,
                        Protocol = _settings.Proxy.Protocol,
                        Address = _settings.Proxy.Address,
                        ForPlugin = true
                    };
                    VPetLLM.Utils.Logger.Log("WebSearch: Using custom proxy settings");
                }
                else
                {
                    // 不使用代理
                    proxyToUse = new VPetLLM.Setting.ProxySetting { IsEnabled = false };
                    VPetLLM.Utils.Logger.Log("WebSearch: Proxy disabled");
                }

                // 创建 HttpClient，支持代理配置
                _httpClient = CreateHttpClient(proxyToUse);
                
                _scraper = new WebScraper(_httpClient);
                _searchEngine = new SearchEngine(_httpClient);
                
                // 应用设置
                _scraper.MaxContentLength = _settings.MaxContentLength;
                
                var proxyInfo = GetProxyInfo(proxyToUse);
                VPetLLM.Utils.Logger.Log($"WebSearch Plugin Initialized! (Local Mode, Proxy: {proxyInfo})");
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"WebSearch Plugin Initialization Error: {ex.Message}");
                throw;
            }
        }

        // 打开设置窗口
        public void OpenSettings()
        {
            try
            {
                var settingsWindow = new winWebSearchSettings(_settings, OnSettingsSaved);
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"WebSearch: Error opening settings: {ex.Message}");
            }
        }

        private void OnSettingsSaved(WebSearchSettings newSettings)
        {
            _settings = newSettings;
            
            // 应用新设置
            if (_scraper != null)
            {
                _scraper.MaxContentLength = _settings.MaxContentLength;
            }

            VPetLLM.Utils.Logger.Log("WebSearch: Settings applied. Some changes may require plugin reload.");
        }

        private HttpClient CreateHttpClient(VPetLLM.Setting.ProxySetting proxySetting)
        {
            HttpClientHandler handler = new HttpClientHandler();
            
            // 检查是否需要使用代理
            bool useProxy = proxySetting.IsEnabled && 
                           (proxySetting.ForAllAPI || proxySetting.ForPlugin);

            if (useProxy)
            {
                try
                {
                    if (proxySetting.FollowSystemProxy)
                    {
                        // 使用系统代理
                        handler.UseProxy = true;
                        handler.Proxy = System.Net.WebRequest.GetSystemWebProxy();
                        VPetLLM.Utils.Logger.Log("WebSearch: Using system proxy");
                    }
                    else
                    {
                        // 使用自定义代理
                        var proxyUri = new Uri($"{proxySetting.Protocol}://{proxySetting.Address}");
                        handler.Proxy = new System.Net.WebProxy(proxyUri);
                        handler.UseProxy = true;
                        VPetLLM.Utils.Logger.Log($"WebSearch: Using custom proxy: {proxyUri}");
                    }
                }
                catch (Exception ex)
                {
                    VPetLLM.Utils.Logger.Log($"WebSearch: Proxy configuration error: {ex.Message}");
                    handler.UseProxy = false;
                }
            }
            else
            {
                handler.UseProxy = false;
            }

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(30);
            
            return client;
        }

        private string GetProxyInfo(VPetLLM.Setting.ProxySetting proxySetting)
        {
            if (!proxySetting.IsEnabled)
            {
                return "Disabled";
            }

            bool useProxy = proxySetting.ForAllAPI || proxySetting.ForPlugin;
            if (!useProxy)
            {
                return "Not Applied";
            }

            if (proxySetting.FollowSystemProxy)
            {
                return "System Proxy";
            }

            return $"{proxySetting.Protocol}://{proxySetting.Address}";
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

                // 检查是否是 action(setting) 格式的设置命令
                var actionMatch = System.Text.RegularExpressions.Regex.Match(arguments, @"action\((\w+)\)");
                if (actionMatch.Success)
                {
                    var action = actionMatch.Groups[1].Value.ToLower();
                    if (action == "setting")
                    {
                        try
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                var settingWindow = new winWebSearchSettings(_settings, OnSettingsSaved);
                                settingWindow.Show();
                            });
                            return "设置窗口已打开。";
                        }
                        catch (Exception ex)
                        {
                            _vpetLLM?.Log($"WebSearch: Error opening settings: {ex.Message}");
                            return $"打开设置窗口失败: {ex.Message}";
                        }
                    }
                    
                    return "无效的操作。";
                }

                var parts = arguments.Split(new[] { '|' }, 2);
                if (parts.Length < 2)
                {
                    return "错误：参数格式不正确。格式：search|关键词 或 fetch|网址";
                }

                var actionType = parts[0].Trim().ToLower();
                var param = parts[1].Trim();

                _vpetLLM?.Log($"WebSearch: Action={actionType}, Param={param}");

                switch (actionType)
                {
                    case "search":
                        return await HandleSearch(param);
                    
                    case "fetch":
                        return await HandleFetch(param);
                    
                    default:
                        return $"错误：未知操作 '{actionType}'。支持的操作：search, fetch";
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
                
                int maxResults = Math.Min(_settings.MaxSearchResults, engineResults.Count);
                for (int i = 0; i < maxResults; i++)
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

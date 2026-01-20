using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VPetLLM.Core.Abstractions.Interfaces.Plugin;

namespace WebSearchPlugin
{
    public class WebSearchPlugin : IActionPlugin, IPluginWithData
    {
        public string Name => "WebSearch";
        public string Author => "ycxom";
        public string Description
        {
            get
            {
                if (_vpetLLM is null) return "搜索互联网内容或获取网页内容（Markdown格式）";
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
        public string Examples => "Examples: `<|plugin_WebSearch_begin|> search|AMD 9950HX <|plugin_WebSearch_end|>`, `<|plugin_WebSearch_begin|> fetch|https://example.com <|plugin_WebSearch_end|>`";
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string PluginDataDir { get; set; } = "";

        private VPetLLM.VPetLLM? _vpetLLM;
        private HttpClient? _httpClient;
        private WebScraper? _scraper;
        private SearchEngine? _searchEngine;
        private WebSearchSettings _settings;
        private IContentFetcher? _contentFetcher;
        private ulong _steamId;

        public WebSearchPlugin()
        {
            _settings = new WebSearchSettings();
        }

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            
            try { _steamId = plugin.MW?.SteamID ?? 0; } catch { _steamId = 0; }
            
            try
            {
                _settings = WebSearchSettings.Load(PluginDataDir);
                VPetLLM.Setting.ProxySetting proxyToUse;
                
                // 使用内置凭证时不使用代理
                if (_settings.Api.UseApiMode && _settings.Api.UseBuiltInCredentials)
                {
                    proxyToUse = new VPetLLM.Setting.ProxySetting { IsEnabled = false };
                }
                else if (_settings.Proxy.UseVPetLLMProxy)
                {
                    proxyToUse = plugin.Settings.Proxy;
                }
                else if (_settings.Proxy.EnableCustomProxy)
                {
                    proxyToUse = new VPetLLM.Setting.ProxySetting
                    {
                        IsEnabled = true,
                        FollowSystemProxy = _settings.Proxy.UseSystemProxy,
                        Protocol = _settings.Proxy.Protocol,
                        Address = _settings.Proxy.Address,
                        ForPlugin = true
                    };
                }
                else
                {
                    proxyToUse = new VPetLLM.Setting.ProxySetting { IsEnabled = false };
                }

                // 创建 HttpClient，支持代理配置
                _httpClient = CreateHttpClient(proxyToUse);
                
                _scraper = new WebScraper(_httpClient);
                _searchEngine = new SearchEngine(_httpClient);
                
                // 应用设置
                _scraper.MaxContentLength = _settings.MaxContentLength;
                
                // 创建内容抓取器
                CreateContentFetcher();
                
                var proxyInfo = GetProxyInfo(proxyToUse);
                var modeInfo = _settings.Api.UseApiMode ? "API Mode" : "Local Mode";
                VPetLLM.Utils.System.Logger.Log($"WebSearch Plugin Initialized! ({modeInfo}, Proxy: {proxyInfo})");
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.System.Logger.Log($"WebSearch Plugin Initialization Error: {ex.Message}");
                throw;
            }
        }

        public void OpenSettings()
        {
            try
            {
                _settings.SetPluginDataDir(PluginDataDir);
                var settingsWindow = new winWebSearchSettings(_settings, OnSettingsSaved);
                settingsWindow.ShowDialog();
            }
            catch { }
        }

        private void OnSettingsSaved(WebSearchSettings newSettings)
        {
            _settings = newSettings;
            if (_scraper is not null) _scraper.MaxContentLength = _settings.MaxContentLength;
            CreateContentFetcher();
        }

        private void CreateContentFetcher()
        {
            if (_httpClient is null || _scraper is null) return;
            var localFetcher = new LocalContentFetcher(_scraper);
            if (_settings.Api.UseApiMode)
            {
                _contentFetcher = new ApiContentFetcher(_httpClient, _settings.Api.GetEffectiveApiUrl(),
                    _settings.Api.GetEffectiveToken(), _steamId, GetAuthKeyAsync, localFetcher, 
                    _settings.Api.EnableFallback, _settings.Api.UseBuiltInCredentials);
            }
            else
            {
                _contentFetcher = localFetcher;
            }
        }

        private async Task<int> GetAuthKeyAsync()
        {
            try { if (_vpetLLM?.MW is not null) return await _vpetLLM.MW.GenerateAuthKey(); } catch { }
            return 0;
        }

        private HttpClient CreateHttpClient(VPetLLM.Setting.ProxySetting proxySetting)
        {
            HttpClientHandler handler = new HttpClientHandler();
            bool useProxy = proxySetting.IsEnabled && (proxySetting.ForAllAPI || proxySetting.ForPlugin);
            if (useProxy)
            {
                try
                {
                    if (proxySetting.FollowSystemProxy)
                    {
                        handler.UseProxy = true;
                        handler.Proxy = System.Net.WebRequest.GetSystemWebProxy();
                    }
                    else
                    {
                        handler.Proxy = new System.Net.WebProxy(new Uri($"{proxySetting.Protocol}://{proxySetting.Address}"));
                        handler.UseProxy = true;
                    }
                }
                catch { handler.UseProxy = false; }
            }
            else { handler.UseProxy = false; }
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
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
                if (_searchEngine is null || _scraper is null)
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
                                _settings.SetPluginDataDir(PluginDataDir);
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
            _vpetLLM?.Log($"WebSearch: Searching for '{query}'");

            // API 模式：通过 API 获取 Bing 搜索结果
            if (_settings.Api.UseApiMode && _contentFetcher is not null)
            {
                return await HandleSearchViaApi(query);
            }

            // 本地模式：使用本地搜索引擎
            return await HandleSearchLocal(query);
        }

        private async Task<string> HandleSearchViaApi(string query)
        {
            // 构建 Bing 搜索 URL
            var encodedQuery = Uri.EscapeDataString(query);
            var searchUrl = $"https://www.bing.com/search?q={encodedQuery}";

            _vpetLLM?.Log($"WebSearch: Searching via API: {searchUrl}");

            var result = await _contentFetcher!.FetchAsync(searchUrl);

            if (!result.Success)
            {
                // API 失败，尝试本地搜索
                if (_settings.Api.EnableFallback)
                {
                    _vpetLLM?.Log($"WebSearch: API search failed, falling back to local search");
                    return await HandleSearchLocal(query);
                }
                return $"错误：{result.ErrorMessage}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# 搜索结果：{query}\n");
            
            if (result.UsedFallback)
            {
                sb.AppendLine("⚠️ API 模式失败，已降级到本地模式\n");
            }
            
            sb.Append(result.Content);
            
            sb.AppendLine("\n\n---");
            sb.AppendLine("💡 使用 `<|plugin_WebSearch_begin|> fetch|网址 <|plugin_WebSearch_end|>` 获取完整网页内容");

            _vpetLLM?.Log($"WebSearch: Search completed via API (Mode: {result.Mode})");
            return sb.ToString();
        }

        private async Task<string> HandleSearchLocal(string query)
        {
            if (_searchEngine is null)
            {
                return "错误：搜索引擎未初始化";
            }

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
            sb.AppendLine("💡 使用 `<|plugin_WebSearch_begin|> fetch|网址 <|plugin_WebSearch_end|>` 获取完整网页内容");

            return sb.ToString();
        }

        private async Task<string> HandleFetch(string url)
        {
            if (_contentFetcher is null)
            {
                return "错误：内容抓取器未初始化";
            }

            _vpetLLM?.Log($"WebSearch: Fetching '{url}'");
            
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"错误：无效的URL '{url}'";
            }

            var result = await _contentFetcher.FetchAsync(url);
            
            if (!result.Success)
            {
                return $"错误：{result.ErrorMessage}";
            }

            // 添加模式信息到返回内容
            var sb = new StringBuilder();
            if (result.UsedFallback)
            {
                sb.AppendLine($"⚠️ API 模式失败，已降级到本地模式\n");
            }
            sb.Append(result.Content);
            
            _vpetLLM?.Log($"WebSearch: Fetch completed (Mode: {result.Mode}, Fallback: {result.UsedFallback})");

            return sb.ToString();
        }

        public void Unload()
        {
            _httpClient?.Dispose();
            VPetLLM.Utils.System.Logger.Log("WebSearch Plugin Unloaded!");
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }
    }
}

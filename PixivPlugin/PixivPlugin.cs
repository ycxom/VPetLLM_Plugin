using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Newtonsoft.Json;
using VPetLLM.Core;
using PixivPlugin.Models;
using PixivPlugin.Services;

namespace PixivPlugin
{
    public class PixivPlugin : IVPetLLMPlugin, IActionPlugin, IPluginWithData
    {
        public string Name => "Pixiv";
        public string Author => "ycxom";

        public string Description
        {
            get
            {
                if (_vpetLLM == null)
                    return "搜索 Pixiv 图片或获取随机推荐图片。";

                return _vpetLLM.Settings.Language switch
                {
                    "ja" => "Pixiv画像を検索したり、ランダムなおすすめ画像を取得します。",
                    "zh-hant" => "搜尋 Pixiv 圖片或取得隨機推薦圖片。",
                    "en" => "Search Pixiv images or get random recommended images.",
                    _ => "搜索 Pixiv 图片或获取随机推荐图片。"
                };
            }
        }

        public string Parameters => "action(string: search/random/setting), keyword(string, required for search), page(int, optional, default 1)";

        public string Examples => @"搜索图片: `<|plugin_Pixiv_begin|> action(search), keyword(小鸟游星野) <|plugin_Pixiv_end|>`
搜索第2页: `<|plugin_Pixiv_begin|> action(search), keyword(小鸟游星野), page(2) <|plugin_Pixiv_end|>`
随机推荐: `<|plugin_Pixiv_begin|> action(random) <|plugin_Pixiv_end|>`";

        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = string.Empty;
        public string PluginDataDir { get; set; } = string.Empty;

        private VPetLLM.VPetLLM? _vpetLLM;
        private PluginSettings _settings = new();
        private PixivApiService? _apiService;
        private ImageLoader? _imageLoader;
        private ImageProxyService? _imageProxyService;
        private ulong _steamId;

        private const string SettingsFileName = "PixivPlugin.json";

        public void Initialize(VPetLLM.VPetLLM plugin)
        {
            _vpetLLM = plugin;
            // 注意：不要覆盖 FilePath，PluginManager 已经正确设置了 DLL 文件路径

            try { _steamId = plugin.MW?.SteamID ?? 0; } catch { _steamId = 0; }

            LoadSettings();

            _apiService = new PixivApiService(_steamId, GetAuthKeyAsync);
            _apiService.SetTimeout(_settings.TimeoutSeconds);

            _imageLoader = new ImageLoader();
            _imageProxyService = new ImageProxyService(_settings);
            _imageLoader.SetImageProxyService(_imageProxyService);
            ApplyProxySettings();

            VPetLLM.Utils.Logger.Log("Pixiv Plugin Initialized!");
        }

        private async Task<int> GetAuthKeyAsync()
        {
            try { if (_vpetLLM?.MW != null) return await _vpetLLM.MW.GenerateAuthKey(); } catch { }
            return 0;
        }

        public async Task<string> Function(string arguments)
        {
            try
            {
                // 解析 action 参数
                var actionMatch = Regex.Match(arguments, @"action\((\w+)\)");
                var action = actionMatch.Success ? actionMatch.Groups[1].Value.ToLower() : "random";

                // 打开设置窗口
                if (action == "setting")
                {
                    OpenSettingsWindow();
                    return "设置窗口已打开。";
                }

                // 搜索图片
                if (action == "search")
                {
                    var keywordMatch = Regex.Match(arguments, @"keyword\(([^)]+)\)");
                    var keyword = keywordMatch.Success ? keywordMatch.Groups[1].Value.Trim('"', ' ') : "";

                    if (!PixivApiService.ValidateKeyword(keyword))
                    {
                        return "请提供搜索关键词。";
                    }

                    // 解析页码参数，默认为1
                    var pageMatch = Regex.Match(arguments, @"page\((\d+)\)");
                    var page = pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out var p) ? Math.Max(1, p) : 1;

                    return await SearchAndShowAsync(keyword, page);
                }

                // 随机推荐
                return await RandomAndShowAsync();
            }
            catch (Exception ex)
            {
                Log($"Pixiv Plugin Error: {ex.Message}");
                return $"操作失败: {ex.Message}";
            }
        }

        private async Task<string> SearchAndShowAsync(string keyword, int page = 1)
        {
            if (_apiService == null || _imageLoader == null)
                return "插件未正确初始化。";

            var response = await _apiService.SearchAsync(keyword, page);
            
            if (response?.Illusts == null || response.Illusts.Count == 0)
            {
                return page > 1 
                    ? $"第 {page} 页没有更多与 \"{keyword}\" 相关的图片了。"
                    : $"未找到与 \"{keyword}\" 相关的图片。";
            }

            ShowPreviewWindow(response.Illusts);
            return $"找到 {response.Illusts.Count} 张图片（第 {page} 页），已打开预览窗口。";
        }

        private async Task<string> RandomAndShowAsync()
        {
            if (_apiService == null || _imageLoader == null)
                return "插件未正确初始化。";

            var illust = await _apiService.GetRandomRankingImageAsync();
            
            if (illust == null)
            {
                return "获取推荐图片失败，请稍后重试。";
            }

            ShowPreviewWindow(illust);
            return $"为您推荐: {illust.Title} - {illust.User.Name}";
        }

        private void ShowPreviewWindow(List<PixivIllust> illusts)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var window = new winPixivPreview(illusts, _imageLoader!, _settings.DownloadPath);
                window.Show();
            });
        }

        private void ShowPreviewWindow(PixivIllust illust)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var window = new winPixivPreview(illust, _imageLoader!, _settings.DownloadPath);
                window.Show();
            });
        }

        private void OpenSettingsWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var window = new winPixivSetting(this);
                window.Show();
            });
        }

        public void Unload()
        {
            SaveSettings();
            _imageLoader?.Dispose();
            VPetLLM.Utils.Logger.Log("Pixiv Plugin Unloaded!");
        }

        public void Log(string message)
        {
            _vpetLLM?.Log(message);
        }

        #region Settings Management

        public void LoadSettings()
        {
            if (string.IsNullOrEmpty(PluginDataDir))
                return;

            var path = Path.Combine(PluginDataDir, SettingsFileName);
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    _settings = JsonConvert.DeserializeObject<PluginSettings>(json) ?? new PluginSettings();
                }
                catch
                {
                    _settings = new PluginSettings();
                }
            }
        }

        public void SaveSettings()
        {
            if (string.IsNullOrEmpty(PluginDataDir))
                return;

            var path = Path.Combine(PluginDataDir, SettingsFileName);
            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Log($"Failed to save settings: {ex.Message}");
            }
        }

        public PluginSettings GetSettings() => _settings;

        public void UpdateSettings(PluginSettings settings)
        {
            _settings = settings;
            _imageProxyService?.UpdateSettings(settings);
            ApplyProxySettings();
            SaveSettings();
        }

        private void ApplyProxySettings()
        {
            if (_imageLoader == null)
                return;

            if (!_settings.UseProxy)
            {
                _imageLoader.SetProxy(null);
                return;
            }

            // 跟随 VPetLLM 代理设置
            if (_settings.FollowVPetLLMProxy)
            {
                var vpetProxy = _vpetLLM?.Settings?.Proxy;
                if (vpetProxy != null && vpetProxy.IsEnabled)
                {
                    // 如果跟随系统代理，则不设置自定义代理（让系统处理）
                    if (vpetProxy.FollowSystemProxy)
                    {
                        _imageLoader.SetProxy(null);
                        Log("Pixiv Plugin: Following system proxy");
                    }
                    else if (!string.IsNullOrEmpty(vpetProxy.Address))
                    {
                        var proxyUrl = $"{vpetProxy.Protocol}://{vpetProxy.Address}";
                        _imageLoader.SetProxy(proxyUrl);
                        Log($"Pixiv Plugin: Using VPetLLM proxy: {proxyUrl}");
                    }
                    else
                    {
                        _imageLoader.SetProxy(null);
                        Log("Pixiv Plugin: VPetLLM proxy address is empty");
                    }
                }
                else
                {
                    _imageLoader.SetProxy(null);
                    Log("Pixiv Plugin: VPetLLM proxy not enabled");
                }
                return;
            }

            // 使用自定义代理设置
            if (!string.IsNullOrEmpty(_settings.ProxyUrl))
            {
                _imageLoader.SetProxy(_settings.ProxyUrl);
            }
        }

        #endregion
    }
}

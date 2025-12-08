using System;
using System.IO;
using Newtonsoft.Json;

namespace WebSearchPlugin
{
    public class WebSearchSettings
    {
        private const string SettingFileName = "WebSearchSettings.json";
        
        // 用于保存时记住数据目录
        [JsonIgnore]
        private string _pluginDataDir = "";

        // 代理设置
        public ProxySettings Proxy { get; set; } = new ProxySettings();

        // 搜索设置
        public int MaxSearchResults { get; set; } = 5;
        public int MaxContentLength { get; set; } = 20000;

        // API 设置
        public ApiSettings Api { get; set; } = new ApiSettings();

        public class ProxySettings
        {
            public bool UseVPetLLMProxy { get; set; } = true;  // 使用 VPetLLM 的代理配置
            public bool EnableCustomProxy { get; set; } = false;  // 启用自定义代理
            public bool UseSystemProxy { get; set; } = false;
            public string Protocol { get; set; } = "http";
            public string Address { get; set; } = "127.0.0.1:7890";
        }

        public class ApiSettings
        {
            public bool UseApiMode { get; set; } = false;  // 是否使用 API 模式
            public bool UseBuiltInCredentials { get; set; } = true;  // 使用内置凭证
            public string ApiUrl { get; set; } = "";  // 自定义 API 地址
            public string BearerToken { get; set; } = "";  // 自定义 Bearer Token
            public bool EnableFallback { get; set; } = true;  // API 失败时是否降级到本地模式

            /// <summary>
            /// 获取实际使用的 API 地址
            /// </summary>
            public string GetEffectiveApiUrl()
            {
                return UseBuiltInCredentials ? ApiCredentials.GetBuiltInApiUrl() : ApiUrl;
            }

            /// <summary>
            /// 获取实际使用的 Bearer Token
            /// </summary>
            public string GetEffectiveToken()
            {
                return UseBuiltInCredentials ? ApiCredentials.GetBuiltInToken() : BearerToken;
            }
        }

        private static string GetSettingsPath(string pluginDataDir)
        {
            if (!string.IsNullOrEmpty(pluginDataDir))
            {
                return Path.Combine(pluginDataDir, SettingFileName);
            }
            // 回退到程序集目录
            var pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            return Path.Combine(pluginDir ?? "", SettingFileName);
        }

        public static WebSearchSettings Load(string pluginDataDir = "")
        {
            try
            {
                var path = GetSettingsPath(pluginDataDir);
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var settings = JsonConvert.DeserializeObject<WebSearchSettings>(json);
                    if (settings != null)
                    {
                        settings._pluginDataDir = pluginDataDir;
                        VPetLLM.Utils.Logger.Log($"WebSearch: Settings loaded from {path}");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"WebSearch: Error loading settings: {ex.Message}");
            }

            VPetLLM.Utils.Logger.Log("WebSearch: Using default settings");
            var defaultSettings = new WebSearchSettings();
            defaultSettings._pluginDataDir = pluginDataDir;
            return defaultSettings;
        }

        public void Save()
        {
            try
            {
                var path = GetSettingsPath(_pluginDataDir);
                
                // 确保目录存在
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
                VPetLLM.Utils.Logger.Log($"WebSearch: Settings saved to {path}");
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"WebSearch: Error saving settings: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置插件数据目录（用于从设置窗口保存时）
        /// </summary>
        public void SetPluginDataDir(string pluginDataDir)
        {
            _pluginDataDir = pluginDataDir;
        }
    }
}

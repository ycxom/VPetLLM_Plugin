using System;
using System.IO;
using Newtonsoft.Json;

namespace WebSearchPlugin
{
    public class WebSearchSettings
    {
        // 代理设置
        public ProxySettings Proxy { get; set; } = new ProxySettings();

        // 搜索设置
        public int MaxSearchResults { get; set; } = 5;
        public int MaxContentLength { get; set; } = 20000;

        public class ProxySettings
        {
            public bool UseVPetLLMProxy { get; set; } = true;  // 使用 VPetLLM 的代理配置
            public bool EnableCustomProxy { get; set; } = false;  // 启用自定义代理
            public bool UseSystemProxy { get; set; } = false;
            public string Protocol { get; set; } = "http";
            public string Address { get; set; } = "127.0.0.1:7890";
        }

        private static string GetSettingsPath()
        {
            var pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            return Path.Combine(pluginDir, "WebSearchSettings.json");
        }

        public static WebSearchSettings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var settings = JsonConvert.DeserializeObject<WebSearchSettings>(json);
                    if (settings != null)
                    {
                        VPetLLM.Utils.Logger.Log("WebSearch: Settings loaded from file");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"WebSearch: Error loading settings: {ex.Message}");
            }

            VPetLLM.Utils.Logger.Log("WebSearch: Using default settings");
            return new WebSearchSettings();
        }

        public void Save()
        {
            try
            {
                var path = GetSettingsPath();
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
                VPetLLM.Utils.Logger.Log("WebSearch: Settings saved");
            }
            catch (Exception ex)
            {
                VPetLLM.Utils.Logger.Log($"WebSearch: Error saving settings: {ex.Message}");
            }
        }
    }
}

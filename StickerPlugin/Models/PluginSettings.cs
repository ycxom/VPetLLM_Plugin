using System.IO;
using Newtonsoft.Json;
using VPetLLM.Infrastructure.Configuration;

namespace StickerPlugin.Models
{
    /// <summary>
    /// 插件设置
    /// </summary>
    public class PluginSettings
    {
        private const string SettingsFileName = "StickerPlugin.json";

        // 是否使用内置凭证
        [JsonProperty("useBuiltInCredentials")]
        public bool UseBuiltInCredentials { get; set; } = true;

        // 服务配置（自定义时使用）
        [JsonProperty("serviceUrl")]
        public string ServiceUrl { get; set; } = "";

        [JsonProperty("apiKey")]
        public string ApiKey { get; set; } = "";

        /// <summary>
        /// 获取实际使用的服务地址
        /// </summary>
        public string GetEffectiveServiceUrl()
        {
            return UseBuiltInCredentials ? global::StickerPlugin.ApiCredentials.GetBuiltInServiceUrl() : ServiceUrl;
        }

        /// <summary>
        /// 获取实际使用的 API Key
        /// </summary>
        public string GetEffectiveApiKey()
        {
            return UseBuiltInCredentials ? global::StickerPlugin.ApiCredentials.GetBuiltInApiKey() : ApiKey;
        }

        // 标签配置
        [JsonProperty("tagCount")]
        public int TagCount { get; set; } = 10;

        [JsonProperty("cacheDurationMinutes")]
        public int CacheDurationMinutes { get; set; } = 5;

        // 显示配置
        [JsonProperty("displayDurationSeconds")]
        public int DisplayDurationSeconds { get; set; } = 6;

        /// <summary>
        /// 验证并修正设置值到有效范围
        /// </summary>
        /// <param name="totalAvailableTags">可用标签总数，用于限制 TagCount 上限</param>
        public void Validate(int totalAvailableTags = int.MaxValue)
        {
            // TagCount: 最小 1，最大为可用标签数或 100
            if (TagCount < 1)
                TagCount = 1;
            if (TagCount > totalAvailableTags)
                TagCount = totalAvailableTags;
            if (TagCount > 100)
                TagCount = 100;

            // DisplayDurationSeconds: 1-60 秒
            if (DisplayDurationSeconds < 1)
                DisplayDurationSeconds = 1;
            if (DisplayDurationSeconds > 60)
                DisplayDurationSeconds = 60;

            // CacheDurationMinutes: 最小 1 分钟
            if (CacheDurationMinutes < 1)
                CacheDurationMinutes = 1;

            // 自定义凭证时验证 ServiceUrl
            if (!UseBuiltInCredentials && string.IsNullOrWhiteSpace(ServiceUrl))
                ServiceUrl = "";
        }

        /// <summary>
        /// 从文件加载设置
        /// </summary>
        public static PluginSettings Load(string pluginDataDir)
        {
            // pluginDataDir 参数保留以兼容，但不再使用
            var settings = PluginConfigHelper.Load<PluginSettings>("Sticker");
            
            // 迁移：如果使用内置凭证，清除自定义凭证字段（防止显示旧的内置值）
            if (settings.UseBuiltInCredentials)
            {
                settings.ServiceUrl = "";
                settings.ApiKey = "";
            }
            
            return settings;
        }

        /// <summary>
        /// 保存设置到文件
        /// </summary>
        public void Save(string pluginDataDir)
        {
            // pluginDataDir 参数保留以兼容，但不再使用
            PluginConfigHelper.Save("Sticker", this);
        }

        /// <summary>
        /// 创建设置的深拷贝
        /// </summary>
        public PluginSettings Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<PluginSettings>(json) ?? new PluginSettings();
        }
    }
}

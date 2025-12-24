using System.IO;
using Newtonsoft.Json;

namespace StickerPlugin.Models
{
    /// <summary>
    /// 插件设置
    /// </summary>
    public class PluginSettings
    {
        private const string SettingsFileName = "StickerPlugin.json";
        private const string ImagePluginDllName = "VPet.Plugin.Imgae.dll";
        private const string WorkshopItemId = "3027023665"; // Steam Workshop ID

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

        // DLL 路径（空字符串表示自动查找）
        [JsonProperty("imagePluginDllPath")]
        public string ImagePluginDllPath { get; set; } = string.Empty;

        /// <summary>
        /// 获取有效的 DLL 路径（自动查找或使用配置值）
        /// </summary>
        /// <param name="modPaths">VPet 已加载的 MOD 路径列表（可选）</param>
        public string GetEffectiveDllPath(IEnumerable<DirectoryInfo>? modPaths = null)
        {
            // 如果已配置且文件存在，直接返回
            if (!string.IsNullOrEmpty(ImagePluginDllPath) && File.Exists(ImagePluginDllPath))
            {
                return ImagePluginDllPath;
            }

            // 优先从 VPet MODPath 查找（最快最准确）
            if (modPaths != null)
            {
                var dllPath = FindImagePluginDllFromModPaths(modPaths);
                if (!string.IsNullOrEmpty(dllPath))
                {
                    return dllPath;
                }
            }

            // 后备：智能查找 Steam 路径
            return FindImagePluginDll() ?? ImagePluginDllPath;
        }

        /// <summary>
        /// 从 VPet 已加载的 MOD 路径中查找 DLL
        /// </summary>
        /// <param name="modPaths">VPet 的 MODPath 列表</param>
        public static string? FindImagePluginDllFromModPaths(IEnumerable<DirectoryInfo> modPaths)
        {
            foreach (var modDir in modPaths)
            {
                // 检查是否是目标插件目录（通过 Workshop ID 或目录名判断）
                if (modDir.FullName.Contains(WorkshopItemId) || 
                    modDir.Name.Equals("VPet.Plugin.Imgae", StringComparison.OrdinalIgnoreCase))
                {
                    var dllPath = Path.Combine(modDir.FullName, "plugin", ImagePluginDllName);
                    if (File.Exists(dllPath))
                    {
                        return dllPath;
                    }

                    // 也检查根目录
                    dllPath = Path.Combine(modDir.FullName, ImagePluginDllName);
                    if (File.Exists(dllPath))
                    {
                        return dllPath;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 智能查找 VPet.Plugin.Imgae.dll
        /// 优先级：注册表 Steam 路径 > libraryfolders.vdf 中的库路径 > 常见硬编码路径
        /// </summary>
        public static string? FindImagePluginDll()
        {
            var searchPaths = new List<string>();

            // 1. 通过注册表查找 Steam 主安装路径
            string? mainSteamPath = null;
            try
            {
                mainSteamPath = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                    "InstallPath",
                    null) as string;

                // 备用注册表路径（32位系统或其他情况）
                if (string.IsNullOrEmpty(mainSteamPath))
                {
                    mainSteamPath = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                        "InstallPath",
                        null) as string;
                }

                // 当前用户注册表
                if (string.IsNullOrEmpty(mainSteamPath))
                {
                    mainSteamPath = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam",
                        "SteamPath",
                        null) as string;
                }

                if (!string.IsNullOrEmpty(mainSteamPath))
                {
                    AddWorkshopPath(searchPaths, mainSteamPath);
                }
            }
            catch
            {
                // 忽略注册表访问错误
            }

            // 2. 解析 libraryfolders.vdf 获取所有 Steam 库路径
            if (!string.IsNullOrEmpty(mainSteamPath))
            {
                try
                {
                    var libraryFoldersPath = Path.Combine(mainSteamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraryFoldersPath))
                    {
                        var libraryPaths = ParseLibraryFolders(libraryFoldersPath);
                        foreach (var libPath in libraryPaths)
                        {
                            if (libPath != mainSteamPath) // 避免重复
                            {
                                AddWorkshopPath(searchPaths, libPath);
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略解析错误
                }
            }

            // 3. 常见硬编码路径作为后备
            var fallbackPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                @"D:\Steam",
                @"D:\SteamLibrary",
                @"E:\Steam",
                @"E:\SteamLibrary",
                @"F:\Steam",
                @"F:\SteamLibrary",
                @"G:\Steam",
                @"G:\SteamLibrary"
            };

            foreach (var steamPath in fallbackPaths)
            {
                AddWorkshopPath(searchPaths, steamPath);
            }

            // 4. 查找第一个存在的路径
            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// 添加 Workshop 路径到搜索列表（避免重复）
        /// </summary>
        private static void AddWorkshopPath(List<string> searchPaths, string steamPath)
        {
            var workshopPath = Path.Combine(steamPath, "steamapps", "workshop", "content", "1920960", WorkshopItemId, "plugin", ImagePluginDllName);
            if (!searchPaths.Contains(workshopPath))
            {
                searchPaths.Add(workshopPath);
            }
        }

        /// <summary>
        /// 解析 libraryfolders.vdf 获取所有 Steam 库路径
        /// </summary>
        private static List<string> ParseLibraryFolders(string vdfPath)
        {
            var paths = new List<string>();
            
            try
            {
                var content = File.ReadAllText(vdfPath);
                
                // 匹配 "path" 字段，格式如: "path"		"D:\\SteamLibrary"
                var pathRegex = new System.Text.RegularExpressions.Regex(
                    "\"path\"\\s+\"([^\"]+)\"",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                var matches = pathRegex.Matches(content);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var path = match.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(path))
                        {
                            paths.Add(path);
                        }
                    }
                }
            }
            catch
            {
                // 忽略解析错误
            }

            return paths;
        }

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
            if (string.IsNullOrEmpty(pluginDataDir))
                return new PluginSettings();

            var path = Path.Combine(pluginDataDir, SettingsFileName);
            if (!File.Exists(path))
                return new PluginSettings();

            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonConvert.DeserializeObject<PluginSettings>(json);
                if (settings == null)
                    return new PluginSettings();

                // 迁移：如果使用内置凭证，清除自定义凭证字段（防止显示旧的内置值）
                if (settings.UseBuiltInCredentials)
                {
                    settings.ServiceUrl = "";
                    settings.ApiKey = "";
                }

                return settings;
            }
            catch
            {
                return new PluginSettings();
            }
        }

        /// <summary>
        /// 保存设置到文件
        /// </summary>
        public void Save(string pluginDataDir)
        {
            if (string.IsNullOrEmpty(pluginDataDir))
                return;

            try
            {
                if (!Directory.Exists(pluginDataDir))
                    Directory.CreateDirectory(pluginDataDir);

                var path = Path.Combine(pluginDataDir, SettingsFileName);
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch
            {
                // 静默失败，日志由调用方处理
            }
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

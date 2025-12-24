using System;
using System.Text;

namespace StickerPlugin
{
    internal static class ApiCredentials
    {
        private static readonly string _obfuscatedUrl = "aHR0cHM6Ly9haS55Y3hvbS50b3A6MzAyMA==";
        private static readonly string _obfuscatedKey = "VlBldExMTS15Y3hvbS1JTUFHRV9WRUNUT1I=";

        /// <summary>
        /// 获取内置的服务地址
        /// </summary>
        public static string GetBuiltInServiceUrl()
        {
            return Deobfuscate(_obfuscatedUrl);
        }

        /// <summary>
        /// 获取内置的 API Key
        /// </summary>
        public static string GetBuiltInApiKey()
        {
            return Deobfuscate(_obfuscatedKey);
        }

        /// <summary>
        /// 检查是否使用内置凭证
        /// </summary>
        public static bool IsUsingBuiltIn(string url, string key)
        {
            return string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key) ||
                   url == GetBuiltInServiceUrl() || key == GetBuiltInApiKey();
        }

        /// <summary>
        /// </summary>
        internal static string Obfuscate(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
        }

        private static string Deobfuscate(string obfuscated)
        {
            if (string.IsNullOrEmpty(obfuscated)) return "";

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(obfuscated));
            }
            catch
            {
                return "";
            }
        }
    }
}

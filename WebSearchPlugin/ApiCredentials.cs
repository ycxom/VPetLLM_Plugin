using System;
using System.Text;

namespace WebSearchPlugin
{
    internal static class ApiCredentials
    {
        private static readonly string _obfuscatedUrl = "aHR0cHM6Ly9haS55Y3hvbS50b3A6NDQ1Ny9leHRyYWN0";
        private static readonly string _obfuscatedToken = "YzNlOTZkZDdkN2JmY2JjYWY5NzY1NGJkMDM0NjUwZWQ=";

        /// <summary>
        /// 获取内置的 API 地址
        /// </summary>
        public static string GetBuiltInApiUrl()
        {
            return Deobfuscate(_obfuscatedUrl);
        }

        /// <summary>
        /// 获取内置的 Bearer Token
        /// </summary>
        public static string GetBuiltInToken()
        {
            return Deobfuscate(_obfuscatedToken);
        }

        /// <summary>
        /// 检查是否使用内置凭证
        /// </summary>
        public static bool IsUsingBuiltIn(string url, string token)
        {
            return string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token) ||
                   url == GetBuiltInApiUrl() || token == GetBuiltInToken();
        }

        /// <summary>
        /// 混淆字符串（用于生成混淆值）
        /// </summary>
        internal static string Obfuscate(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            
            // 字符偏移
            var shifted = new StringBuilder();
            foreach (char c in input)
            {
                shifted.Append((char)(c + 1));
            }
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(shifted.ToString()));
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

using System;
using System.Collections.Generic;
using System.Linq;
using WeatherPlugin.Data;

namespace WeatherPlugin.Search
{
    /// <summary>
    /// 城市向量搜索，使用编辑距离进行模糊匹配
    /// </summary>
    public class CityVectorSearch
    {
        private readonly CityDatabase _database;

        public CityVectorSearch(CityDatabase database)
        {
            _database = database;
        }

        /// <summary>
        /// 查找最佳匹配的城市
        /// </summary>
        /// <param name="cityName">输入的城市名称</param>
        /// <returns>匹配的 Adcode，如果未找到返回 null</returns>
        public int? FindBestMatch(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName))
                return null;

            cityName = cityName.Trim();

            // 首先尝试精确匹配
            var exactMatch = _database.GetAdcode(cityName);
            if (exactMatch.HasValue)
                return exactMatch;

            // 尝试添加常见后缀匹配
            var suffixes = new[] { "市", "区", "县" };
            foreach (var suffix in suffixes)
            {
                var withSuffix = cityName + suffix;
                var match = _database.GetAdcode(withSuffix);
                if (match.HasValue)
                    return match;
            }

            // 使用模糊匹配
            var topMatches = FindTopMatches(cityName, 1);
            if (topMatches.Count > 0 && topMatches[0].score >= 0.6)
            {
                return topMatches[0].adcode;
            }

            return null;
        }

        /// <summary>
        /// 查找前 K 个最佳匹配
        /// </summary>
        /// <param name="cityName">输入的城市名称</param>
        /// <param name="topK">返回的最大数量</param>
        /// <returns>匹配结果列表，按相似度降序排列</returns>
        public List<(string city, int adcode, double score)> FindTopMatches(string cityName, int topK = 5)
        {
            if (string.IsNullOrWhiteSpace(cityName))
                return new List<(string, int, double)>();

            cityName = cityName.Trim();

            var results = new List<(string city, int adcode, double score)>();

            foreach (var kvp in _database.CityToAdcode)
            {
                var score = CalculateSimilarity(cityName, kvp.Key);
                results.Add((kvp.Key, kvp.Value, score));
            }

            return results
                .OrderByDescending(r => r.score)
                .Take(topK)
                .ToList();
        }

        /// <summary>
        /// 计算两个字符串的相似度（0-1之间）
        /// </summary>
        private double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0;

            // 如果完全相等
            if (source == target)
                return 1.0;

            // 如果一个包含另一个
            if (target.Contains(source) || source.Contains(target))
            {
                var shorter = source.Length < target.Length ? source : target;
                var longer = source.Length >= target.Length ? source : target;
                return (double)shorter.Length / longer.Length * 0.95;
            }

            // 使用编辑距离计算相似度
            var distance = LevenshteinDistance(source, target);
            var maxLength = Math.Max(source.Length, target.Length);
            
            return 1.0 - (double)distance / maxLength;
        }

        /// <summary>
        /// 计算 Levenshtein 编辑距离
        /// </summary>
        private int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
                return target?.Length ?? 0;
            if (string.IsNullOrEmpty(target))
                return source.Length;

            var sourceLength = source.Length;
            var targetLength = target.Length;

            var matrix = new int[sourceLength + 1, targetLength + 1];

            // 初始化第一列
            for (var i = 0; i <= sourceLength; i++)
                matrix[i, 0] = i;

            // 初始化第一行
            for (var j = 0; j <= targetLength; j++)
                matrix[0, j] = j;

            // 填充矩阵
            for (var i = 1; i <= sourceLength; i++)
            {
                for (var j = 1; j <= targetLength; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;

                    matrix[i, j] = Math.Min(
                        Math.Min(
                            matrix[i - 1, j] + 1,      // 删除
                            matrix[i, j - 1] + 1),     // 插入
                        matrix[i - 1, j - 1] + cost);  // 替换
                }
            }

            return matrix[sourceLength, targetLength];
        }

        /// <summary>
        /// 检查城市名是否为非中国地区
        /// </summary>
        public bool IsNonChinaRegion(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName))
                return false;

            // 常见的非中国地区关键词
            var nonChinaKeywords = new[]
            {
                "东京", "大阪", "京都", "首尔", "釜山", "曼谷", "新加坡",
                "纽约", "洛杉矶", "伦敦", "巴黎", "柏林", "悉尼", "墨尔本",
                "Tokyo", "Osaka", "Seoul", "Bangkok", "Singapore",
                "New York", "Los Angeles", "London", "Paris", "Berlin", "Sydney"
            };

            return nonChinaKeywords.Any(k => 
                cityName.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
    }
}

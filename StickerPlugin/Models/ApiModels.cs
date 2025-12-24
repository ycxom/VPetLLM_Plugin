using Newtonsoft.Json;

namespace StickerPlugin.Models
{
    /// <summary>
    /// 搜索请求
    /// </summary>
    public class SearchRequest
    {
        [JsonProperty("query")]
        public string Query { get; set; } = string.Empty;

        [JsonProperty("limit")]
        public int Limit { get; set; } = 1;

        [JsonProperty("minScore")]
        public double MinScore { get; set; } = 0.2;

        [JsonProperty("includeBase64")]
        public bool IncludeBase64 { get; set; } = true;

        [JsonProperty("random")]
        public bool Random { get; set; } = true;
    }

    /// <summary>
    /// 搜索响应
    /// </summary>
    public class SearchResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("results")]
        public List<SearchResult> Results { get; set; } = new();

        [JsonProperty("error")]
        public string? Error { get; set; }
    }

    /// <summary>
    /// 搜索结果项
    /// </summary>
    public class SearchResult
    {
        [JsonProperty("filename")]
        public string Filename { get; set; } = string.Empty;

        [JsonProperty("filepath")]
        public string? Filepath { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonProperty("score")]
        public double Score { get; set; }

        [JsonProperty("created_at")]
        public string? CreatedAt { get; set; }

        [JsonProperty("base64")]
        public string? Base64 { get; set; }
    }

    /// <summary>
    /// 标签响应
    /// </summary>
    public class TagsResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonProperty("error")]
        public string? Error { get; set; }
    }

    /// <summary>
    /// 健康检查响应
    /// </summary>
    public class HealthResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("error")]
        public string? Error { get; set; }
    }

    /// <summary>
    /// 统计响应
    /// </summary>
    public class StatsResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("totalImages")]
        public int TotalImages { get; set; }

        [JsonProperty("indexedImages")]
        public int IndexedImages { get; set; }

        [JsonProperty("error")]
        public string? Error { get; set; }
    }
}

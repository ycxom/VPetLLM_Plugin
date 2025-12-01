using Newtonsoft.Json;

namespace PixivPlugin.Models
{
    /// <summary>
    /// Pixiv API 搜索/排行榜响应
    /// </summary>
    public class PixivResponse
    {
        [JsonProperty("illusts")]
        public List<PixivIllust> Illusts { get; set; } = new();
    }

    /// <summary>
    /// Pixiv 插画数据
    /// </summary>
    public class PixivIllust
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("caption")]
        public string Caption { get; set; } = string.Empty;

        [JsonProperty("user")]
        public PixivUser User { get; set; } = new();

        [JsonProperty("image_urls")]
        public PixivImageUrls ImageUrls { get; set; } = new();

        [JsonProperty("meta_single_page")]
        public PixivMetaSinglePage MetaSinglePage { get; set; } = new();

        [JsonProperty("meta_pages")]
        public List<PixivMetaPage> MetaPages { get; set; } = new();

        [JsonProperty("page_count")]
        public int PageCount { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }

        [JsonProperty("tags")]
        public List<PixivTag> Tags { get; set; } = new();

        [JsonProperty("total_bookmarks")]
        public int TotalBookmarks { get; set; }

        [JsonProperty("total_view")]
        public int TotalView { get; set; }

        [JsonProperty("create_date")]
        public DateTime CreateDate { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// 获取指定页的缩略图 URL
        /// </summary>
        public string GetThumbnailUrl(int pageIndex = 0)
        {
            if (PageCount == 1 || MetaPages.Count == 0)
            {
                return ImageUrls.Large;
            }
            if (pageIndex >= 0 && pageIndex < MetaPages.Count)
            {
                return MetaPages[pageIndex].ImageUrls.Large;
            }
            return ImageUrls.Large;
        }

        /// <summary>
        /// 获取指定页的原图 URL
        /// </summary>
        public string? GetOriginalUrl(int pageIndex = 0)
        {
            if (PageCount == 1 || MetaPages.Count == 0)
            {
                return MetaSinglePage.OriginalImageUrl;
            }
            if (pageIndex >= 0 && pageIndex < MetaPages.Count)
            {
                return MetaPages[pageIndex].ImageUrls.Original;
            }
            return null;
        }
    }

    /// <summary>
    /// Pixiv 用户信息
    /// </summary>
    public class PixivUser
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("account")]
        public string Account { get; set; } = string.Empty;
    }

    /// <summary>
    /// 图片 URL 集合
    /// </summary>
    public class PixivImageUrls
    {
        [JsonProperty("large")]
        public string Large { get; set; } = string.Empty;

        [JsonProperty("medium")]
        public string Medium { get; set; } = string.Empty;

        [JsonProperty("square_medium")]
        public string SquareMedium { get; set; } = string.Empty;
    }

    /// <summary>
    /// 单页图片元数据
    /// </summary>
    public class PixivMetaSinglePage
    {
        [JsonProperty("original_image_url")]
        public string? OriginalImageUrl { get; set; }
    }

    /// <summary>
    /// 多页图片元数据
    /// </summary>
    public class PixivMetaPage
    {
        [JsonProperty("image_urls")]
        public PixivMetaPageUrls ImageUrls { get; set; } = new();
    }

    /// <summary>
    /// 多页图片 URL 集合
    /// </summary>
    public class PixivMetaPageUrls
    {
        [JsonProperty("large")]
        public string Large { get; set; } = string.Empty;

        [JsonProperty("medium")]
        public string Medium { get; set; } = string.Empty;

        [JsonProperty("original")]
        public string Original { get; set; } = string.Empty;

        [JsonProperty("square_medium")]
        public string SquareMedium { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pixiv 标签
    /// </summary>
    public class PixivTag
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("translated_name")]
        public string? TranslatedName { get; set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(TranslatedName) ? Name : $"{Name}({TranslatedName})";
        }
    }
}

using System.Collections.Generic;
using Newtonsoft.Json;

namespace CyanTools.Model
{
    public sealed class PixivTokenResponse
    {
        [JsonProperty("access_token")]
        public string? AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }

    public sealed class PixivUserIllustsResponse
    {
        [JsonProperty("illusts")]
        public List<PixivIllust> Illusts { get; set; } = new();

        [JsonProperty("next_url")]
        public string? NextUrl { get; set; }
    }

    public sealed class PixivIllust
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("image_urls")]
        public PixivImageUrls? ImageUrls { get; set; }

        [JsonProperty("meta_single_page")]
        public PixivMetaSinglePage? MetaSinglePage { get; set; }

        [JsonProperty("meta_pages")]
        public List<PixivMetaPage>? MetaPages { get; set; }
    }

    public sealed class PixivMetaSinglePage
    {
        [JsonProperty("original_image_url")]
        public string? OriginalImageUrl { get; set; }
    }

    public sealed class PixivMetaPage
    {
        [JsonProperty("image_urls")]
        public PixivImageUrls? ImageUrls { get; set; }
    }

    public sealed class PixivImageUrls
    {
        [JsonProperty("original")]
        public string? Original { get; set; }

        [JsonProperty("large")]
        public string? Large { get; set; }

        [JsonProperty("medium")]
        public string? Medium { get; set; }
    }
}

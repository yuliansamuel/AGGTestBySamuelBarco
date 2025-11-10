namespace Agg.Test.DTOs
{
    using System.Text.Json.Serialization;
    public class Pagination
    {
        [JsonPropertyName("limit")]
        public int? Limit { get; set; }

        [JsonPropertyName("offset")]
        public int? Offset { get; set; }

        [JsonPropertyName("count")]
        public int? Count { get; set; }

        [JsonPropertyName("total")]
        public int? Total { get; set; }
    }
}

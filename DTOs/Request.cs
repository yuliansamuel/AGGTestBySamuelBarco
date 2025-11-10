namespace Agg.Test.DTOs
{
    using System.Text.Json.Serialization;
    public class Request
    {
        [JsonPropertyName("pagination")]
        public Pagination Pagination { get; set; }

        [JsonPropertyName("data")]
        public List<Datum> Data { get; set; }

        [JsonPropertyName("date_time")]
        public DateTime? Datetime { get; set; }
    }
}

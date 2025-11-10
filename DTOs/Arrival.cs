namespace Agg.Test.DTOs
{
    using System.Text.Json.Serialization;
    public class Arrival
    {
        [JsonPropertyName("airport")]
        public string Airport { get; set; }

        [JsonPropertyName("timezone")]
        public string Timezone { get; set; }

        [JsonPropertyName("iata")]
        public string Iata { get; set; }

        [JsonPropertyName("icao")]
        public string Icao { get; set; }

        [JsonPropertyName("terminal")]
        public string Terminal { get; set; }

        [JsonPropertyName("gate")]
        public string Gate { get; set; }

        [JsonPropertyName("baggage")]
        public string Baggage { get; set; }

        [JsonPropertyName("scheduled")]
        public DateTime? Scheduled { get; set; }

        [JsonPropertyName("delay")]
        public object Delay { get; set; }

        [JsonPropertyName("estimated")]
        public DateTime? Estimated { get; set; }

        [JsonPropertyName("actual")]
        public object Actual { get; set; }

        [JsonPropertyName("estimated_runway")]
        public object EstimatedRunway { get; set; }

        [JsonPropertyName("actual_runway")]
        public object ActualRunway { get; set; }
    }
}

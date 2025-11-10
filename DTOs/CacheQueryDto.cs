namespace Agg.Test.DTOs
{
    using System.Text.Json.Serialization;
    public class CacheQueryDto
    {
        [JsonPropertyName("airline_iata")]
        public string? AirlineIata { get; set; }

        [JsonPropertyName("airport_iata")]
        public string? AirportIata { get; set; }
    }
}

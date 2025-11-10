namespace Agg.Test.DTOs
{
    using System.Text.Json.Serialization;
    public class Flight
    {
        [JsonPropertyName("number")]
        public string Number { get; set; }

        [JsonPropertyName("iata")]
        public string Iata { get; set; }

        [JsonPropertyName("icao")]
        public string Icao { get; set; }

        [JsonPropertyName("codeshared")]
        public Codeshared Codeshared { get; set; }
    }
}

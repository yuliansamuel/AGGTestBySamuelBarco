namespace Agg.Test.DTOs
{
    using System.Text.Json.Serialization;
    public class Datum
    {
        [JsonPropertyName("flight_date")]
        public string FlightDate { get; set; }

        [JsonPropertyName("flight_status")]
        public string FlightStatus { get; set; }

        [JsonPropertyName("departure")]
        public Departure Departure { get; set; }

        [JsonPropertyName("arrival")]
        public Arrival Arrival { get; set; }

        [JsonPropertyName("airline")]
        public Airline Airline { get; set; }

        [JsonPropertyName("flight")]
        public Flight Flight { get; set; }

        [JsonPropertyName("aircraft")]
        public object Aircraft { get; set; }

        [JsonPropertyName("live")]
        public object Live { get; set; }
    }
}

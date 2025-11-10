namespace Agg.Test
{
    using Agg.Test.DTOs;
    using Agg.Test.Infrastructure;
    using Microsoft.AspNetCore.WebUtilities;
    using System.Net.Http;
    using System.Text.Json;

    /// <summary>
    /// Contract for services that retrieve flight data and persist it.
    /// </summary>
    public interface IFlights
    {
        /// <summary>
        /// Downloads the latest flights payload from the external provider,
        /// persists it to the database, and returns the parsed DTO.
        /// </summary>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>
        /// A <see cref="Request"/> DTO representing the external response.
        /// Returns an empty <see cref="Request"/> when the operation fails.
        /// </returns>
        Task<Request> GetFlightsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Default implementation of <see cref="IFlights"/> that uses an
    /// <see cref="IHttpClientFactory"/> to call the external flights API and an
    /// <see cref="IDbExecutor"/> to persist the JSON via a stored procedure.
    /// </summary>
    public class Flights : IFlights
    {
        private readonly IDbExecutor _db;
        private readonly IHttpClientFactory _http;
        /// <summary>
        /// Base endpoint for the external flights provider.
        /// </summary>
       
        
        
        /// <summary>
        /// Access key used to authenticate requests to the external provider.
        /// Consider moving this to configuration/secrets instead of hard-coding.
        /// </summary>
        private const string Endpoint = "http://api.aviationstack.com/v1/flights";

        private const string AccessKey = "53beccef66e74af16d3a3fdeb0f929c2";

        /// <summary>
        /// Initializes a new instance of the <see cref="Flights"/> service.
        /// </summary>
        /// <param name="http">The HTTP client factory used to create named clients.</param>
        /// <param name="db">Database executor used to invoke stored procedures.</param>
        public Flights(IHttpClientFactory http, IDbExecutor db)
        {
            _http = http;
            _db = db;
        }


        /// <summary>
        /// Calls the external flights API, validates the HTTP response, deserializes
        /// the JSON into a <see cref="Request"/> DTO, and persists the raw JSON
        /// to the database via <c>sp_insert_flights_from_json</c>.
        /// </summary>
        /// <remarks>
        /// - Uses the named HttpClient <c>"aviation"</c> (configured in Program.cs).
        /// - Adds <c>access_key</c> as a query string parameter.
        /// - Throws for non-success HTTP status codes via <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>.
        /// - Returns an empty DTO if an exception is thrown (error is logged to console).
        /// </remarks>
        /// <param name="ct">Cancellation token to cancel the operation.</param>
        /// <returns>The parsed <see cref="Request"/> DTO for the fetched payload.</returns>
        public async Task<Request> GetFlightsAsync(CancellationToken ct = default)
        {
            try
            {
                var client = _http.CreateClient("aviation");

                var ub = new UriBuilder(Endpoint);
                var query = QueryHelpers.AddQueryString(ub.Uri.ToString(), "access_key", AccessKey);
                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var payload = await response.Content.ReadAsStringAsync(ct);

                response.EnsureSuccessStatusCode();

                var dto = JsonSerializer.Deserialize<Request>(payload)!;
                var json = JsonSerializer.Serialize(dto);

                var rows = await _db.ExecJsonSpWithOutParamAsync("sp_insert_flights_from_json", json, "@p_json","@p_result",ct);

                return dto;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new Request();
            }
    }
    }
}
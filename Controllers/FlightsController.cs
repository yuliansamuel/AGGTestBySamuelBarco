namespace Agg.Test.Controllers
{
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Provides a manual endpoint to trigger the flight data ingestion process.
    /// </summary>
    /// <remarks>
    /// This controller is primarily intended for testing and manual validation purposes.
    /// It directly invokes the <see cref="Flights.GetFlightsAsync"/> method to fetch data 
    /// from the AviationStack API, insert it into the database, and return the resulting payload.
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    public class FlightsController : ControllerBase
    {
        private readonly Flights _flights;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlightsController"/> class.
        /// </summary>
        /// <param name="flights">The flights ingestion service used to fetch live data.</param>
        public FlightsController(Flights flights)
        {
            _flights = flights;
        }

        /// <summary>
        /// Manually triggers the flight data download and ingestion process.
        /// </summary>
        /// <remarks>
        /// Calls the AviationStack API to retrieve flight information and stores the response 
        /// into the database using a stored procedure (<c>sp_insert_flights_from_json</c>).
        /// This endpoint is meant for development or debugging — the background job 
        /// (<see cref="Agg.Test.Jobs.FlightsJob"/>) performs this automatically at runtime.
        /// </remarks>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        /// <returns>
        /// A JSON response containing the retrieved flight dataset as returned by the API.
        /// </returns>
        [HttpGet("GetFlights")]
        public async Task<IActionResult> RunManual(CancellationToken ct)
        {
            var result = await _flights.GetFlightsAsync(ct);
            return Ok(result);
        }
    }
}

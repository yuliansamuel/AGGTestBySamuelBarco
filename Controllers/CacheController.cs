namespace Agg.Test.Controllers
{
    using Agg.Test.DTOs;
    using Agg.Test.Services;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Provides API endpoints for interacting with the caching layer.
    /// </summary>
    /// <remarks>
    /// This controller allows authorized clients to:
    /// <list type="bullet">
    /// <item><description>Retrieve and store JSON data in Redis.</description></item>
    /// <item><description>Search cached flight datasets by airline or airport filters.</description></item>
    /// <item><description>List cached keys based on a pattern.</description></item>
    /// </list>
    /// All endpoints require a valid API key via the <c>X-API-KEY</c> header
    /// and are protected by the <c>CacheAccess</c> authorization policy.
    /// </remarks>

    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "CacheAccess")]

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheController"/> class.
    /// </summary>
    /// <param name="cache">The caching service abstraction used to interact with Redis.</param>
    public class CacheController : ControllerBase
    {
        private readonly ICacheService _cache;

        public CacheController(ICacheService cache)
        {
            _cache = cache;
        }

        /// <summary>
        /// Retrieves the JSON value associated with a given Redis key.
        /// </summary>
        /// <param name="key">The Redis key to retrieve.</param>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        /// <returns>
        /// <see cref="IActionResult"/> containing the JSON content if found,
        /// or <c>404 Not Found</c> if the key does not exist.
        /// </returns>
        [HttpGet("get/{key}")]
        public async Task<IActionResult> GetKey(string key, CancellationToken ct)
        {
            var json = await _cache.GetJsonAsync(key, ct);
            return json is null
                ? NotFound(new { message = $"Key '{key}' not found" })
                : Content(json, "application/json");
        }

        /// <summary>
        /// Stores an arbitrary JSON object in Redis under a given key.
        /// </summary>
        /// <param name="key">The Redis key under which to store the data.</param>
        /// <param name="value">The JSON object to be stored.</param>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        /// <returns>
        /// <see cref="IActionResult"/> indicating success and returning the stored key.
        /// </returns>
        [HttpPost("setjson/{key}")]
        public async Task<IActionResult> SetJson(string key, [FromBody] object value, CancellationToken ct)
        {
            await _cache.SetJsonAsync(key, value, ct);
            return Ok(new { saved = true, key });
        }

        /// <summary>
        /// Searches the cached dataset for records that match one or more filters.
        /// </summary>
        /// <remarks>
        /// The request body should include a JSON object containing optional filters:
        /// <example>
        /// {
        ///   "airline_iata": "6E",
        ///   "airport_iata": "HBX"
        /// }
        /// </example>
        /// Matching is performed using logical <c>OR</c> (any matching field).
        /// </remarks>
        /// <param name="q">The query filter object containing IATA codes for airline or airport.</param>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        /// <returns>
        /// A JSON response with all matching entries,
        /// or <c>404 Not Found</c> if no matches or dataset is missing.
        /// </returns>
        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] CacheQueryDto q, CancellationToken ct)
        {
            var json = await _cache.SearchAsync(q, ct);
            return json is null
                ? NotFound(new { message = "No matches or missing dataset" })
                : Content(json, "application/json");
        }

        /// <summary>
        /// Lists all Redis keys that match a given pattern.
        /// </summary>
        /// <remarks>
        /// By default, the pattern is <c>flights:*</c>.
        /// </remarks>
        /// <param name="pattern">The Redis key pattern to match (supports wildcards).</param>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        /// <returns>
        /// A list of keys matching the specified pattern.
        /// </returns>
        [HttpGet("keys")]
        public async Task<IActionResult> Keys([FromQuery] string pattern = "flights:*", CancellationToken ct = default)
        {
            var keys = await _cache.ListKeysAsync(pattern, ct);
            return Ok(keys);
        }
    }
}
namespace Agg.Test.Services
{
    using Agg.Test.DTOs;
    using Agg.Test.Infrastructure;
    using StackExchange.Redis;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    public sealed class CacheService : ICacheService
    {
        private readonly IRedisDbProvider _redis;
        private readonly IConnectionMultiplexer _mux;
        private readonly IConfiguration _cfg;

        public CacheService(IRedisDbProvider redis, IConnectionMultiplexer mux, IConfiguration cfg)
        {
            _redis = redis;
            _mux = mux;
            _cfg = cfg;
        }

        /// <summary>
        /// Retrieves a JSON document from Redis by key.
        /// Returns null if the key does not exist or contains no value.
        /// </summary>
        public async Task<string?> GetJsonAsync(string key, CancellationToken ct = default)
        {
            var db = _redis.Db;
            var res = await db.ExecuteAsync("JSON.GET", key, "$");
            return res.IsNull ? null : (string)res!;
        }

        /// <summary>
        /// Serializes the provided object and stores it in Redis as a JSON document.
        /// If the key already exists, it will be overwritten.
        /// </summary>
        public async Task SetJsonAsync(string key, object value, CancellationToken ct = default)
        {
            var db = _redis.Db;
            var json = System.Text.Json.JsonSerializer.Serialize(value);
            await db.ExecuteAsync("JSON.SET", key, "$", json);
        }

        /// <summary>
        /// Lists all Redis keys that match the specified pattern.
        /// Useful for debugging and verifying cache state.
        /// </summary>
        public async Task<string[]> ListKeysAsync(string pattern, CancellationToken ct = default)
        {
            var srv = _mux.GetServer(_mux.GetEndPoints().First());
            return srv.Keys(pattern: pattern).Select(k => (string)k).ToArray();
        }

        /// <summary>
        /// Searches the cached flight dataset in Redis using airline and/or airport IATA filters.
        /// Results are cached for subsequent identical queries to improve performance.
        /// If both filters are provided, results are combined using an OR condition.
        /// </summary>
        public async Task<string?> SearchAsync(CacheQueryDto q, CancellationToken ct = default)
        {
            var db = _redis.Db;
            var pref = _cfg.GetValue("Redis:KeyPrefix", "flights");
            var ttlQ = TimeSpan.FromMinutes(_cfg.GetValue("Redis:QueryTtlMinutes", 30));

            var (cacheKey, _) = await BuildQueryCacheKeyAsync(db, pref, q, ct);

            var cached = await db.ExecuteAsync("JSON.GET", cacheKey, "$");
            if (!cached.IsNull) return NormalizeArrayJson((string)cached!);

            var keyLast = $"{pref}:last";
            var path = BuildJsonPath(q);

            var res = await db.ExecuteAsync("JSON.GET", keyLast, path);
            string? filteredJson;

            if (!res.IsNull)
            {
                filteredJson = NormalizeArrayJson((string)res!);
            }
            else
            {
                var fullRes = await db.ExecuteAsync("JSON.GET", keyLast, "$");
                if (fullRes.IsNull) return null;

                filteredJson = FilterInMemory((string)fullRes!, q);
            }

            await db.ExecuteAsync("JSON.SET", cacheKey, "$", filteredJson);
            await db.KeyExpireAsync(cacheKey, ttlQ);

            return filteredJson;
        }

        // ---------- Helpers ----------

        private static string BuildJsonPath(CacheQueryDto q)
        {
            var airline = q.AirlineIata?.Trim().ToUpperInvariant();
            var airport = q.AirportIata?.Trim().ToUpperInvariant();

            var filters = new List<string>();

            if (!string.IsNullOrWhiteSpace(airline))
                filters.Add($"@.airline.iata==\"{airline}\"");

            if (!string.IsNullOrWhiteSpace(airport))
                filters.Add($"(@.departure.iata==\"{airport}\" || @.arrival.iata==\"{airport}\")");

            return filters.Count == 0 ? "$.data[*]" : $"$.data[?({string.Join("&&", filters)})]";
        }

        /// <summary>
        /// Builds a consistent Redis cache key for filtered queries based on parameters and dataset version.
        /// This helps prevent redundant filtering for the same inputs.
        /// </summary>
        private async Task<(string cacheKey, string version)> BuildQueryCacheKeyAsync(
            IDatabase db, string prefix, CacheQueryDto q, CancellationToken ct)
        {
            var version = (await db.StringGetAsync($"{prefix}:version")).ToString();
            if (string.IsNullOrWhiteSpace(version)) version = "noversion";

            var airline = q.AirlineIata?.Trim().ToUpperInvariant();
            var airport = q.AirportIata?.Trim().ToUpperInvariant();

            var canon = $"airline={airline ?? ""};airport={airport ?? ""}";
            var hash = Hash16(canon);

            var airlinePart = string.IsNullOrWhiteSpace(airline) ? "_" : airline;
            var airportPart = string.IsNullOrWhiteSpace(airport) ? "_" : airport;

            var key = $"{prefix}:q:{airlinePart}:{airportPart}:{version}:{hash}";
            return (key, version);
        }

        /// <summary>
        /// Generates a short 16-character hash (from a SHA256 digest) to identify a cached query.
        /// </summary>
        private static string Hash16(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
        }

        /// <summary>
        /// Normalizes RedisJSON responses, converting nested arrays (e.g. [[...]] structures)
        /// into a flat JSON array for easier deserialization and consistent output.
        /// </summary>
        private static string NormalizeArrayJson(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1)
                {
                    var inner = root[0];
                    if (inner.ValueKind == JsonValueKind.Array)
                        return inner.GetRawText();
                }

                if (root.ValueKind == JsonValueKind.Array)
                    return root.GetRawText();

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                        return dataProp.GetRawText();
                }
            }
            catch
            {
            }
            return raw;
        }

        /// <summary>
        /// Fallback: filtra en memoria desde el documento completo (root con pagination+data)
        /// y retorna SIEMPRE un array (JSON) con los elementos coincidentes de data[].
        /// </summary>
        private static string FilterInMemory(string fullJson, CacheQueryDto q)
        {
            var airline = q.AirlineIata?.Trim().ToUpperInvariant();
            var airport = q.AirportIata?.Trim().ToUpperInvariant();

            try
            {
                var node = JsonNode.Parse(fullJson);
                var data = node?["data"] as JsonArray;
                if (data is null) return "[]";

                IEnumerable<JsonNode?> items = data;

                if (!string.IsNullOrWhiteSpace(airline))
                {
                    items = items.Where(n =>
                        (n?["airline"]?["iata"]?.GetValue<string>()?.ToUpperInvariant() ?? "") == airline
                    );
                }

                if (!string.IsNullOrWhiteSpace(airport))
                {
                    items = items.Where(n =>
                    {
                        var dep = n?["departure"]?["iata"]?.GetValue<string>()?.ToUpperInvariant();
                        var arr = n?["arrival"]?["iata"]?.GetValue<string>()?.ToUpperInvariant();
                        return dep == airport || arr == airport;
                    });
                }

                var filtered = new JsonArray(items.Where(x => x is not null).ToArray());
                return filtered.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                return "[]";
            }
        }
    }
}
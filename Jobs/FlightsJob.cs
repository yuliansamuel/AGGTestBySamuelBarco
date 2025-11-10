namespace Agg.Test.Jobs
{
    using Agg.Test;
    using Agg.Test.Infrastructure;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using StackExchange.Redis;
    using System.Text.Json;

    /// <summary>
    /// Background service responsible for periodically downloading flight data,
    /// storing it in Redis, and updating versioned snapshots for downstream consumers.
    /// </summary>
    /// <remarks>
    /// This job:
    /// <item><description>Fetches data from the configured flight API using <see cref="IFlights"/>.</description></item>
    /// <item><description>Saves the latest snapshot in Redis under <c>flights:last</c>.</description></item>
    /// <item><description>Creates timestamped snapshots (<c>flights:snapshot:YYYYMMDDHHMMSS</c>) and indexes.</description></item>
    /// <item><description>Updates the global dataset version (<c>flights:version</c>).</description></item>
    /// </list>
    /// The job runs continuously at the configured interval from <c>appsettings.json</c> (<c>Ingestion:IntervalMinutes</c>).
    /// </remarks>
    public class FlightsJob : BackgroundService
    {
        private readonly IFlights _flights;
        private readonly ILogger<FlightsJob> _log;
        private readonly IConnectionMultiplexer _mux;
        private readonly IRedisDbProvider _redis;
        private readonly IConfiguration _cfg;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="FlightsJob"/> background service.
        /// </summary>
        /// <param name="flights">Flight ingestion service used to retrieve data.</param>
        /// <param name="log">Logger for structured diagnostics.</param>
        /// <param name="mux">Redis connection multiplexer.</param>
        /// <param name="cfg">Application configuration provider.</param>
        /// <param name="redis">Redis database provider abstraction.</param>
        public FlightsJob(IFlights flights, ILogger<FlightsJob> log,
                          IConnectionMultiplexer mux, IConfiguration cfg,
                          IRedisDbProvider redis)
        {
            _flights = flights;
            _log = log;
            _mux = mux;
            _cfg = cfg;
            _redis = redis;
        }

        /// <summary>
        /// Entry point for the background worker.
        /// Executes <see cref="RunOnceAsync"/> immediately at startup, 
        /// then continues running at the configured ingestion interval.
        /// </summary>
        /// <param name="stoppingToken">Cancellation token used to gracefully stop the background task.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RunOnceAsync(stoppingToken);

            var interval = TimeSpan.FromMinutes(_cfg.GetValue("Ingestion:IntervalMinutes", 50));
            using var timer = new PeriodicTimer(interval);

            while (!stoppingToken.IsCancellationRequested &&
                   await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }

        /// <summary>
        /// Executes a single ingestion cycle:
        /// downloads flight data, serializes it, and persists it to Redis
        /// with time-based snapshot and version management.
        /// </summary>
        /// <remarks>
        /// This method:
        /// <list type="bullet">
        /// <item><description>Calls <see cref="IFlights.GetFlightsAsync"/> to fetch data.</description></item>
        /// <item><description>Stores JSON in Redis using <c>JSON.SET</c> at multiple keys:
        /// <c>flights:last</c>, <c>flights:snapshot:timestamp</c>, and daily <c>flights:index:YYYYMMDD</c>.</description></item>
        /// <item><description>Sets TTL for each key to ensure old snapshots expire automatically.</description></item>
        /// <item><description>Updates <c>flights:version</c> with a new timestamp string.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        private async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                _log.LogInformation("Ingestion tick at {ts}", DateTimeOffset.UtcNow);

                
                var dto = await _flights.GetFlightsAsync(ct);

                #region dto for QA
                /*var dto = JsonSerializer.Deserialize<Request>(@"{
    ""pagination"": {
        ""limit"": 100,
        ""offset"": 0,
        ""count"": 100,
        ""total"": 74748
    },
    ""data"": [
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Hubli"",
                ""timezone"": ""Asia/Kolkata"",
                ""iata"": ""HBX"",
                ""icao"": ""VOHB"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T13:25:00+00:00"",
                ""estimated"": ""2025-11-08T13:25:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Hyderabad Airport"",
                ""timezone"": ""Asia/Kolkata"",
                ""iata"": ""HYD"",
                ""icao"": ""VOHS"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": ""5"",
                ""scheduled"": ""2025-11-08T14:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""IndiGo"",
                ""iata"": ""6E"",
                ""icao"": ""IGO""
            },
            ""flight"": {
                ""number"": ""7416"",
                ""iata"": ""6E7416"",
                ""icao"": ""IGO7416"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Hubli"",
                ""timezone"": ""Asia/Kolkata"",
                ""iata"": ""HBX"",
                ""icao"": ""VOHB"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:20:00+00:00"",
                ""estimated"": ""2025-11-08T09:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Bangalore International Airport"",
                ""timezone"": ""Asia/Kolkata"",
                ""iata"": ""BLR"",
                ""icao"": ""VOBL"",
                ""terminal"": ""1"",
                ""gate"": ""A3"",
                ""baggage"": ""3"",
                ""scheduled"": ""2025-11-08T10:40:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""IndiGo"",
                ""iata"": ""6E"",
                ""icao"": ""IGO""
            },
            ""flight"": {
                ""number"": ""7233"",
                ""iata"": ""6E7233"",
                ""icao"": ""IGO7233"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Hubli"",
                ""timezone"": ""Asia/Kolkata"",
                ""iata"": ""HBX"",
                ""icao"": ""VOHB"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 15,
                ""scheduled"": ""2025-11-08T08:00:00+00:00"",
                ""estimated"": ""2025-11-08T08:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Chhatrapati Shivaji International (Sahar International)"",
                ""timezone"": ""Asia/Kolkata"",
                ""iata"": ""BOM"",
                ""icao"": ""VABB"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:05:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""IndiGo"",
                ""iata"": ""6E"",
                ""icao"": ""IGO""
            },
            ""flight"": {
                ""number"": ""5145"",
                ""iata"": ""6E5145"",
                ""icao"": ""IGO5145"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""unknown"",
            ""departure"": {
                ""airport"": ""Misima Island"",
                ""timezone"": ""Pacific/Port_Moresby"",
                ""iata"": ""MIS"",
                ""icao"": ""AYMS"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:15:00+00:00"",
                ""estimated"": ""2025-11-08T09:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Gurney"",
                ""timezone"": ""Pacific/Port_Moresby"",
                ""iata"": ""GUR"",
                ""icao"": ""AYGN"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""PNG Air"",
                ""iata"": ""CG"",
                ""icao"": ""TOK""
            },
            ""flight"": {
                ""number"": ""1643"",
                ""iata"": ""CG1643"",
                ""icao"": ""TOK1643"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Beijing Daxing International Airport"",
                ""timezone"": ""+8"",
                ""iata"": ""PKX"",
                ""icao"": ""ZBAD"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T12:20:00+00:00"",
                ""estimated"": ""2025-11-08T12:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T15:55:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Air China LTD"",
                ""iata"": ""CA"",
                ""icao"": ""CCA""
            },
            ""flight"": {
                ""number"": ""8693"",
                ""iata"": ""CA8693"",
                ""icao"": ""CCA8693"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Beijing Daxing International Airport"",
                ""timezone"": ""+8"",
                ""iata"": ""PKX"",
                ""icao"": ""ZBAD"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T12:20:00+00:00"",
                ""estimated"": ""2025-11-08T12:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T15:55:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Shenzhen Airlines"",
                ""iata"": ""ZH"",
                ""icao"": ""CSZ""
            },
            ""flight"": {
                ""number"": ""4393"",
                ""iata"": ""ZH4393"",
                ""icao"": ""CSZ4393"",
                ""codeshared"": {
                    ""airline_name"": ""air china ltd"",
                    ""airline_iata"": ""ca"",
                    ""airline_icao"": ""cca"",
                    ""flight_number"": ""8693"",
                    ""flight_iata"": ""ca8693"",
                    ""flight_icao"": ""cca8693""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Xianyang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""XIY"",
                ""icao"": ""ZLXY"",
                ""terminal"": ""5"",
                ""gate"": ""B03-"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T13:20:00+00:00"",
                ""estimated"": ""2025-11-08T13:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T15:50:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""China Eastern Airlines"",
                ""iata"": ""MU"",
                ""icao"": ""CES""
            },
            ""flight"": {
                ""number"": ""2215"",
                ""iata"": ""MU2215"",
                ""icao"": ""CES2215"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Xianyang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""XIY"",
                ""icao"": ""ZLXY"",
                ""terminal"": ""5"",
                ""gate"": ""B03-"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T13:20:00+00:00"",
                ""estimated"": ""2025-11-08T13:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T15:50:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Xiamen Airlines"",
                ""iata"": ""MF"",
                ""icao"": ""CXA""
            },
            ""flight"": {
                ""number"": ""3071"",
                ""iata"": ""MF3071"",
                ""icao"": ""CXA3071"",
                ""codeshared"": {
                    ""airline_name"": ""china eastern airlines"",
                    ""airline_iata"": ""mu"",
                    ""airline_icao"": ""ces"",
                    ""flight_number"": ""2215"",
                    ""flight_iata"": ""mu2215"",
                    ""flight_icao"": ""ces2215""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Xianyang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""XIY"",
                ""icao"": ""ZLXY"",
                ""terminal"": ""5"",
                ""gate"": ""B03-"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T13:20:00+00:00"",
                ""estimated"": ""2025-11-08T13:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T15:50:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""China Express Air"",
                ""iata"": ""G5"",
                ""icao"": ""HXA""
            },
            ""flight"": {
                ""number"": ""8549"",
                ""iata"": ""G58549"",
                ""icao"": ""HXA8549"",
                ""codeshared"": {
                    ""airline_name"": ""china eastern airlines"",
                    ""airline_iata"": ""mu"",
                    ""airline_icao"": ""ces"",
                    ""flight_number"": ""2215"",
                    ""flight_iata"": ""mu2215"",
                    ""flight_icao"": ""ces2215""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Lanzhou Zhongchuan Airport"",
                ""timezone"": null,
                ""iata"": ""LHW"",
                ""icao"": ""ZLLL"",
                ""terminal"": null,
                ""gate"": ""C55"",
                ""delay"": 95,
                ""scheduled"": ""2025-11-08T10:45:00+00:00"",
                ""estimated"": ""2025-11-08T10:45:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T12:20:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""China Eastern Airlines"",
                ""iata"": ""MU"",
                ""icao"": ""CES""
            },
            ""flight"": {
                ""number"": ""6833"",
                ""iata"": ""MU6833"",
                ""icao"": ""CES6833"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Lanzhou Zhongchuan Airport"",
                ""timezone"": null,
                ""iata"": ""LHW"",
                ""icao"": ""ZLLL"",
                ""terminal"": null,
                ""gate"": ""C55"",
                ""delay"": 95,
                ""scheduled"": ""2025-11-08T10:45:00+00:00"",
                ""estimated"": ""2025-11-08T10:45:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T12:20:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Xiamen Airlines"",
                ""iata"": ""MF"",
                ""icao"": ""CXA""
            },
            ""flight"": {
                ""number"": ""2265"",
                ""iata"": ""MF2265"",
                ""icao"": ""CXA2265"",
                ""codeshared"": {
                    ""airline_name"": ""china eastern airlines"",
                    ""airline_iata"": ""mu"",
                    ""airline_icao"": ""ces"",
                    ""flight_number"": ""6833"",
                    ""flight_iata"": ""mu6833"",
                    ""flight_icao"": ""ces6833""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Lanzhou Zhongchuan Airport"",
                ""timezone"": null,
                ""iata"": ""LHW"",
                ""icao"": ""ZLLL"",
                ""terminal"": null,
                ""gate"": ""C55"",
                ""delay"": 95,
                ""scheduled"": ""2025-11-08T10:45:00+00:00"",
                ""estimated"": ""2025-11-08T10:45:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T12:20:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""China Express Air"",
                ""iata"": ""G5"",
                ""icao"": ""HXA""
            },
            ""flight"": {
                ""number"": ""6820"",
                ""iata"": ""G56820"",
                ""icao"": ""HXA6820"",
                ""codeshared"": {
                    ""airline_name"": ""china eastern airlines"",
                    ""airline_iata"": ""mu"",
                    ""airline_icao"": ""ces"",
                    ""flight_number"": ""6833"",
                    ""flight_iata"": ""mu6833"",
                    ""flight_icao"": ""ces6833""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Shanghai Pudong International"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""PVG"",
                ""icao"": ""ZSPD"",
                ""terminal"": ""1"",
                ""gate"": ""11"",
                ""delay"": 145,
                ""scheduled"": ""2025-11-08T07:30:00+00:00"",
                ""estimated"": ""2025-11-08T07:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T12:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""China Eastern Airlines"",
                ""iata"": ""MU"",
                ""icao"": ""CES""
            },
            ""flight"": {
                ""number"": ""6759"",
                ""iata"": ""MU6759"",
                ""icao"": ""CES6759"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Shanghai Pudong International"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""PVG"",
                ""icao"": ""ZSPD"",
                ""terminal"": ""1"",
                ""gate"": ""11"",
                ""delay"": 145,
                ""scheduled"": ""2025-11-08T07:30:00+00:00"",
                ""estimated"": ""2025-11-08T07:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T12:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Xiamen Airlines"",
                ""iata"": ""MF"",
                ""icao"": ""CXA""
            },
            ""flight"": {
                ""number"": ""2247"",
                ""iata"": ""MF2247"",
                ""icao"": ""CXA2247"",
                ""codeshared"": {
                    ""airline_name"": ""china eastern airlines"",
                    ""airline_iata"": ""mu"",
                    ""airline_icao"": ""ces"",
                    ""flight_number"": ""6759"",
                    ""flight_iata"": ""mu6759"",
                    ""flight_icao"": ""ces6759""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Shanghai Pudong International"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""PVG"",
                ""icao"": ""ZSPD"",
                ""terminal"": ""1"",
                ""gate"": ""11"",
                ""delay"": 145,
                ""scheduled"": ""2025-11-08T07:30:00+00:00"",
                ""estimated"": ""2025-11-08T07:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T12:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Juneyao Airlines"",
                ""iata"": ""HO"",
                ""icao"": ""DKH""
            },
            ""flight"": {
                ""number"": ""5726"",
                ""iata"": ""HO5726"",
                ""icao"": ""DKH5726"",
                ""codeshared"": {
                    ""airline_name"": ""china eastern airlines"",
                    ""airline_iata"": ""mu"",
                    ""airline_icao"": ""ces"",
                    ""flight_number"": ""6759"",
                    ""flight_iata"": ""mu6759"",
                    ""flight_icao"": ""ces6759""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Qingyang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""IQN"",
                ""icao"": ""ZLQY"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:05:00+00:00"",
                ""estimated"": ""2025-11-08T09:05:00+00:00"",
                ""actual"": ""2025-11-08T08:56:00+00:00"",
                ""estimated_runway"": ""2025-11-08T08:56:00+00:00"",
                ""actual_runway"": ""2025-11-08T08:56:00+00:00""
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:25:00+00:00"",
                ""delay"": null,
                ""estimated"": ""2025-11-08T11:03:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""China Express Air"",
                ""iata"": ""G5"",
                ""icao"": ""HXA""
            },
            ""flight"": {
                ""number"": ""4277"",
                ""iata"": ""G54277"",
                ""icao"": ""HXA4277"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Qingyang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""IQN"",
                ""icao"": ""ZLQY"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:05:00+00:00"",
                ""estimated"": ""2025-11-08T09:05:00+00:00"",
                ""actual"": ""2025-11-08T08:56:00+00:00"",
                ""estimated_runway"": ""2025-11-08T08:56:00+00:00"",
                ""actual_runway"": ""2025-11-08T08:56:00+00:00""
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:25:00+00:00"",
                ""delay"": null,
                ""estimated"": ""2025-11-08T11:03:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Xiamen Airlines"",
                ""iata"": ""MF"",
                ""icao"": ""CXA""
            },
            ""flight"": {
                ""number"": ""2603"",
                ""iata"": ""MF2603"",
                ""icao"": ""CXA2603"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""4277"",
                    ""flight_iata"": ""g54277"",
                    ""flight_icao"": ""hxa4277""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Qingyang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""IQN"",
                ""icao"": ""ZLQY"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:05:00+00:00"",
                ""estimated"": ""2025-11-08T09:05:00+00:00"",
                ""actual"": ""2025-11-08T08:56:00+00:00"",
                ""estimated_runway"": ""2025-11-08T08:56:00+00:00"",
                ""actual_runway"": ""2025-11-08T08:56:00+00:00""
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:25:00+00:00"",
                ""delay"": null,
                ""estimated"": ""2025-11-08T11:03:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Sichuan Airlines"",
                ""iata"": ""3U"",
                ""icao"": ""CSC""
            },
            ""flight"": {
                ""number"": ""4203"",
                ""iata"": ""3U4203"",
                ""icao"": ""CSC4203"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""4277"",
                    ""flight_iata"": ""g54277"",
                    ""flight_icao"": ""hxa4277""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Qingyang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""IQN"",
                ""icao"": ""ZLQY"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:05:00+00:00"",
                ""estimated"": ""2025-11-08T09:05:00+00:00"",
                ""actual"": ""2025-11-08T08:56:00+00:00"",
                ""estimated_runway"": ""2025-11-08T08:56:00+00:00"",
                ""actual_runway"": ""2025-11-08T08:56:00+00:00""
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:25:00+00:00"",
                ""delay"": null,
                ""estimated"": ""2025-11-08T11:03:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Loong Air"",
                ""iata"": ""GJ"",
                ""icao"": ""CDC""
            },
            ""flight"": {
                ""number"": ""5751"",
                ""iata"": ""GJ5751"",
                ""icao"": ""CDC5751"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""4277"",
                    ""flight_iata"": ""g54277"",
                    ""flight_icao"": ""hxa4277""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Qingyang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""IQN"",
                ""icao"": ""ZLQY"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:05:00+00:00"",
                ""estimated"": ""2025-11-08T09:05:00+00:00"",
                ""actual"": ""2025-11-08T08:56:00+00:00"",
                ""estimated_runway"": ""2025-11-08T08:56:00+00:00"",
                ""actual_runway"": ""2025-11-08T08:56:00+00:00""
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:25:00+00:00"",
                ""delay"": null,
                ""estimated"": ""2025-11-08T11:03:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Shandong Airlines"",
                ""iata"": ""SC"",
                ""icao"": ""CDG""
            },
            ""flight"": {
                ""number"": ""3639"",
                ""iata"": ""SC3639"",
                ""icao"": ""CDG3639"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""4277"",
                    ""flight_iata"": ""g54277"",
                    ""flight_icao"": ""hxa4277""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""cancelled"",
            ""departure"": {
                ""airport"": null,
                ""timezone"": null,
                ""iata"": ""JIC"",
                ""icao"": ""ZLJC"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:40:00+00:00"",
                ""estimated"": ""2025-11-08T09:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""China Express Air"",
                ""iata"": ""G5"",
                ""icao"": ""HXA""
            },
            ""flight"": {
                ""number"": ""2749"",
                ""iata"": ""G52749"",
                ""icao"": ""HXA2749"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""cancelled"",
            ""departure"": {
                ""airport"": null,
                ""timezone"": null,
                ""iata"": ""JIC"",
                ""icao"": ""ZLJC"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:40:00+00:00"",
                ""estimated"": ""2025-11-08T09:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Xiamen Airlines"",
                ""iata"": ""MF"",
                ""icao"": ""CXA""
            },
            ""flight"": {
                ""number"": ""2443"",
                ""iata"": ""MF2443"",
                ""icao"": ""CXA2443"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""2749"",
                    ""flight_iata"": ""g52749"",
                    ""flight_icao"": ""hxa2749""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""cancelled"",
            ""departure"": {
                ""airport"": null,
                ""timezone"": null,
                ""iata"": ""JIC"",
                ""icao"": ""ZLJC"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:40:00+00:00"",
                ""estimated"": ""2025-11-08T09:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Sichuan Airlines"",
                ""iata"": ""3U"",
                ""icao"": ""CSC""
            },
            ""flight"": {
                ""number"": ""4045"",
                ""iata"": ""3U4045"",
                ""icao"": ""CSC4045"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""2749"",
                    ""flight_iata"": ""g52749"",
                    ""flight_icao"": ""hxa2749""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""cancelled"",
            ""departure"": {
                ""airport"": null,
                ""timezone"": null,
                ""iata"": ""JIC"",
                ""icao"": ""ZLJC"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:40:00+00:00"",
                ""estimated"": ""2025-11-08T09:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Loong Air"",
                ""iata"": ""GJ"",
                ""icao"": ""CDC""
            },
            ""flight"": {
                ""number"": ""5591"",
                ""iata"": ""GJ5591"",
                ""icao"": ""CDC5591"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""2749"",
                    ""flight_iata"": ""g52749"",
                    ""flight_icao"": ""hxa2749""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""cancelled"",
            ""departure"": {
                ""airport"": null,
                ""timezone"": null,
                ""iata"": ""JIC"",
                ""icao"": ""ZLJC"",
                ""terminal"": null,
                ""gate"": ""G1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:40:00+00:00"",
                ""estimated"": ""2025-11-08T09:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Shandong Airlines"",
                ""iata"": ""SC"",
                ""icao"": ""CDG""
            },
            ""flight"": {
                ""number"": ""3535"",
                ""iata"": ""SC3535"",
                ""icao"": ""CDG3535"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""2749"",
                    ""flight_iata"": ""g52749"",
                    ""flight_icao"": ""hxa2749""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Lanzhou Zhongchuan Airport"",
                ""timezone"": null,
                ""iata"": ""LHW"",
                ""icao"": ""ZLLL"",
                ""terminal"": null,
                ""gate"": ""A"",
                ""delay"": 2,
                ""scheduled"": ""2025-11-08T09:15:00+00:00"",
                ""estimated"": ""2025-11-08T09:15:00+00:00"",
                ""actual"": ""2025-11-08T09:17:00+00:00"",
                ""estimated_runway"": ""2025-11-08T09:17:00+00:00"",
                ""actual_runway"": ""2025-11-08T09:17:00+00:00""
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:10:00+00:00"",
                ""delay"": null,
                ""estimated"": ""2025-11-08T10:42:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Spring Airlines"",
                ""iata"": ""9C"",
                ""icao"": ""CQH""
            },
            ""flight"": {
                ""number"": ""6185"",
                ""iata"": ""9C6185"",
                ""icao"": ""CQH6185"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Jiayuguan"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""JGN"",
                ""icao"": ""ZLJQ"",
                ""terminal"": null,
                ""gate"": ""205"",
                ""delay"": 5,
                ""scheduled"": ""2025-11-08T10:15:00+00:00"",
                ""estimated"": ""2025-11-08T10:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:05:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""China Express Air"",
                ""iata"": ""G5"",
                ""icao"": ""HXA""
            },
            ""flight"": {
                ""number"": ""2627"",
                ""iata"": ""G52627"",
                ""icao"": ""HXA2627"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Jiayuguan"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""JGN"",
                ""icao"": ""ZLJQ"",
                ""terminal"": null,
                ""gate"": ""205"",
                ""delay"": 5,
                ""scheduled"": ""2025-11-08T10:15:00+00:00"",
                ""estimated"": ""2025-11-08T10:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:05:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Xiamen Airlines"",
                ""iata"": ""MF"",
                ""icao"": ""CXA""
            },
            ""flight"": {
                ""number"": ""2403"",
                ""iata"": ""MF2403"",
                ""icao"": ""CXA2403"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""2627"",
                    ""flight_iata"": ""g52627"",
                    ""flight_icao"": ""hxa2627""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Jiayuguan"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""JGN"",
                ""icao"": ""ZLJQ"",
                ""terminal"": null,
                ""gate"": ""205"",
                ""delay"": 5,
                ""scheduled"": ""2025-11-08T10:15:00+00:00"",
                ""estimated"": ""2025-11-08T10:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:05:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Sichuan Airlines"",
                ""iata"": ""3U"",
                ""icao"": ""CSC""
            },
            ""flight"": {
                ""number"": ""4003"",
                ""iata"": ""3U4003"",
                ""icao"": ""CSC4003"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""2627"",
                    ""flight_iata"": ""g52627"",
                    ""flight_icao"": ""hxa2627""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Jiayuguan"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""JGN"",
                ""icao"": ""ZLJQ"",
                ""terminal"": null,
                ""gate"": ""205"",
                ""delay"": 5,
                ""scheduled"": ""2025-11-08T10:15:00+00:00"",
                ""estimated"": ""2025-11-08T10:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:05:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Loong Air"",
                ""iata"": ""GJ"",
                ""icao"": ""CDC""
            },
            ""flight"": {
                ""number"": ""5551"",
                ""iata"": ""GJ5551"",
                ""icao"": ""CDC5551"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""2627"",
                    ""flight_iata"": ""g52627"",
                    ""flight_icao"": ""hxa2627""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Jiayuguan"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""JGN"",
                ""icao"": ""ZLJQ"",
                ""terminal"": null,
                ""gate"": ""205"",
                ""delay"": 5,
                ""scheduled"": ""2025-11-08T10:15:00+00:00"",
                ""estimated"": ""2025-11-08T10:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dunhuang"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""DNH"",
                ""icao"": ""ZLDH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:05:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Shandong Airlines"",
                ""iata"": ""SC"",
                ""icao"": ""CDG""
            },
            ""flight"": {
                ""number"": ""3501"",
                ""iata"": ""SC3501"",
                ""icao"": ""CDG3501"",
                ""codeshared"": {
                    ""airline_name"": ""china express air"",
                    ""airline_iata"": ""g5"",
                    ""airline_icao"": ""hxa"",
                    ""flight_number"": ""2627"",
                    ""flight_iata"": ""g52627"",
                    ""flight_icao"": ""hxa2627""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Orly"",
                ""timezone"": ""Europe/Paris"",
                ""iata"": ""ORY"",
                ""icao"": ""LFPO"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""delay"": 15,
                ""scheduled"": ""2025-11-08T06:00:00+00:00"",
                ""estimated"": ""2025-11-08T06:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Habib Bourguiba"",
                ""timezone"": ""Africa/Tunis"",
                ""iata"": ""MIR"",
                ""icao"": ""DTMB"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T08:40:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Transavia"",
                ""iata"": ""HV"",
                ""icao"": ""TRA""
            },
            ""flight"": {
                ""number"": ""8938"",
                ""iata"": ""HV8938"",
                ""icao"": ""TRA8938"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Pulkovo"",
                ""timezone"": ""Europe/Moscow"",
                ""iata"": ""LED"",
                ""icao"": ""ULLI"",
                ""terminal"": null,
                ""gate"": ""75"",
                ""delay"": 11,
                ""scheduled"": ""2025-11-08T00:55:00+00:00"",
                ""estimated"": ""2025-11-08T00:55:00+00:00"",
                ""actual"": ""2025-11-08T01:05:00+00:00"",
                ""estimated_runway"": ""2025-11-08T01:05:00+00:00"",
                ""actual_runway"": ""2025-11-08T01:05:00+00:00""
            },
            ""arrival"": {
                ""airport"": ""Habib Bourguiba"",
                ""timezone"": ""Africa/Tunis"",
                ""iata"": ""MIR"",
                ""icao"": ""DTMB"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T03:20:00+00:00"",
                ""delay"": null,
                ""estimated"": ""2025-11-08T03:03:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Nouvelair Tunisie"",
                ""iata"": ""BJ"",
                ""icao"": ""LBT""
            },
            ""flight"": {
                ""number"": ""839"",
                ""iata"": ""BJ839"",
                ""icao"": ""LBT839"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 15,
                ""scheduled"": ""2025-11-08T09:40:00+00:00"",
                ""estimated"": ""2025-11-08T09:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Bergamo - Orio al Serio"",
                ""timezone"": ""Europe/Rome"",
                ""iata"": ""BGY"",
                ""icao"": ""LIME"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:55:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Ryanair"",
                ""iata"": ""FR"",
                ""icao"": ""RYR""
            },
            ""flight"": {
                ""number"": ""260"",
                ""iata"": ""FR260"",
                ""icao"": ""RYR260"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:35:00+00:00"",
                ""estimated"": ""2025-11-08T09:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Berlin Brandenburg Airport"",
                ""timezone"": ""Europe/Berlin"",
                ""iata"": ""BER"",
                ""icao"": ""EDDB"",
                ""terminal"": ""2"",
                ""gate"": ""Z34"",
                ""baggage"": ""C1"",
                ""scheduled"": ""2025-11-08T10:45:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Ryanair"",
                ""iata"": ""FR"",
                ""icao"": ""RYR""
            },
            ""flight"": {
                ""number"": ""315"",
                ""iata"": ""FR315"",
                ""icao"": ""RYR315"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:30:00+00:00"",
                ""estimated"": ""2025-11-08T09:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Ciampino"",
                ""timezone"": ""Europe/Rome"",
                ""iata"": ""CIA"",
                ""icao"": ""LIRA"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": ""1"",
                ""scheduled"": ""2025-11-08T10:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Ryanair"",
                ""iata"": ""FR"",
                ""icao"": ""RYR""
            },
            ""flight"": {
                ""number"": ""3957"",
                ""iata"": ""FR3957"",
                ""icao"": ""RYR3957"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 15,
                ""scheduled"": ""2025-11-08T09:30:00+00:00"",
                ""estimated"": ""2025-11-08T09:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""S. Angelo"",
                ""timezone"": ""Europe/Rome"",
                ""iata"": ""TSF"",
                ""icao"": ""LIPH"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Ryanair"",
                ""iata"": ""FR"",
                ""icao"": ""RYR""
            },
            ""flight"": {
                ""number"": ""1181"",
                ""iata"": ""FR1181"",
                ""icao"": ""RYR1181"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T09:00:00+00:00"",
                ""estimated"": ""2025-11-08T09:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Oradea"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OMR"",
                ""icao"": ""LROD"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:50:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""HiSky"",
                ""iata"": ""H7"",
                ""icao"": ""HYM""
            },
            ""flight"": {
                ""number"": ""762"",
                ""iata"": ""H7762"",
                ""icao"": ""HYM762"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T09:00:00+00:00"",
                ""estimated"": ""2025-11-08T09:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Oradea"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OMR"",
                ""icao"": ""LROD"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:50:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": null,
                ""iata"": ""A1"",
                ""icao"": ""A1""
            },
            ""flight"": {
                ""number"": ""5653"",
                ""iata"": ""A15653"",
                ""icao"": ""A15653"",
                ""codeshared"": {
                    ""airline_name"": ""hisky"",
                    ""airline_iata"": ""h7"",
                    ""airline_icao"": ""hym"",
                    ""flight_number"": ""762"",
                    ""flight_iata"": ""h7762"",
                    ""flight_icao"": ""hym762""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:40:00+00:00"",
                ""estimated"": ""2025-11-08T08:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Charles De Gaulle"",
                ""timezone"": ""Europe/Paris"",
                ""iata"": ""CDG"",
                ""icao"": ""LFPG"",
                ""terminal"": ""2F"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""381"",
                ""iata"": ""RO381"",
                ""icao"": ""ROT381"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:40:00+00:00"",
                ""estimated"": ""2025-11-08T08:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Charles De Gaulle"",
                ""timezone"": ""Europe/Paris"",
                ""iata"": ""CDG"",
                ""icao"": ""LFPG"",
                ""terminal"": ""2F"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Air France"",
                ""iata"": ""AF"",
                ""icao"": ""AFR""
            },
            ""flight"": {
                ""number"": ""6636"",
                ""iata"": ""AF6636"",
                ""icao"": ""AFR6636"",
                ""codeshared"": {
                    ""airline_name"": ""tarom"",
                    ""airline_iata"": ""ro"",
                    ""airline_icao"": ""rot"",
                    ""flight_number"": ""381"",
                    ""flight_iata"": ""ro381"",
                    ""flight_icao"": ""rot381""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:35:00+00:00"",
                ""estimated"": ""2025-11-08T08:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Schiphol"",
                ""timezone"": ""Europe/Amsterdam"",
                ""iata"": ""AMS"",
                ""icao"": ""EHAM"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:40:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""361"",
                ""iata"": ""RO361"",
                ""icao"": ""ROT361"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:35:00+00:00"",
                ""estimated"": ""2025-11-08T08:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Schiphol"",
                ""timezone"": ""Europe/Amsterdam"",
                ""iata"": ""AMS"",
                ""icao"": ""EHAM"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:40:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Air Baltic"",
                ""iata"": ""BT"",
                ""icao"": ""BTI""
            },
            ""flight"": {
                ""number"": ""5634"",
                ""iata"": ""BT5634"",
                ""icao"": ""BTI5634"",
                ""codeshared"": {
                    ""airline_name"": ""tarom"",
                    ""airline_iata"": ""ro"",
                    ""airline_icao"": ""rot"",
                    ""flight_number"": ""361"",
                    ""flight_iata"": ""ro361"",
                    ""flight_icao"": ""rot361""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:35:00+00:00"",
                ""estimated"": ""2025-11-08T08:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Schiphol"",
                ""timezone"": ""Europe/Amsterdam"",
                ""iata"": ""AMS"",
                ""icao"": ""EHAM"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:40:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""KLM"",
                ""iata"": ""KL"",
                ""icao"": ""KLM""
            },
            ""flight"": {
                ""number"": ""2700"",
                ""iata"": ""KL2700"",
                ""icao"": ""KLM2700"",
                ""codeshared"": {
                    ""airline_name"": ""tarom"",
                    ""airline_iata"": ""ro"",
                    ""airline_icao"": ""rot"",
                    ""flight_number"": ""361"",
                    ""flight_iata"": ""ro361"",
                    ""flight_icao"": ""rot361""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:35:00+00:00"",
                ""estimated"": ""2025-11-08T08:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Frankfurt International Airport"",
                ""timezone"": ""Europe/Berlin"",
                ""iata"": ""FRA"",
                ""icao"": ""EDDF"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""301"",
                ""iata"": ""RO301"",
                ""icao"": ""ROT301"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:35:00+00:00"",
                ""estimated"": ""2025-11-08T08:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Istanbul Airport"",
                ""timezone"": ""Europe/Istanbul"",
                ""iata"": ""IST"",
                ""icao"": ""LTFM"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:50:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""261"",
                ""iata"": ""RO261"",
                ""icao"": ""ROT261"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:35:00+00:00"",
                ""estimated"": ""2025-11-08T08:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Istanbul Airport"",
                ""timezone"": ""Europe/Istanbul"",
                ""iata"": ""IST"",
                ""icao"": ""LTFM"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:50:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Turkish Airlines"",
                ""iata"": ""TK"",
                ""icao"": ""THY""
            },
            ""flight"": {
                ""number"": ""8591"",
                ""iata"": ""TK8591"",
                ""icao"": ""THY8591"",
                ""codeshared"": {
                    ""airline_name"": ""tarom"",
                    ""airline_iata"": ""ro"",
                    ""airline_icao"": ""rot"",
                    ""flight_number"": ""261"",
                    ""flight_iata"": ""ro261"",
                    ""flight_icao"": ""rot261""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:20:00+00:00"",
                ""estimated"": ""2025-11-08T08:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Barajas"",
                ""timezone"": ""Europe/Madrid"",
                ""iata"": ""MAD"",
                ""icao"": ""LEMD"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:25:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""415"",
                ""iata"": ""RO415"",
                ""icao"": ""ROT415"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:20:00+00:00"",
                ""estimated"": ""2025-11-08T08:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Barajas"",
                ""timezone"": ""Europe/Madrid"",
                ""iata"": ""MAD"",
                ""icao"": ""LEMD"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:25:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Air Europa"",
                ""iata"": ""UX"",
                ""icao"": ""AEA""
            },
            ""flight"": {
                ""number"": ""3702"",
                ""iata"": ""UX3702"",
                ""icao"": ""AEA3702"",
                ""codeshared"": {
                    ""airline_name"": ""tarom"",
                    ""airline_iata"": ""ro"",
                    ""airline_icao"": ""rot"",
                    ""flight_number"": ""415"",
                    ""flight_iata"": ""ro415"",
                    ""flight_icao"": ""rot415""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Nagasaki"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGS"",
                ""icao"": ""RJFU"",
                ""terminal"": null,
                ""gate"": ""1A"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:50:00+00:00"",
                ""estimated"": ""2025-11-08T09:50:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Fukue"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""FUJ"",
                ""icao"": ""RJFE"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:20:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Oriental Air Bridge"",
                ""iata"": ""OC"",
                ""icao"": ""ORC""
            },
            ""flight"": {
                ""number"": ""73"",
                ""iata"": ""OC73"",
                ""icao"": ""ORC73"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:20:00+00:00"",
                ""estimated"": ""2025-11-08T08:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Sofia"",
                ""timezone"": ""Europe/Sofia"",
                ""iata"": ""SOF"",
                ""icao"": ""LBSF"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""291"",
                ""iata"": ""RO291"",
                ""icao"": ""ROT291"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Nagasaki"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGS"",
                ""icao"": ""RJFU"",
                ""terminal"": null,
                ""gate"": ""1"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:50:00+00:00"",
                ""estimated"": ""2025-11-08T09:50:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Fukue"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""FUJ"",
                ""icao"": ""RJFE"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:20:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""ANA"",
                ""iata"": ""NH"",
                ""icao"": ""ANA""
            },
            ""flight"": {
                ""number"": ""4673"",
                ""iata"": ""NH4673"",
                ""icao"": ""ANA4673"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:20:00+00:00"",
                ""estimated"": ""2025-11-08T08:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Sofia"",
                ""timezone"": ""Europe/Sofia"",
                ""iata"": ""SOF"",
                ""icao"": ""LBSF"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Bulgaria Air"",
                ""iata"": ""FB"",
                ""icao"": ""LZB""
            },
            ""flight"": {
                ""number"": ""1806"",
                ""iata"": ""FB1806"",
                ""icao"": ""LZB1806"",
                ""codeshared"": {
                    ""airline_name"": ""tarom"",
                    ""airline_iata"": ""ro"",
                    ""airline_icao"": ""rot"",
                    ""flight_number"": ""291"",
                    ""flight_iata"": ""ro291"",
                    ""flight_icao"": ""rot291""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Nagasaki"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGS"",
                ""icao"": ""RJFU"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:50:00+00:00"",
                ""estimated"": ""2025-11-08T09:50:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Haneda Airport"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""HND"",
                ""icao"": ""RJTT"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:25:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""ANA"",
                ""iata"": ""NH"",
                ""icao"": ""ANA""
            },
            ""flight"": {
                ""number"": ""2432"",
                ""iata"": ""NH2432"",
                ""icao"": ""ANA2432"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:20:00+00:00"",
                ""estimated"": ""2025-11-08T08:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Sofia"",
                ""timezone"": ""Europe/Sofia"",
                ""iata"": ""SOF"",
                ""icao"": ""LBSF"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Air France"",
                ""iata"": ""AF"",
                ""icao"": ""AFR""
            },
            ""flight"": {
                ""number"": ""6623"",
                ""iata"": ""AF6623"",
                ""icao"": ""AFR6623"",
                ""codeshared"": {
                    ""airline_name"": ""tarom"",
                    ""airline_iata"": ""ro"",
                    ""airline_icao"": ""rot"",
                    ""flight_number"": ""291"",
                    ""flight_iata"": ""ro291"",
                    ""flight_icao"": ""rot291""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Nagasaki"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGS"",
                ""icao"": ""RJFU"",
                ""terminal"": null,
                ""gate"": ""5"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:35:00+00:00"",
                ""estimated"": ""2025-11-08T09:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:45:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""ANA"",
                ""iata"": ""NH"",
                ""icao"": ""ANA""
            },
            ""flight"": {
                ""number"": ""372"",
                ""iata"": ""NH372"",
                ""icao"": ""ANA372"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T08:20:00+00:00"",
                ""estimated"": ""2025-11-08T08:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Frederic Chopin"",
                ""timezone"": ""Europe/Warsaw"",
                ""iata"": ""WAW"",
                ""icao"": ""EPWA"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""LOT - Polish Airlines"",
                ""iata"": ""LO"",
                ""icao"": ""LOT""
            },
            ""flight"": {
                ""number"": ""640"",
                ""iata"": ""LO640"",
                ""icao"": ""LOT640"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""active"",
            ""departure"": {
                ""airport"": ""Nagasaki"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGS"",
                ""icao"": ""RJFU"",
                ""terminal"": null,
                ""gate"": ""3"",
                ""delay"": 9,
                ""scheduled"": ""2025-11-08T09:10:00+00:00"",
                ""estimated"": ""2025-11-08T09:10:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Itami"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""ITM"",
                ""icao"": ""RJOO"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:20:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Japan Airlines"",
                ""iata"": ""JL"",
                ""icao"": ""JAL""
            },
            ""flight"": {
                ""number"": ""2372"",
                ""iata"": ""JL2372"",
                ""icao"": ""JAL2372"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T08:20:00+00:00"",
                ""estimated"": ""2025-11-08T08:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Frederic Chopin"",
                ""timezone"": ""Europe/Warsaw"",
                ""iata"": ""WAW"",
                ""icao"": ""EPWA"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAP Air Portugal"",
                ""iata"": ""TP"",
                ""icao"": ""TAP""
            },
            ""flight"": {
                ""number"": ""7072"",
                ""iata"": ""TP7072"",
                ""icao"": ""TAP7072"",
                ""codeshared"": {
                    ""airline_name"": ""lot - polish airlines"",
                    ""airline_iata"": ""lo"",
                    ""airline_icao"": ""lot"",
                    ""flight_number"": ""640"",
                    ""flight_iata"": ""lo640"",
                    ""flight_icao"": ""lot640""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:05:00+00:00"",
                ""estimated"": ""2025-11-08T08:05:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Heathrow"",
                ""timezone"": ""Europe/London"",
                ""iata"": ""LHR"",
                ""icao"": ""EGLL"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""British Airways"",
                ""iata"": ""BA"",
                ""icao"": ""BAW""
            },
            ""flight"": {
                ""number"": ""889"",
                ""iata"": ""BA889"",
                ""icao"": ""BAW889"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:05:00+00:00"",
                ""estimated"": ""2025-11-08T08:05:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Heathrow"",
                ""timezone"": ""Europe/London"",
                ""iata"": ""LHR"",
                ""icao"": ""EGLL"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Japan Airlines"",
                ""iata"": ""JL"",
                ""icao"": ""JTL""
            },
            ""flight"": {
                ""number"": ""6534"",
                ""iata"": ""JL6534"",
                ""icao"": ""JTL6534"",
                ""codeshared"": {
                    ""airline_name"": ""british airways"",
                    ""airline_iata"": ""ba"",
                    ""airline_icao"": ""baw"",
                    ""flight_number"": ""889"",
                    ""flight_iata"": ""ba889"",
                    ""flight_icao"": ""baw889""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:05:00+00:00"",
                ""estimated"": ""2025-11-08T08:05:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Heathrow"",
                ""timezone"": ""Europe/London"",
                ""iata"": ""LHR"",
                ""icao"": ""EGLL"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""American Airlines"",
                ""iata"": ""AA"",
                ""icao"": ""AAL""
            },
            ""flight"": {
                ""number"": ""7192"",
                ""iata"": ""AA7192"",
                ""icao"": ""AAL7192"",
                ""codeshared"": {
                    ""airline_name"": ""british airways"",
                    ""airline_iata"": ""ba"",
                    ""airline_icao"": ""baw"",
                    ""flight_number"": ""889"",
                    ""flight_iata"": ""ba889"",
                    ""flight_icao"": ""baw889""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 17,
                ""scheduled"": ""2025-11-08T08:00:00+00:00"",
                ""estimated"": ""2025-11-08T08:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Valencia"",
                ""timezone"": ""Europe/Madrid"",
                ""iata"": ""VLC"",
                ""icao"": ""LEVC"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:45:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Wizz Air"",
                ""iata"": ""W6"",
                ""icao"": ""WZZ""
            },
            ""flight"": {
                ""number"": ""3185"",
                ""iata"": ""W63185"",
                ""icao"": ""WZZ3185"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:00:00+00:00"",
                ""estimated"": ""2025-11-08T08:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Belgrade Nikola Tesla"",
                ""timezone"": ""Europe/Belgrade"",
                ""iata"": ""BEG"",
                ""icao"": ""LYBE"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T08:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""211"",
                ""iata"": ""RO211"",
                ""icao"": ""ROT211"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T08:00:00+00:00"",
                ""estimated"": ""2025-11-08T08:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dubai"",
                ""timezone"": ""Asia/Dubai"",
                ""iata"": ""DXB"",
                ""icao"": ""OMDB"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T15:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""flydubai"",
                ""iata"": ""FZ"",
                ""icao"": ""FDB""
            },
            ""flight"": {
                ""number"": ""1710"",
                ""iata"": ""FZ1710"",
                ""icao"": ""FDB1710"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T08:00:00+00:00"",
                ""estimated"": ""2025-11-08T08:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Dubai"",
                ""timezone"": ""Asia/Dubai"",
                ""iata"": ""DXB"",
                ""icao"": ""OMDB"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T15:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Emirates"",
                ""iata"": ""EK"",
                ""icao"": ""UAE""
            },
            ""flight"": {
                ""number"": ""2160"",
                ""iata"": ""EK2160"",
                ""icao"": ""UAE2160"",
                ""codeshared"": {
                    ""airline_name"": ""flydubai"",
                    ""airline_iata"": ""fz"",
                    ""airline_icao"": ""fdb"",
                    ""flight_number"": ""1710"",
                    ""flight_iata"": ""fz1710"",
                    ""flight_icao"": ""fdb1710""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T07:50:00+00:00"",
                ""estimated"": ""2025-11-08T07:50:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Prague Vaclav Havel Airport"",
                ""timezone"": ""Europe/Prague"",
                ""iata"": ""PRG"",
                ""icao"": ""LKPR"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T08:45:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""225"",
                ""iata"": ""RO225"",
                ""icao"": ""ROT225"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T07:40:00+00:00"",
                ""estimated"": ""2025-11-08T07:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Leonardo Da Vinci (Fiumicino)"",
                ""timezone"": ""Europe/Rome"",
                ""iata"": ""FCO"",
                ""icao"": ""LIRF"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Wizz Air"",
                ""iata"": ""W6"",
                ""icao"": ""WZZ""
            },
            ""flight"": {
                ""number"": ""3141"",
                ""iata"": ""W63141"",
                ""icao"": ""WZZ3141"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T07:40:00+00:00"",
                ""estimated"": ""2025-11-08T07:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Cluj Napoca International Airport"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""CLJ"",
                ""icao"": ""LRCL"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T08:30:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""HiSky"",
                ""iata"": ""H7"",
                ""icao"": ""HYM""
            },
            ""flight"": {
                ""number"": ""278"",
                ""iata"": ""H7278"",
                ""icao"": ""HYM278"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""estimated"": ""2025-11-08T10:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Mwanza"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""MWZ"",
                ""icao"": ""HTMW"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Flightlink"",
                ""iata"": ""YS"",
                ""icao"": ""FLZ""
            },
            ""flight"": {
                ""number"": ""601"",
                ""iata"": ""YS601"",
                ""icao"": ""FLZ601"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T07:40:00+00:00"",
                ""estimated"": ""2025-11-08T07:40:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Cluj Napoca International Airport"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""CLJ"",
                ""icao"": ""LRCL"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T08:30:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": null,
                ""iata"": ""A1"",
                ""icao"": ""A1""
            },
            ""flight"": {
                ""number"": ""3316"",
                ""iata"": ""A13316"",
                ""icao"": ""A13316"",
                ""codeshared"": {
                    ""airline_name"": ""hisky"",
                    ""airline_iata"": ""h7"",
                    ""airline_icao"": ""hym"",
                    ""flight_number"": ""278"",
                    ""flight_iata"": ""h7278"",
                    ""flight_icao"": ""hym278""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""estimated"": ""2025-11-08T10:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Seronera"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""SEU"",
                ""icao"": ""HTSN"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Flightlink"",
                ""iata"": ""YS"",
                ""icao"": ""FLZ""
            },
            ""flight"": {
                ""number"": ""101"",
                ""iata"": ""YS101"",
                ""icao"": ""FLZ101"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T07:20:00+00:00"",
                ""estimated"": ""2025-11-08T07:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Timisoara (traian Vuia) International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""TSR"",
                ""icao"": ""LRTR"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T08:40:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""TAROM"",
                ""iata"": ""RO"",
                ""icao"": ""ROT""
            },
            ""flight"": {
                ""number"": ""601"",
                ""iata"": ""RO601"",
                ""icao"": ""ROT601"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""estimated"": ""2025-11-08T10:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Mwanza"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""MWZ"",
                ""icao"": ""HTMW"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""empty"",
                ""iata"": null,
                ""icao"": null
            },
            ""flight"": {
                ""number"": ""468"",
                ""iata"": null,
                ""icao"": null,
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T07:20:00+00:00"",
                ""estimated"": ""2025-11-08T07:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Timisoara (traian Vuia) International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""TSR"",
                ""icao"": ""LRTR"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T08:40:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""El Al"",
                ""iata"": ""LY"",
                ""icao"": ""ELY""
            },
            ""flight"": {
                ""number"": ""9511"",
                ""iata"": ""LY9511"",
                ""icao"": ""ELY9511"",
                ""codeshared"": {
                    ""airline_name"": ""tarom"",
                    ""airline_iata"": ""ro"",
                    ""airline_icao"": ""rot"",
                    ""flight_number"": ""601"",
                    ""flight_iata"": ""ro601"",
                    ""flight_icao"": ""rot601""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""estimated"": ""2025-11-08T10:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Kisauni"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ZNZ"",
                ""icao"": ""HTZA"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""empty"",
                ""iata"": null,
                ""icao"": null
            },
            ""flight"": {
                ""number"": ""444"",
                ""iata"": null,
                ""icao"": null,
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T07:10:00+00:00"",
                ""estimated"": ""2025-11-08T07:10:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Hurghada"",
                ""timezone"": ""Africa/Cairo"",
                ""iata"": ""HRG"",
                ""icao"": ""HEGN"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:12:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Animawings"",
                ""iata"": ""A2"",
                ""icao"": ""AWG""
            },
            ""flight"": {
                ""number"": ""4206"",
                ""iata"": ""A24206"",
                ""icao"": ""AWG4206"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""estimated"": ""2025-11-08T10:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Seronera"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""SEU"",
                ""icao"": ""HTSN"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T11:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""empty"",
                ""iata"": null,
                ""icao"": null
            },
            ""flight"": {
                ""number"": ""440"",
                ""iata"": null,
                ""icao"": null,
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T07:10:00+00:00"",
                ""estimated"": ""2025-11-08T07:10:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Luton Airport"",
                ""timezone"": ""Europe/London"",
                ""iata"": ""LTN"",
                ""icao"": ""EGGW"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T08:45:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Wizz Air"",
                ""iata"": ""W6"",
                ""icao"": ""WZZ""
            },
            ""flight"": {
                ""number"": ""3001"",
                ""iata"": ""W63001"",
                ""icao"": ""WZZ3001"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T10:00:00+00:00"",
                ""estimated"": ""2025-11-08T10:00:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Kilimanjaro"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""JRO"",
                ""icao"": ""HTKJ"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Auric Air"",
                ""iata"": ""UI"",
                ""icao"": ""AUK""
            },
            ""flight"": {
                ""number"": ""616"",
                ""iata"": ""UI616"",
                ""icao"": ""AUK616"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T06:55:00+00:00"",
                ""estimated"": ""2025-11-08T06:55:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Orly"",
                ""timezone"": ""Europe/Paris"",
                ""iata"": ""ORY"",
                ""icao"": ""LFPO"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Wizz Air"",
                ""iata"": ""W6"",
                ""icao"": ""WZZ""
            },
            ""flight"": {
                ""number"": ""3051"",
                ""iata"": ""W63051"",
                ""icao"": ""WZZ3051"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:50:00+00:00"",
                ""estimated"": ""2025-11-08T09:50:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Lake Manyara"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""LKY"",
                ""icao"": ""HTLM"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:05:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Auric Air"",
                ""iata"": ""UI"",
                ""icao"": ""AUK""
            },
            ""flight"": {
                ""number"": ""621"",
                ""iata"": ""UI621"",
                ""icao"": ""AUK621"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T06:55:00+00:00"",
                ""estimated"": ""2025-11-08T06:55:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Larnaca"",
                ""timezone"": ""Asia/Nicosia"",
                ""iata"": ""LCA"",
                ""icao"": ""LCLK"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Wizz Air"",
                ""iata"": ""W6"",
                ""icao"": ""WZZ""
            },
            ""flight"": {
                ""number"": ""3025"",
                ""iata"": ""W63025"",
                ""icao"": ""WZZ3025"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T06:55:00+00:00"",
                ""estimated"": ""2025-11-08T06:55:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Vienna International"",
                ""timezone"": ""Europe/Vienna"",
                ""iata"": ""VIE"",
                ""icao"": ""LOWW"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T07:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Austrian"",
                ""iata"": ""OS"",
                ""icao"": ""AUA""
            },
            ""flight"": {
                ""number"": ""700"",
                ""iata"": ""OS700"",
                ""icao"": ""AUA700"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:50:00+00:00"",
                ""estimated"": ""2025-11-08T09:50:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Lake Manyara"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""LKY"",
                ""icao"": ""HTLM"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:05:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""FlexFlight"",
                ""iata"": ""W2"",
                ""icao"": ""FXT""
            },
            ""flight"": {
                ""number"": ""1322"",
                ""iata"": ""W21322"",
                ""icao"": ""FXT1322"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T06:55:00+00:00"",
                ""estimated"": ""2025-11-08T06:55:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Vienna International"",
                ""timezone"": ""Europe/Vienna"",
                ""iata"": ""VIE"",
                ""icao"": ""LOWW"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T07:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Luxair"",
                ""iata"": ""LG"",
                ""icao"": ""LGL""
            },
            ""flight"": {
                ""number"": ""1782"",
                ""iata"": ""LG1782"",
                ""icao"": ""LGL1782"",
                ""codeshared"": {
                    ""airline_name"": ""austrian"",
                    ""airline_iata"": ""os"",
                    ""airline_icao"": ""aua"",
                    ""flight_number"": ""700"",
                    ""flight_iata"": ""os700"",
                    ""flight_icao"": ""aua700""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T09:30:00+00:00"",
                ""estimated"": ""2025-11-08T09:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Kilimanjaro"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""JRO"",
                ""icao"": ""HTKJ"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T09:45:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Air Excel"",
                ""iata"": null,
                ""icao"": ""XLL""
            },
            ""flight"": {
                ""number"": ""460"",
                ""iata"": null,
                ""icao"": ""XLL460"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Henri Coanda International"",
                ""timezone"": ""Europe/Bucharest"",
                ""iata"": ""OTP"",
                ""icao"": ""LROP"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": 10,
                ""scheduled"": ""2025-11-08T06:55:00+00:00"",
                ""estimated"": ""2025-11-08T06:55:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Vienna International"",
                ""timezone"": ""Europe/Vienna"",
                ""iata"": ""VIE"",
                ""icao"": ""LOWW"",
                ""terminal"": ""3"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T07:35:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Air Canada"",
                ""iata"": ""AC"",
                ""icao"": ""ACA""
            },
            ""flight"": {
                ""number"": ""6181"",
                ""iata"": ""AC6181"",
                ""icao"": ""ACA6181"",
                ""codeshared"": {
                    ""airline_name"": ""austrian"",
                    ""airline_iata"": ""os"",
                    ""airline_icao"": ""aua"",
                    ""flight_number"": ""700"",
                    ""flight_iata"": ""os700"",
                    ""flight_icao"": ""aua700""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Arusha"",
                ""timezone"": ""Africa/Dar_es_Salaam"",
                ""iata"": ""ARK"",
                ""icao"": ""HTAR"",
                ""terminal"": null,
                ""gate"": null,
                ""delay"": null,
                ""scheduled"": ""2025-11-08T08:30:00+00:00"",
                ""estimated"": ""2025-11-08T08:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": null,
                ""timezone"": null,
                ""iata"": ""GTZ"",
                ""icao"": ""HTGR"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T10:30:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Regional Air"",
                ""iata"": ""8N"",
                ""icao"": ""REG""
            },
            ""flight"": {
                ""number"": ""1012"",
                ""iata"": ""8N1012"",
                ""icao"": ""REG1012"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""101"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:35:00+00:00"",
                ""estimated"": ""2025-11-08T15:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Akita"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""AXT"",
                ""icao"": ""RJSK"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T16:55:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Oriental Air Bridge"",
                ""iata"": ""OC"",
                ""icao"": ""ORC""
            },
            ""flight"": {
                ""number"": ""87"",
                ""iata"": ""OC87"",
                ""icao"": ""ORC87"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""101"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:35:00+00:00"",
                ""estimated"": ""2025-11-08T15:35:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Akita"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""AXT"",
                ""icao"": ""RJSK"",
                ""terminal"": null,
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T16:55:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""ANA"",
                ""iata"": ""NH"",
                ""icao"": ""ANA""
            },
            ""flight"": {
                ""number"": ""4687"",
                ""iata"": ""NH4687"",
                ""icao"": ""ANA4687"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""9"",
                ""delay"": 15,
                ""scheduled"": ""2025-11-08T15:30:00+00:00"",
                ""estimated"": ""2025-11-08T15:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Chitose"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""CTS"",
                ""icao"": ""RJCC"",
                ""terminal"": ""D"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T17:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Air Do"",
                ""iata"": ""HD"",
                ""icao"": ""ADO""
            },
            ""flight"": {
                ""number"": ""135"",
                ""iata"": ""HD135"",
                ""icao"": ""ADO135"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""9"",
                ""delay"": 15,
                ""scheduled"": ""2025-11-08T15:30:00+00:00"",
                ""estimated"": ""2025-11-08T15:30:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Chitose"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""CTS"",
                ""icao"": ""RJCC"",
                ""terminal"": ""D"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T17:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""ANA"",
                ""iata"": ""NH"",
                ""icao"": ""ANA""
            },
            ""flight"": {
                ""number"": ""4835"",
                ""iata"": ""NH4835"",
                ""icao"": ""ANA4835"",
                ""codeshared"": {
                    ""airline_name"": ""air do"",
                    ""airline_iata"": ""hd"",
                    ""airline_icao"": ""ado"",
                    ""flight_number"": ""135"",
                    ""flight_iata"": ""hd135"",
                    ""flight_icao"": ""ado135""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""2"",
                ""gate"": ""B"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:25:00+00:00"",
                ""estimated"": ""2025-11-08T15:25:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Shanghai Pudong International"",
                ""timezone"": ""Asia/Shanghai"",
                ""iata"": ""PVG"",
                ""icao"": ""ZSPD"",
                ""terminal"": ""2"",
                ""gate"": null,
                ""baggage"": ""33"",
                ""scheduled"": ""2025-11-08T17:25:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Spring Airlines"",
                ""iata"": ""9C"",
                ""icao"": ""CQH""
            },
            ""flight"": {
                ""number"": ""8602"",
                ""iata"": ""9C8602"",
                ""icao"": ""CQH8602"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""2"",
                ""gate"": ""A"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:25:00+00:00"",
                ""estimated"": ""2025-11-08T15:25:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Hong Kong International"",
                ""timezone"": ""Asia/Hong_Kong"",
                ""iata"": ""HKG"",
                ""icao"": ""VHHH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T19:15:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Hong Kong Express"",
                ""iata"": ""UO"",
                ""icao"": ""HKE""
            },
            ""flight"": {
                ""number"": ""681"",
                ""iata"": ""UO681"",
                ""icao"": ""HKE681"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""19"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:20:00+00:00"",
                ""estimated"": ""2025-11-08T15:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Gimhae"",
                ""timezone"": ""Asia/Seoul"",
                ""iata"": ""PUS"",
                ""icao"": ""RKPK"",
                ""terminal"": ""I"",
                ""gate"": ""5"",
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T17:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Korean Air"",
                ""iata"": ""KE"",
                ""icao"": ""KAL""
            },
            ""flight"": {
                ""number"": ""2134"",
                ""iata"": ""KE2134"",
                ""icao"": ""KAL2134"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""19"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:20:00+00:00"",
                ""estimated"": ""2025-11-08T15:20:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Gimhae"",
                ""timezone"": ""Asia/Seoul"",
                ""iata"": ""PUS"",
                ""icao"": ""RKPK"",
                ""terminal"": ""I"",
                ""gate"": ""5"",
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T17:00:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Japan Airlines"",
                ""iata"": ""JL"",
                ""icao"": ""JTL""
            },
            ""flight"": {
                ""number"": ""5239"",
                ""iata"": ""JL5239"",
                ""icao"": ""JTL5239"",
                ""codeshared"": {
                    ""airline_name"": ""korean air"",
                    ""airline_iata"": ""ke"",
                    ""airline_icao"": ""kal"",
                    ""flight_number"": ""2134"",
                    ""flight_iata"": ""ke2134"",
                    ""flight_icao"": ""kal2134""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""I,J"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:15:00+00:00"",
                ""estimated"": ""2025-11-08T15:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Hong Kong International"",
                ""timezone"": ""Asia/Hong_Kong"",
                ""iata"": ""HKG"",
                ""icao"": ""VHHH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T19:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Cathay Pacific"",
                ""iata"": ""CX"",
                ""icao"": ""CPA""
            },
            ""flight"": {
                ""number"": ""539"",
                ""iata"": ""CX539"",
                ""icao"": ""CPA539"",
                ""codeshared"": null
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""I,J"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:15:00+00:00"",
                ""estimated"": ""2025-11-08T15:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Hong Kong International"",
                ""timezone"": ""Asia/Hong_Kong"",
                ""iata"": ""HKG"",
                ""icao"": ""VHHH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T19:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Qatar Airways"",
                ""iata"": ""QR"",
                ""icao"": ""QTR""
            },
            ""flight"": {
                ""number"": ""5855"",
                ""iata"": ""QR5855"",
                ""icao"": ""QTR5855"",
                ""codeshared"": {
                    ""airline_name"": ""cathay pacific"",
                    ""airline_iata"": ""cx"",
                    ""airline_icao"": ""cpa"",
                    ""flight_number"": ""539"",
                    ""flight_iata"": ""cx539"",
                    ""flight_icao"": ""cpa539""
                }
            },
            ""aircraft"": null,
            ""live"": null
        },
        {
            ""flight_date"": ""2025-11-08"",
            ""flight_status"": ""scheduled"",
            ""departure"": {
                ""airport"": ""Chu-Bu Centrair International (Central Japan International)"",
                ""timezone"": ""Asia/Tokyo"",
                ""iata"": ""NGO"",
                ""icao"": ""RJGG"",
                ""terminal"": ""1"",
                ""gate"": ""I,J"",
                ""delay"": null,
                ""scheduled"": ""2025-11-08T15:15:00+00:00"",
                ""estimated"": ""2025-11-08T15:15:00+00:00"",
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""arrival"": {
                ""airport"": ""Hong Kong International"",
                ""timezone"": ""Asia/Hong_Kong"",
                ""iata"": ""HKG"",
                ""icao"": ""VHHH"",
                ""terminal"": ""1"",
                ""gate"": null,
                ""baggage"": null,
                ""scheduled"": ""2025-11-08T19:10:00+00:00"",
                ""delay"": null,
                ""estimated"": null,
                ""actual"": null,
                ""estimated_runway"": null,
                ""actual_runway"": null
            },
            ""airline"": {
                ""name"": ""Qantas"",
                ""iata"": ""QF"",
                ""icao"": ""QFA""
            },
            ""flight"": {
                ""number"": ""8258"",
                ""iata"": ""QF8258"",
                ""icao"": ""QFA8258"",
                ""codeshared"": {
                    ""airline_name"": ""cathay pacific"",
                    ""airline_iata"": ""cx"",
                    ""airline_icao"": ""cpa"",
                    ""flight_number"": ""539"",
                    ""flight_iata"": ""cx539"",
                    ""flight_icao"": ""cpa539""
                }
            },
            ""aircraft"": null,
            ""live"": null
        }
    ]
}");*/
                #endregion
                
                dto.Datetime = DateTime.UtcNow.AddHours(-5);
                
                var json = JsonSerializer.Serialize(dto, JsonOpts);

                var db = _redis.Db;
                var prefix = _cfg.GetValue("Redis:KeyPrefix", "flights");
                var ttl = TimeSpan.FromMinutes(_cfg.GetValue("Redis:TtlMinutes", 60));

                var keyLast = $"{prefix}:last";
                var versionStamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var keySnap = $"{prefix}:snapshot:{DateTime.UtcNow:yyyyMMddHHmmss}";
                var keyIndex = $"{prefix}:index:{DateTime.UtcNow:yyyyMMdd}";

                await db.ExecuteAsync("JSON.SET", keyLast, "$", json);
                await db.KeyExpireAsync(keyLast, ttl);

                await db.ExecuteAsync("JSON.SET", keySnap, "$", json);
                await db.KeyExpireAsync(keySnap, TimeSpan.FromDays(2));

                await db.ListLeftPushAsync(keyIndex, keySnap);
                await db.KeyExpireAsync(keyIndex, TimeSpan.FromDays(2));

                await db.StringSetAsync($"{prefix}:version", versionStamp);

                _log.LogInformation("Saved in Redis: {last} & {snap}", keyLast, keySnap);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Ingestion failed");
            }
        }
    }
}
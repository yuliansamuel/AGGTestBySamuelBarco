using Agg.Test;
using Agg.Test.Infrastructure;
using Agg.Test.Jobs;
using Agg.Test.Services;
using Microsoft.AspNetCore.Authentication;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------
// Logging
// ---------------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ---------------------------
// Controllers + Swagger
// ---------------------------
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    // Security definition for API Key in header
    o.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "Provide your API Key in the header `X-API-KEY`",
        Name = "X-API-KEY",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "ApiKey"
    });

    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ---------------------------
// HttpClient(s)
// ---------------------------
builder.Services.AddHttpClient("aviation", c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
    c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
});

// ---------------------------
// Redis
// ---------------------------
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var cfg = builder.Configuration.GetSection("Redis");
    var options = new ConfigurationOptions
    {
        EndPoints = { { cfg["Host"]!, int.Parse(cfg["Port"]!) } },
        User = cfg["User"],
        Password = cfg["Password"],
        Ssl = bool.Parse(cfg["Ssl"] ?? "false"),
        AbortOnConnectFail = false,
        ConnectRetry = 3,
        ConnectTimeout = 10000
    };
    return ConnectionMultiplexer.Connect(options);
});

builder.Services.AddSingleton<IRedisDbProvider, RedisDbProvider>();

// ---------------------------
// DB / Services / Jobs
// ---------------------------
builder.Services.AddSingleton<IDbExecutor, MySqlDbExecutor>();
builder.Services.AddSingleton<IFlights, Flights>();
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddHostedService<FlightsJob>();

// ---------------------------
// AuthN/AuthZ (API Key en X-API-KEY)
// ---------------------------
builder.Services
    .AddAuthentication("ApiKey")
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

var app = builder.Build();

// ---------------------------
// Pipeline
// ---------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

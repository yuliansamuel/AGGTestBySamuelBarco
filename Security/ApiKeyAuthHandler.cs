using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Agg.Test.Infrastructure;

/// <summary>
/// Custom authentication handler that validates incoming API requests
/// using an API key provided via the <c>X-API-KEY</c> header.
/// </summary>
/// <remarks>
/// The handler checks the provided key against one or more valid keys
/// defined in <c>appsettings.json</c> under <c>Auth:ApiKey</c> or <c>Auth:ApiKeys</c>.
/// </remarks>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string HeaderName = "X-API-KEY";
    private readonly IConfiguration _cfg;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyAuthenticationHandler"/> class.
    /// </summary>
    /// <param name="options">Monitors authentication scheme options.</param>
    /// <param name="logger">Logger factory for diagnostics.</param>
    /// <param name="encoder">Encoder for URL-safe operations.</param>
    /// <param name="clock">System clock used for time-sensitive operations.</param>
    /// <param name="cfg">Application configuration provider.</param>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IConfiguration cfg)
        : base(options, logger, encoder, clock)
    {
        _cfg = cfg;
    }

    /// <summary>
    /// Handles the authentication logic for requests.
    /// </summary>
    /// <remarks>
    /// - Extracts the API key from the <c>X-API-KEY</c> header.<br/>
    /// - Compares it against configured valid keys.<br/>
    /// - Creates a <see cref="ClaimsPrincipal"/> when authentication succeeds.<br/>
    /// - Returns <see cref="AuthenticateResult.Fail"/> if no or invalid key is provided.
    /// </remarks>
    /// <returns>
    /// An <see cref="AuthenticateResult"/> representing success or failure of authentication.
    /// </returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.TryGetValue(HeaderName, out var apiKeyHeader) == false)
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API Key"));
        }

        var singleKey = _cfg["Auth:ApiKey"];
        var manyKeys = _cfg.GetSection("Auth:ApiKeys").Get<string[]>() ?? Array.Empty<string>();

        var valid = false;
        var provided = apiKeyHeader.ToString();

        if (!string.IsNullOrWhiteSpace(singleKey))
            valid = string.Equals(provided, singleKey, StringComparison.Ordinal);

        if (!valid && manyKeys.Length > 0)
            valid = manyKeys.Contains(provided, StringComparer.Ordinal);

        if (!valid)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "api-key"),
            new Claim(ClaimTypes.Name, "ApiKeyClient")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Handles unauthorized requests (HTTP 401).
    /// </summary>
    /// <remarks>
    /// Sets the HTTP response status code to <c>401 Unauthorized</c>
    /// and allows the base handler to manage the response pipeline.
    /// </remarks>
    /// <param name="properties">Authentication properties associated with the challenge.</param>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return base.HandleChallengeAsync(properties);
    }
}

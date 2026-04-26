using System.Security.Claims;
using System.Text.Json;
using JellyFederation.Data;
using JellyFederation.Server.Options;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JellyFederation.Server.Auth;

public sealed class FederationApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IMemoryCache _cache;
    private readonly FederationDbContext _db;
    private readonly ILogger<FederationApiKeyAuthenticationHandler> _logger;
    private readonly SecurityOptions _securityOptions;
    private readonly WebSessionService _sessions;

    public FederationApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        FederationDbContext db,
        IMemoryCache cache,
        WebSessionService sessions,
        IOptions<SecurityOptions> securityOptions)
        : base(options, logger, encoder)
    {
        _db = db;
        _cache = cache;
        _sessions = sessions;
        _securityOptions = securityOptions.Value;
        _logger = logger.CreateLogger<FederationApiKeyAuthenticationHandler>();
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var correlationId = ResolveCorrelationId();
        Context.Items["CorrelationId"] = correlationId;

        var server = await AuthenticateServerAsync().ConfigureAwait(false);
        if (server is null)
            return AuthenticateResult.Fail("Missing or invalid credentials.");

        Context.Items["Server"] = server;

        var serverId = server.Id.ToString();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, serverId),
            new(FederationAuthSchemes.ServerIdClaimType, serverId),
            new(ClaimTypes.Name, server.Name),
            new("jf.owner_user_id", server.OwnerUserId)
        };

        var identity = new ClaimsIdentity(claims, FederationAuthSchemes.ApiKeyOrSession);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, FederationAuthSchemes.ApiKeyOrSession);
        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Response.HasStarted)
            return;

        var correlationId = ResolveCorrelationId();
        Context.Items["CorrelationId"] = correlationId;

        var failure = FailureDescriptor.Authorization(
            "api.auth.invalid_credentials",
            "Missing or invalid credentials.",
            correlationId);

        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        await Response.WriteAsync(JsonSerializer.Serialize(new ErrorEnvelope(ErrorContractMapper.ToContract(failure))))
            .ConfigureAwait(false);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (Response.HasStarted)
            return;

        var correlationId = ResolveCorrelationId();
        Context.Items["CorrelationId"] = correlationId;

        var failure = FailureDescriptor.Authorization(
            "api.auth.forbidden",
            "Insufficient permissions.",
            correlationId);

        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        await Response.WriteAsync(JsonSerializer.Serialize(new ErrorEnvelope(ErrorContractMapper.ToContract(failure))))
            .ConfigureAwait(false);
    }

    private async Task<RegisteredServer?> AuthenticateServerAsync()
    {
        var apiKey = ResolveApiKey(Request);
        if (!string.IsNullOrWhiteSpace(apiKey))
            return await ResolveByApiKeyAsync(apiKey).ConfigureAwait(false);

        return await _sessions.AuthenticateCookieAsync(Request).ConfigureAwait(false);
    }

    private async Task<RegisteredServer?> ResolveByApiKeyAsync(string apiKey)
    {
        var cacheKey = $"apikey:{apiKey}";
        if (_cache.TryGetValue(cacheKey, out RegisteredServer? cachedServer))
            return cachedServer;

        var server = await _db.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ApiKey == apiKey)
            .ConfigureAwait(false);

        if (server is not null)
            _cache.Set(cacheKey, server, CacheTtl);
        else
            _logger.LogWarning("Rejected API key for path {Path}", Request.Path);

        return server;
    }

    private string? ResolveApiKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            var apiKey = apiKeyHeader.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(apiKey))
                return apiKey;
        }

        if (request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            var authorization = authorizationHeader.ToString();
            const string bearerPrefix = "Bearer ";
            if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var bearerToken = authorization[bearerPrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(bearerToken))
                    return bearerToken;
            }
        }

        var isHubRequest = request.Path.StartsWithSegments("/hubs/federation", StringComparison.OrdinalIgnoreCase);
        if (!isHubRequest)
            return null;

        var accessToken = request.Query["access_token"].ToString();
        if (!string.IsNullOrWhiteSpace(accessToken))
            return accessToken;

        if (!_securityOptions.AllowLegacySignalRApiKeyQuery)
            return null;

        var legacyApiKey = request.Query["apiKey"].ToString();
        return string.IsNullOrWhiteSpace(legacyApiKey) ? null : legacyApiKey;
    }

    private string ResolveCorrelationId()
    {
        if (Context.Items.TryGetValue("CorrelationId", out var current) &&
            current is string existing &&
            !string.IsNullOrWhiteSpace(existing))
            return existing;

        if (Request.Headers.TryGetValue("X-Correlation-ID", out var requested) &&
            !string.IsNullOrWhiteSpace(requested))
            return requested.ToString().Trim();

        return FederationTelemetry.CreateCorrelationId();
    }
}

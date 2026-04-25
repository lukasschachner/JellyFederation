using System.Diagnostics;
using JellyFederation.Data;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JellyFederation.Server.Filters;

public partial class ApiKeyAuthFilter : IAsyncActionFilter
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly IMemoryCache _cache;
    private readonly FederationDbContext _db;
    private readonly ErrorContractMapper _errorMapper;
    private readonly ILogger<ApiKeyAuthFilter> _logger;
    private readonly WebSessionService _sessions;

    public ApiKeyAuthFilter(FederationDbContext db,
        IMemoryCache cache,
        ErrorContractMapper errorMapper,
        WebSessionService sessions,
        ILogger<ApiKeyAuthFilter> logger)
    {
        _db = db;
        _cache = cache;
        _errorMapper = errorMapper;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var startedAt = Stopwatch.StartNew();
        // Read the correlation ID directly instead of materialising all headers into a dictionary.
        var correlationId = context.HttpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var cid) &&
                            !string.IsNullOrWhiteSpace(cid)
            ? cid.ToString().Trim()
            : FederationTelemetry.CreateCorrelationId();
        context.HttpContext.Items["CorrelationId"] = correlationId;

        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanServerHttpRequest);
        FederationTelemetry.SetCommonTags(activity, "api.auth", "server", correlationId, releaseVersion: "server");

        var server = await AuthenticateAsync(context.HttpContext).ConfigureAwait(false);
        if (server is null)
        {
            LogApiKeyRejected(_logger, context.HttpContext.Request.Path);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            context.Result = ErrorContractMapper.ToActionResult(FailureDescriptor.Authorization(
                "api.auth.invalid_credentials",
                "Missing or invalid credentials.",
                correlationId));
            return;
        }

        context.HttpContext.Items["Server"] = server;
        context.HttpContext.Items["CorrelationId"] = correlationId;
        LogApiKeyAuthenticated(_logger, context.HttpContext.Request.Path, server.Id);
        var executed = await next().ConfigureAwait(false);
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, executed.Exception);
            FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return;
        }

        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed);
    }

    private async Task<RegisteredServer?> AuthenticateAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var key) && !string.IsNullOrWhiteSpace(key))
        {
            var apiKey = key.ToString();
            var cacheKey = $"apikey:{apiKey}";

            if (!_cache.TryGetValue(cacheKey, out RegisteredServer? server))
            {
                LogApiKeyCacheMiss(_logger, context.Request.Path);
                server = await _db.Servers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.ApiKey == apiKey)
                    .ConfigureAwait(false);
                if (server is not null)
                    _cache.Set(cacheKey, server, CacheTtl);
            }
            else if (server is not null)
            {
                LogApiKeyCacheHit(_logger, context.Request.Path, server.Id);
            }

            return server;
        }

        LogMissingApiKey(_logger, context.Request.Path);
        return await _sessions.AuthenticateCookieAsync(context.Request).ConfigureAwait(false);
    }
}

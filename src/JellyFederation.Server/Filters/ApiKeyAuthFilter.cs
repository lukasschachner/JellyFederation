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

    public ApiKeyAuthFilter(FederationDbContext db,
        IMemoryCache cache,
        ErrorContractMapper errorMapper,
        ILogger<ApiKeyAuthFilter> logger)
    {
        _db = db;
        _cache = cache;
        _errorMapper = errorMapper;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var startedAt = Stopwatch.StartNew();
        var headers = context.HttpContext.Request.Headers
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => (string?)h.Value.ToString());
        var correlationId = TraceContextPropagation.ExtractCorrelationId(headers);
        context.HttpContext.Items["CorrelationId"] = correlationId;

        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanServerHttpRequest);
        FederationTelemetry.SetCommonTags(activity, "api.auth", "server", correlationId, releaseVersion: "server");

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var key))
        {
            LogMissingApiKey(_logger, context.HttpContext.Request.Path);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            context.Result = ErrorContractMapper.ToActionResult(FailureDescriptor.Authorization(
                "api.auth.missing_api_key",
                "Missing API key.",
                correlationId));
            return;
        }

        var apiKey = key.ToString();
        var cacheKey = $"apikey:{apiKey}";

        if (!_cache.TryGetValue(cacheKey, out RegisteredServer? server))
        {
            LogApiKeyCacheMiss(_logger, context.HttpContext.Request.Path);
            server = await _db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey).ConfigureAwait(false);
            if (server is not null)
                _cache.Set(cacheKey, server, CacheTtl);
        }
        else
        {
            if (server is not null)
                LogApiKeyCacheHit(_logger, context.HttpContext.Request.Path, server.Id);
        }

        if (server is null)
        {
            LogApiKeyRejected(_logger, context.HttpContext.Request.Path);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            context.Result = ErrorContractMapper.ToActionResult(FailureDescriptor.Authorization(
                "api.auth.invalid_api_key",
                "Invalid API key.",
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
}

using JellyFederation.Server.Data;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace JellyFederation.Server.Filters;

public partial class ApiKeyAuthFilter(
    FederationDbContext db,
    IMemoryCache cache,
    ILogger<ApiKeyAuthFilter> logger) : IAsyncActionFilter
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var startedAt = Stopwatch.StartNew();
        var headers = context.HttpContext.Request.Headers
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => (string?)h.Value.ToString());
        var correlationId = TraceContextPropagation.ExtractCorrelationId(headers);
        context.HttpContext.Items["CorrelationId"] = correlationId;

        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanServerHttpRequest,
            ActivityKind.Internal);
        FederationTelemetry.SetCommonTags(activity, "api.auth", "server", correlationId, releaseVersion: "server");

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var key))
        {
            LogMissingApiKey(logger, context.HttpContext.Request.Path);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            context.Result = new UnauthorizedResult();
            return;
        }

        var apiKey = key.ToString();
        var cacheKey = $"apikey:{apiKey}";

        if (!cache.TryGetValue(cacheKey, out RegisteredServer? server))
        {
            LogApiKeyCacheMiss(logger, context.HttpContext.Request.Path);
            server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
            if (server is not null)
                cache.Set(cacheKey, server, CacheTtl);
        }
        else
        {
            if (server is not null)
                LogApiKeyCacheHit(logger, context.HttpContext.Request.Path, server.Id);
        }

        if (server is null)
        {
            LogApiKeyRejected(logger, context.HttpContext.Request.Path);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            context.Result = new UnauthorizedResult();
            return;
        }

        context.HttpContext.Items["Server"] = server;
        context.HttpContext.Items["CorrelationId"] = correlationId;
        LogApiKeyAuthenticated(logger, context.HttpContext.Request.Path, server.Id);
        var executed = await next();
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, executed.Exception);
            FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return;
        }

        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("api.auth", "server", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed);
    }
}

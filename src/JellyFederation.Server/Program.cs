using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using JellyFederation.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Hubs;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

const int SignalRMaximumReceiveMessageSize = 128 * 1024;

builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId |
        ActivityTrackingOptions.SpanId |
        ActivityTrackingOptions.ParentId |
        ActivityTrackingOptions.Tags |
        ActivityTrackingOptions.Baggage;
});

var telemetrySection = builder.Configuration.GetSection("Telemetry");
var telemetryServiceName = telemetrySection.GetValue<string>("ServiceName") ?? "jellyfederation-server";
var telemetryOtlpEndpoint = telemetrySection.GetValue<string>("OtlpEndpoint");
var telemetrySamplingRatio = Math.Clamp(telemetrySection.GetValue<double?>("SamplingRatio") ?? 1.0, 0, 1);
var enableTracing = telemetrySection.GetValue<bool?>("EnableTracing") ?? true;
var enableMetrics = telemetrySection.GetValue<bool?>("EnableMetrics") ?? true;
var enableLogs = telemetrySection.GetValue<bool?>("EnableLogs") ?? true;
var releaseVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
FederationTelemetry.CurrentReleaseVersion = releaseVersion;

builder.Logging.AddOpenTelemetry(logging =>
{
    if (!enableLogs)
        return;

    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(telemetryServiceName, serviceVersion: releaseVersion));

    if (Uri.TryCreate(telemetryOtlpEndpoint, UriKind.Absolute, out var endpoint))
        logging.AddOtlpExporter(options => options.Endpoint = endpoint);
});

var maxRequestBodySizeMb = builder.Configuration.GetValue<long?>("ServerLimits:MaxRequestBodySizeMb") ?? 100;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodySizeMb * 1024L * 1024L;
});

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var correlationId = context.HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                            ?? FederationTelemetry.CreateCorrelationId();

        var details = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (string?)string.Join("; ", kvp.Value!.Errors.Select(e => e.ErrorMessage)));

        return ErrorContractMapper.ToActionResult(new FailureDescriptor(
            "request.validation_failed",
            FailureCategory.Validation,
            "One or more validation errors occurred.",
            correlationId,
            details));
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
var connStr = builder.Configuration.GetConnectionString("Default");

builder.Services.AddDbContext<FederationDbContext>(opt =>
{
    if (dbProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        opt.UseNpgsql(connStr ?? throw new InvalidOperationException(
            "ConnectionStrings:Default is required when Database:Provider is PostgreSQL"),
            x => x.MigrationsAssembly("JellyFederation.Migrations.PostgreSQL"));
    }
    else
        opt.UseSqlite(connStr ?? "Data Source=federation.db",
            x => x.MigrationsAssembly("JellyFederation.Migrations.Sqlite"));

    // Default to no-tracking for all read queries; mutation paths explicitly call .AsTracking().
    opt.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
});

builder.Services.AddSignalR(options =>
{
    // Relay chunks are raw 32 KiB payloads, but the JSON SignalR protocol base64-encodes byte[].
    // Keep the hub receive limit comfortably above the encoded envelope size.
    options.MaximumReceiveMessageSize = SignalRMaximumReceiveMessageSize;
});
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection();
builder.Services.AddScoped<WebSessionService>();
builder.Services.AddSingleton<ServerConnectionTracker>();
builder.Services.AddSingleton<FileRequestNotifier>();
builder.Services.AddSingleton<ErrorContractMapper>();
builder.Services.AddSingleton<SignalRErrorMapper>();
builder.Services.AddScoped<ApiKeyAuthFilter>();
builder.Services.AddHostedService<StaleRequestCleanupService>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(telemetryServiceName, serviceVersion: releaseVersion))
    .WithTracing(tracing =>
    {
        if (!enableTracing)
            return;

        tracing
            .AddSource(FederationTelemetry.ActivitySourceServerName, FederationTelemetry.ActivitySourcePluginName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(telemetrySamplingRatio)));

        if (Uri.TryCreate(telemetryOtlpEndpoint, UriKind.Absolute, out var endpoint))
            tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
    })
    .WithMetrics(metrics =>
    {
        if (!enableMetrics)
            return;

        metrics
            .AddMeter(FederationMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (Uri.TryCreate(telemetryOtlpEndpoint, UriKind.Absolute, out var endpoint))
            metrics.AddOtlpExporter(options => options.Endpoint = endpoint);
    });

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    var knownProxies = builder.Configuration
        .GetSection("ForwardedHeaders:KnownProxies")
        .Get<string[]>() ?? [];

    if (knownProxies.Length > 0)
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var proxy in knownProxies)
            if (IPAddress.TryParse(proxy, out var proxyIp))
                options.KnownProxies.Add(proxyIp);
    }
    else if (builder.Environment.IsDevelopment())
    {
        // Development convenience for local reverse proxies.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

builder.Services.AddRateLimiter(options =>
{
    options.AddSlidingWindowLimiter("register", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
    });
});

// CORS: allow the frontend dev server and any configured origin.
// In production, set AllowedOrigins in appsettings.json.
var allowedOrigins = builder.Configuration
                         .GetSection("AllowedOrigins")
                         .Get<string[]>()
                     ?? ["http://localhost:5173", "http://localhost:4173"];

builder.Services.AddCors(opt => opt.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // required for SignalR

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    var activity = Activity.Current;
    if (activity is not null)
    {
        activity.SetTag(FederationTelemetry.TagComponent, "server");
        activity.SetTag(FederationTelemetry.TagReleaseVersion, releaseVersion);
        activity.SetTag(FederationTelemetry.TagTaxonomyVersion, FederationTelemetry.TaxonomyVersion);
    }

    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                        ?? FederationTelemetry.CreateCorrelationId();

    using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["trace_id"] = activity?.TraceId.ToString(),
        ["span_id"] = activity?.SpanId.ToString(),
        ["correlation_id"] = correlationId,
        ["component"] = "server",
        ["release"] = releaseVersion
    });

    await next().ConfigureAwait(false);
});
// HTTPS is terminated by Traefik — no redirect needed inside the container
app.UseRateLimiter();
app.UseStaticFiles(); // serve JS/CSS/assets before routing touches the request
app.UseCors(); // must come before MapControllers / MapHub
app.MapControllers();
app.MapHub<FederationHub>("/hubs/federation");
app.MapFallbackToFile("index.html"); // SPA fallback for client-side routes

app.Run();

public partial class Program;

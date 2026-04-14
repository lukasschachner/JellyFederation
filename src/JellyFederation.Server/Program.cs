using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;
using JellyFederation.Server.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Hubs;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<FederationDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")
                  ?? "Data Source=federation.db"));

builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
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
    // Trust any upstream proxy — the container is not directly internet-exposed (sits behind Traefik)
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
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

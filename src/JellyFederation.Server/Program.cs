using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using JellyFederation.Data;
using JellyFederation.Server.Auth;
using JellyFederation.Server.Hubs;
using JellyFederation.Server.Options;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

const int SignalRMaximumReceiveMessageSize = 128 * 1024;
const string OpenApiRoutePattern = "/openapi/{documentName}.json";
const string ScalarEndpointPrefix = "/docs";
const string ScalarTitle = "JellyFederation API Reference";

var telemetrySection = builder.Configuration.GetSection("Telemetry");
var telemetryServiceName = telemetrySection.GetValue<string>("ServiceName") ?? "jellyfederation-server";
var telemetryOtlpEndpoint = telemetrySection.GetValue<string>("OtlpEndpoint");
var telemetrySamplingRatio = Math.Clamp(telemetrySection.GetValue<double?>("SamplingRatio") ?? 1.0, 0, 1);
var enableTracing = telemetrySection.GetValue<bool?>("EnableTracing") ?? true;
var enableMetrics = telemetrySection.GetValue<bool?>("EnableMetrics") ?? true;
var enableLogs = telemetrySection.GetValue<bool?>("EnableLogs") ?? true;
var releaseVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
FederationTelemetry.CurrentReleaseVersion = releaseVersion;

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("component", "server")
        .Enrich.WithProperty("release", releaseVersion)
        .WriteTo.Console();

    if (enableLogs && Uri.TryCreate(telemetryOtlpEndpoint, UriKind.Absolute, out var endpoint))
        loggerConfiguration.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = endpoint.ToString();
            options.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = telemetryServiceName,
                ["service.version"] = releaseVersion
            };
            options.IncludedData =
                IncludedData.MessageTemplateTextAttribute |
                IncludedData.TraceIdField |
                IncludedData.SpanIdField |
                IncludedData.SpecRequiredResourceAttributes;
        });
});

var maxRequestBodySizeMb = builder.Configuration.GetValue<long?>("ServerLimits:MaxRequestBodySizeMb") ?? 10;
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
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;

    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = "X-Api-Key",
            In = ParameterLocation.Header,
            Description = "Federation API key."
        };

        document.Components.SecuritySchemes["WebSessionCookie"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = WebSessionService.CookieName,
            In = ParameterLocation.Cookie,
            Description = "Web session cookie for browser clients."
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var allowsAnonymous = endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (allowsAnonymous)
            return Task.CompletedTask;

        var requiresAuthorization = endpointMetadata.OfType<IAuthorizeData>().Any();
        if (!requiresAuthorization)
            return Task.CompletedTask;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("ApiKey", context.Document, null)] = []
        });
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("WebSessionCookie", context.Document, null)] = []
        });

        return Task.CompletedTask;
    });
});

builder.Services.AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.AdminToken))
            options.AdminToken = builder.Configuration["AdminToken"] ?? string.Empty;
    })
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();

builder.Services.AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName))
    .PostConfigure(options =>
    {
        if (options.AllowedOrigins.Length == 0)
            options.AllowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
    })
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<CorsOptions>, CorsOptionsValidator>();

builder.Services.AddOptions<WebSessionOptions>()
    .Bind(builder.Configuration.GetSection(WebSessionOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<WebSessionOptions>, WebSessionOptionsValidator>();

builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

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
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = FederationAuthSchemes.ApiKeyOrSession;
        options.DefaultChallengeScheme = FederationAuthSchemes.ApiKeyOrSession;
        options.DefaultForbidScheme = FederationAuthSchemes.ApiKeyOrSession;
    })
    .AddScheme<AuthenticationSchemeOptions, FederationApiKeyAuthenticationHandler>(
        FederationAuthSchemes.ApiKeyOrSession,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddFederationWorkflowServices();
builder.Services.AddScoped<WebSessionService>();
builder.Services.AddSingleton<ServerConnectionTracker>();
builder.Services.AddSingleton<FileRequestNotifier>();
builder.Services.AddSingleton<ErrorContractMapper>();
builder.Services.AddSingleton<SignalRErrorMapper>();
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
// In production, set Cors:AllowedOrigins in appsettings.json or environment variables.
var allowedOrigins = builder.Configuration
                         .GetSection(CorsOptions.SectionName)
                         .Get<CorsOptions>()
                         ?.AllowedOrigins is { Length: > 0 } configuredOrigins
    ? configuredOrigins
    : builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
      ?? ["http://localhost:5173", "http://localhost:4173"];

builder.Services.AddCors(opt => opt.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // required for SignalR

var app = builder.Build();

var autoMigrate = builder.Configuration.GetValue<bool?>("Database:AutoMigrate")
                  ?? !app.Environment.IsProduction();
if (autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
    db.Database.Migrate();
}
else
{
    app.Logger.LogInformation(
        "Database auto-migration is disabled. Apply migrations with the provider-specific migration job or dotnet ef before starting the production web app.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(OpenApiRoutePattern);
    app.MapScalarApiReference(ScalarEndpointPrefix, options =>
    {
        options.ShowDeveloperTools = DeveloperToolsVisibility.Never;
        options.WithTitle(ScalarTitle);
        options.WithTheme(ScalarTheme.Default);
        options.WithOpenApiRoutePattern(OpenApiRoutePattern);
        options.AddPreferredSecuritySchemes(["ApiKey"]);
    });
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

app.UseRateLimiter();
app.UseStaticFiles(); // serve JS/CSS/assets before routing touches the request
app.UseCors(); // must come before auth and endpoint mapping
app.UseAuthentication();
app.UseAuthorization();
app.MapGroup("/api").MapControllers();
app.MapHub<FederationHub>("/hubs/federation");
app.MapFallbackToFile("index.html"); // SPA fallback for client-side routes

app.Run();

public partial class Program;

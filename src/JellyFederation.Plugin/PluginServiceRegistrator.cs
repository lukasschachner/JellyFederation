using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using JellyFederation.Shared.Telemetry;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace JellyFederation.Plugin;

/// <summary>
/// Registers all plugin services with Jellyfin's DI container.
/// Jellyfin discovers this class automatically via reflection.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        // Configuration provider — delegates to the plugin singleton but accessed via DI
        services.AddSingleton<IPluginConfigurationProvider>(
            _ => FederationPlugin.Instance
                 ?? throw new InvalidOperationException("FederationPlugin has not been instantiated yet."));

        var telemetryConfig = FederationPlugin.Instance?.Configuration ?? new PluginConfiguration();
        var serviceName = string.IsNullOrWhiteSpace(telemetryConfig.TelemetryServiceName)
            ? "jellyfederation-plugin"
            : telemetryConfig.TelemetryServiceName;
        var samplingRatio = Math.Clamp(telemetryConfig.TelemetrySamplingRatio, 0, 1);

        services.AddLogging(logging =>
        {
            if (!telemetryConfig.EnableLogs)
                return;

            logging.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName, serviceVersion: FederationPlugin.ReleaseVersion));

                if (Uri.TryCreate(telemetryConfig.TelemetryOtlpEndpoint, UriKind.Absolute, out var endpoint))
                    options.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = endpoint;
                    });
            });
        });

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: FederationPlugin.ReleaseVersion))
            .WithTracing(tracing =>
            {
                if (!telemetryConfig.EnableTracing)
                    return;

                tracing
                    .AddSource(FederationTelemetry.ActivitySourcePluginName, FederationTelemetry.ActivitySourceServerName)
                    .AddHttpClientInstrumentation()
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)));

                if (Uri.TryCreate(telemetryConfig.TelemetryOtlpEndpoint, UriKind.Absolute, out var endpoint))
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = endpoint;
                    });
            })
            .WithMetrics(metrics =>
            {
                if (!telemetryConfig.EnableMetrics)
                    return;

                metrics
                    .AddMeter(FederationMetrics.MeterName)
                    .AddHttpClientInstrumentation();

                if (Uri.TryCreate(telemetryConfig.TelemetryOtlpEndpoint, UriKind.Absolute, out var endpoint))
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = endpoint;
                    });
            });

        services.AddHttpClient<LibrarySyncService>();
        services.AddSingleton<HolePunchService>();
        // Use AddHttpClient so the DI container manages HttpClient lifetime via IHttpClientFactory,
        // avoiding socket exhaustion from a long-lived singleton HttpClient.
        services.AddHttpClient<FileTransferService>();
        services.AddSingleton<FederationSignalRService>();
        services.AddHostedService<TelemetryBootstrapService>();
        services.AddHostedService<FederationStartupService>();
    }
}

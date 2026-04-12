using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using JellyFederation.Plugin.Configuration;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Forces OpenTelemetry providers to be resolved in Jellyfin's plugin host.
/// Some host setups don't instantiate providers until first resolution.
/// </summary>
public sealed class TelemetryBootstrapService(
    IServiceProvider serviceProvider,
    IPluginConfigurationProvider configProvider,
    ILogger<TelemetryBootstrapService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var tracerProvider = serviceProvider.GetService<TracerProvider>();
        var meterProvider = serviceProvider.GetService<MeterProvider>();
        var config = configProvider.GetConfiguration();
        logger.LogInformation(
            "Telemetry bootstrap resolved providers: tracer={TracerResolved}, meter={MeterResolved}, endpoint={Endpoint}, tracing={TracingEnabled}, metrics={MetricsEnabled}, logs={LogsEnabled}",
            tracerProvider is not null,
            meterProvider is not null,
            config.TelemetryOtlpEndpoint,
            config.EnableTracing,
            config.EnableMetrics,
            config.EnableLogs);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

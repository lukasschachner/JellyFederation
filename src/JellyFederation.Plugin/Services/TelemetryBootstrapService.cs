using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using JellyFederation.Plugin.Configuration;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Logs telemetry configuration at startup.
/// </summary>
public sealed class TelemetryBootstrapService(
    IPluginConfigurationProvider configProvider,
    ILogger<TelemetryBootstrapService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = configProvider.GetConfiguration();
        logger.LogInformation(
            "Telemetry configuration loaded: endpoint={Endpoint}, tracing={TracingEnabled}, metrics={MetricsEnabled}, logs={LogsEnabled}",
            config.TelemetryOtlpEndpoint,
            config.EnableTracing,
            config.EnableMetrics,
            config.EnableLogs);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

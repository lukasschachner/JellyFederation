using JellyFederation.Plugin.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
///     Logs telemetry configuration at startup.
///     The plugin intentionally does not wire OpenTelemetry exporters because Jellyfin hosts
///     plugins in-process and package version mismatches can prevent plugin startup.
/// </summary>
public sealed class TelemetryBootstrapService : IHostedService
{
    private readonly IPluginConfigurationProvider _configProvider;
    private readonly ILogger<TelemetryBootstrapService> _logger;

    /// <summary>
    ///     Logs telemetry configuration at startup.
    /// </summary>
    public TelemetryBootstrapService(IPluginConfigurationProvider configProvider,
        ILogger<TelemetryBootstrapService> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configProvider.GetConfiguration();
        _logger.LogInformation(
            "Telemetry configuration loaded: endpoint={Endpoint}, tracing={TracingEnabled}, metrics={MetricsEnabled}, logs={LogsEnabled}",
            config.TelemetryOtlpEndpoint,
            config.EnableTracing,
            config.EnableMetrics,
            config.EnableLogs);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
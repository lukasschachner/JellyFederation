using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Telemetry;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Hosted service that runs at Jellyfin startup:
/// connects SignalR to the federation server and performs the initial library sync.
/// Also re-syncs whenever the Jellyfin library changes.
/// Retries the SignalR connection in the background if the federation server is not
/// reachable at startup time.
/// </summary>
public partial class FederationStartupService(
    LibrarySyncService librarySync,
    FederationSignalRService signalR,
    ILibraryManager libraryManager,
    IPluginConfigurationProvider configProvider,
    ILogger<FederationStartupService> logger) : IHostedService
{
    private CancellationTokenSource? _retryCts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        libraryManager.ItemAdded += OnLibraryChanged;
        libraryManager.ItemRemoved += OnLibraryChanged;
        libraryManager.ItemUpdated += OnLibraryChanged;

        _retryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ConnectWithRetryAsync(_retryCts.Token), _retryCts.Token);
        return Task.CompletedTask;
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        // Retry delays: 5s, 10s, 20s, 40s, then 60s forever
        int[] delaysMs = [5_000, 10_000, 20_000, 40_000];
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            var config = configProvider.GetConfiguration();
            var correlationId = FederationTelemetry.CreateCorrelationId();
            using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
                FederationTelemetry.SpanFederationOperation,
                ActivityKind.Internal);
            FederationTelemetry.SetCommonTags(
                activity,
                "plugin.startup.connect",
                "plugin",
                correlationId,
                releaseVersion: FederationPlugin.ReleaseVersion);
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["trace_id"] = activity?.TraceId.ToString(),
                ["span_id"] = activity?.SpanId.ToString(),
                ["correlation_id"] = correlationId,
                ["operation"] = "plugin.startup.connect",
                ["component"] = "plugin"
            });

            try
            {
                await signalR.StartAsync(config, ct);
                if (!signalR.IsConnected)
                {
                    LogNotConfigured(logger);
                    FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
                    return;
                }

                await librarySync.SyncAsync(config, ct);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
                LogConnectedSuccessfully(logger,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString(),
                    correlationId);
                break; // connected — exit the retry loop
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
                return;
            }
            catch (Exception ex)
            {
                var delayMs = attempt < delaysMs.Length ? delaysMs[attempt] : 60_000;
                attempt++;
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, ex);
                LogConnectionFailed(logger, ex, attempt, delayMs / 1000,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString(),
                    correlationId);
                try { await Task.Delay(delayMs, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _retryCts?.Cancel();
        libraryManager.ItemAdded -= OnLibraryChanged;
        libraryManager.ItemRemoved -= OnLibraryChanged;
        libraryManager.ItemUpdated -= OnLibraryChanged;
        return Task.CompletedTask;
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var config = configProvider.GetConfiguration();
                if (!signalR.IsConnected)
                {
                    LogSkippingResync(logger);
                    return;
                }
                await librarySync.SyncAsync(config);
            }
            catch (Exception ex)
            {
                LogResyncFailed(logger, ex);
            }
        });
    }
}

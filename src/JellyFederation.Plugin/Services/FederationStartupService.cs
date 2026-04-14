using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Telemetry;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
///     Hosted service that runs at Jellyfin startup:
///     connects SignalR to the federation server and performs the initial library sync.
///     Also re-syncs whenever the Jellyfin library changes.
///     Retries the SignalR connection in the background if the federation server is not
///     reachable at startup time.
/// </summary>
public partial class FederationStartupService : IHostedService
{
    private readonly IPluginConfigurationProvider _configProvider;
    private readonly ILibraryManager _libraryManager;
    private readonly LibrarySyncService _librarySync;
    private readonly ILogger<FederationStartupService> _logger;
    private readonly FederationSignalRService _signalR;
    private CancellationTokenSource? _retryCts;

    /// <summary>
    ///     Hosted service that runs at Jellyfin startup:
    ///     connects SignalR to the federation server and performs the initial library sync.
    ///     Also re-syncs whenever the Jellyfin library changes.
    ///     Retries the SignalR connection in the background if the federation server is not
    ///     reachable at startup time.
    /// </summary>
    public FederationStartupService(LibrarySyncService librarySync,
        FederationSignalRService signalR,
        ILibraryManager libraryManager,
        IPluginConfigurationProvider configProvider,
        ILogger<FederationStartupService> logger)
    {
        _librarySync = librarySync;
        _signalR = signalR;
        _libraryManager = libraryManager;
        _configProvider = configProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;
        _libraryManager.ItemUpdated += OnLibraryChanged;

        _retryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ConnectWithRetryAsync(_retryCts.Token), _retryCts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _retryCts?.Cancel();
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;
        _libraryManager.ItemUpdated -= OnLibraryChanged;
        return Task.CompletedTask;
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        // Composition example for future workflows:
        // var result = await _librarySync.SyncAsync(config, ct);
        // if (result.IsFailure) { ...standardized failure handling... }
        // else { ...continue startup pipeline... }
        // Retry delays: 5s, 10s, 20s, 40s, then 60s forever
        int[] delaysMs = [5_000, 10_000, 20_000, 40_000];
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            var config = _configProvider.GetConfiguration();
            var correlationId = FederationTelemetry.CreateCorrelationId();
            using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
                FederationTelemetry.SpanFederationOperation);
            FederationTelemetry.SetCommonTags(
                activity,
                "plugin.startup.connect",
                "plugin",
                correlationId,
                releaseVersion: FederationPlugin.ReleaseVersion);
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["trace_id"] = activity?.TraceId.ToString(),
                ["span_id"] = activity?.SpanId.ToString(),
                ["correlation_id"] = correlationId,
                ["operation"] = "plugin.startup.connect",
                ["component"] = "plugin"
            });

            try
            {
                await _signalR.StartAsync(config, ct).ConfigureAwait(false);
                if (!_signalR.IsConnected)
                {
                    LogNotConfigured(_logger);
                    FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
                    return;
                }

                var startupSyncOutcome = await _librarySync.SyncAsync(config, ct).ConfigureAwait(false);
                if (startupSyncOutcome.IsFailure && startupSyncOutcome.Failure is not null)
                {
                    FederationTelemetry.SetFailure(activity, startupSyncOutcome.Failure);
                    LogStartupSyncFailed(
                        _logger,
                        startupSyncOutcome.Failure.Code,
                        startupSyncOutcome.Failure.Category.ToString(),
                        startupSyncOutcome.Failure.Message);
                }
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
                LogConnectedSuccessfully(_logger,
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
                LogConnectionFailed(_logger, ex, attempt, delayMs / 1000,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString(),
                    correlationId);
                try
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var config = _configProvider.GetConfiguration();
                if (!_signalR.IsConnected)
                {
                    LogSkippingResync(_logger);
                    return;
                }

                var syncOutcome = await _librarySync.SyncAsync(config).ConfigureAwait(false);
                if (syncOutcome.IsFailure && syncOutcome.Failure is not null)
                    LogResyncFailureDescriptor(
                        _logger,
                        syncOutcome.Failure.Code,
                        syncOutcome.Failure.Category.ToString(),
                        syncOutcome.Failure.Message);
            }
            catch (Exception ex)
            {
                LogResyncFailed(_logger, ex);
            }
        });
    }
}

using System.Diagnostics;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
///     Maintains the persistent SignalR connection to the federation server.
///     Dispatches incoming messages to the appropriate local services.
/// </summary>
public partial class FederationSignalRService : IAsyncDisposable
{
    private readonly IPluginConfigurationProvider _configProvider;
    private readonly HolePunchService _holePunch;
    private readonly LibrarySyncService _librarySync;
    private readonly ILogger<FederationSignalRService> _logger;
    private HubConnection? _connection;

    /// <summary>
    ///     Maintains the persistent SignalR connection to the federation server.
    ///     Dispatches incoming messages to the appropriate local services.
    /// </summary>
    public FederationSignalRService(ILogger<FederationSignalRService> logger,
        HolePunchService holePunch,
        LibrarySyncService librarySync,
        IPluginConfigurationProvider configProvider)
    {
        _logger = logger;
        _holePunch = holePunch;
        _librarySync = librarySync;
        _configProvider = configProvider;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
    }

    public async Task StartAsync(PluginConfiguration config, CancellationToken ct = default)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Client);
        FederationTelemetry.SetCommonTags(activity, "signalr.connect", "plugin", correlationId,
            releaseVersion: FederationPlugin.ReleaseVersion);

        if (string.IsNullOrEmpty(config.FederationServerUrl) ||
            string.IsNullOrEmpty(config.ApiKey))
        {
            LogNotConfigured(_logger);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl($"{config.FederationServerUrl.TrimEnd('/')}/hubs/federation?apiKey={config.ApiKey}&client=plugin")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<HolePunchRequest>("HolePunchRequest", async req =>
        {
            LogHolePunchRequestReceived(_logger, req.FileRequestId);
            try
            {
                await _holePunch.ExecuteAsync(req, _connection).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogExecuteAsyncFailed(_logger, ex, req.FileRequestId);
            }
        });

        _connection.On<FileRequestNotification>("FileRequestNotification", async notification =>
        {
            LogIncomingFileRequest(_logger, notification.FileRequestId, notification.JellyfinItemId,
                notification.RequestingServerId);
            try
            {
                await _holePunch.PrepareAndSignalReadyAsync(notification, _connection).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPrepareAndSignalReadyFailed(_logger, ex, notification.FileRequestId);
            }
        });

        _connection.On<FileRequestStatusUpdate>("FileRequestStatusUpdate", update =>
        {
            LogFileRequestStatus(
                _logger,
                update.FileRequestId,
                update.Status,
                update.FailureReason ?? string.Empty,
                update.SelectedTransportMode?.ToString() ?? "n/a",
                update.FailureCategory?.ToString() ?? "n/a",
                update.Failure?.Code ?? "n/a");
        });

        _connection.On<CancelTransfer>("CancelTransfer", msg =>
        {
            LogCancellingTransfer(_logger, msg.FileRequestId);
            _holePunch.Cancel(msg.FileRequestId);
        });

        _connection.Reconnecting += _ =>
        {
            LogReconnecting(_logger);
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            LogReconnected(_logger);
            try
            {
                var cfg = _configProvider.GetConfiguration();
                var syncOutcome = await _librarySync.SyncAsync(cfg).ConfigureAwait(false);
                if (syncOutcome.IsFailure && syncOutcome.Failure is not null)
                    LogResyncAfterReconnectFailureDescriptor(
                        _logger,
                        syncOutcome.Failure.Code,
                        syncOutcome.Failure.Category.ToString(),
                        syncOutcome.Failure.Message);
            }
            catch (Exception ex)
            {
                LogResyncAfterReconnectFailed(_logger, ex);
            }
        };

        await _connection.StartAsync(ct).ConfigureAwait(false);
        LogConnected(_logger, config.FederationServerUrl);
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("signalr.connect", "plugin", FederationTelemetry.OutcomeSuccess,
            startedAt.Elapsed, FederationPlugin.ReleaseVersion);
    }
}

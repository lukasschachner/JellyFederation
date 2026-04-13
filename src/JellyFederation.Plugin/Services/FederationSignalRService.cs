using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Telemetry;
using JellyFederation.Shared.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Maintains the persistent SignalR connection to the federation server.
/// Dispatches incoming messages to the appropriate local services.
/// </summary>
public partial class FederationSignalRService(
    ILogger<FederationSignalRService> logger,
    HolePunchService holePunch,
    LibrarySyncService librarySync,
    IPluginConfigurationProvider configProvider) : IAsyncDisposable
{
    private HubConnection? _connection;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task StartAsync(PluginConfiguration config, CancellationToken ct = default)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Client);
        FederationTelemetry.SetCommonTags(activity, "signalr.connect", "plugin", correlationId, releaseVersion: FederationPlugin.ReleaseVersion);

        if (string.IsNullOrEmpty(config.FederationServerUrl) ||
            string.IsNullOrEmpty(config.ApiKey))
        {
            LogNotConfigured(logger);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl($"{config.FederationServerUrl.TrimEnd('/')}/hubs/federation?apiKey={config.ApiKey}&client=plugin")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<HolePunchRequest>("HolePunchRequest", async req =>
        {
            LogHolePunchRequestReceived(logger, req.FileRequestId);
            try { await holePunch.ExecuteAsync(req, _connection); }
            catch (Exception ex) { LogExecuteAsyncFailed(logger, ex, req.FileRequestId); }
        });

        _connection.On<FileRequestNotification>("FileRequestNotification", async notification =>
        {
            LogIncomingFileRequest(logger, notification.FileRequestId, notification.JellyfinItemId, notification.RequestingServerId);
            try { await holePunch.PrepareAndSignalReadyAsync(notification, _connection); }
            catch (Exception ex) { LogPrepareAndSignalReadyFailed(logger, ex, notification.FileRequestId); }
        });

        _connection.On<FileRequestStatusUpdate>("FileRequestStatusUpdate", update =>
        {
            LogFileRequestStatus(
                logger,
                update.FileRequestId,
                update.Status,
                update.FailureReason ?? string.Empty,
                update.SelectedTransportMode?.ToString() ?? "n/a",
                update.FailureCategory?.ToString() ?? "n/a");
        });

        _connection.On<CancelTransfer>("CancelTransfer", msg =>
        {
            LogCancellingTransfer(logger, msg.FileRequestId);
            holePunch.Cancel(msg.FileRequestId);
        });

        _connection.Reconnecting += _ =>
        {
            LogReconnecting(logger);
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            LogReconnected(logger);
            try
            {
                var cfg = configProvider.GetConfiguration();
                await librarySync.SyncAsync(cfg);
            }
            catch (Exception ex) { LogResyncAfterReconnectFailed(logger, ex); }
        };

        await _connection.StartAsync(ct);
        LogConnected(logger, config.FederationServerUrl);
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("signalr.connect", "plugin", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}

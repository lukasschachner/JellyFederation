using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Diagnostics;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
///     Maintains the persistent SignalR connection to the federation server.
///     Dispatches incoming messages to the appropriate local services.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "SignalR handler wiring requires a live HubConnection and is verified through integration workflow tests.")]
public partial class FederationSignalRService : IAsyncDisposable
{
    private readonly IPluginConfigurationProvider _configProvider;
    private readonly HolePunchService _holePunch;
    private readonly WebRtcTransportService _webRtc;
    private readonly LibrarySyncService _librarySync;
    private readonly ILogger<FederationSignalRService> _logger;
    private HubConnection? _connection;

    /// <summary>
    ///     Maintains the persistent SignalR connection to the federation server.
    ///     Dispatches incoming messages to the appropriate local services.
    /// </summary>
    public FederationSignalRService(ILogger<FederationSignalRService> logger,
        HolePunchService holePunch,
        WebRtcTransportService webRtc,
        LibrarySyncService librarySync,
        IPluginConfigurationProvider configProvider)
    {
        _logger = logger;
        _holePunch = holePunch;
        _webRtc = webRtc;
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
            .WithUrl($"{config.FederationServerUrl.TrimEnd('/')}/hubs/federation?client=plugin", options =>
            {
                options.Headers.Add("X-Api-Key", config.ApiKey);
                options.AccessTokenProvider = () => Task.FromResult<string?>(config.ApiKey);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<HolePunchRequest>("HolePunchRequest", async req =>
        {
            using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
                req.FileRequestId,
                transportMode: req.SelectedTransportMode.ToString()));

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
            using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
                notification.FileRequestId,
                peerServerId: notification.RequestingServerId));

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
            using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
                update.FileRequestId,
                transportMode: update.SelectedTransportMode?.ToString()));

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
            using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(msg.FileRequestId));

            LogCancellingTransfer(_logger, msg.FileRequestId);
            _holePunch.Cancel(msg.FileRequestId);
            _webRtc.Cancel(msg.FileRequestId);
        });

        _connection.On<IceNegotiateStart>("IceNegotiateStart", msg =>
        {
            using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
                msg.FileRequestId,
                role: msg.Role.ToString()));

            LogIceNegotiateStart(_logger, msg.FileRequestId, msg.Role.ToString());
            _ = Task.Run(async () =>
            {
                var cfg = _configProvider.GetConfiguration();
                try
                {
                    if (msg.Role == IceRole.Offerer)
                    {
                        // Sender side: we need the jellyfinItemId — retrieve it from the pending sockets dict
                        // via HolePunchService (which stored it during PrepareAndSignalReadyAsync).
                        // Fall back to empty string if not found; BeginAsOffererAsync will log the resolution error.
                        var jellyfinItemId = _holePunch.GetPendingJellyfinItemId(msg.FileRequestId) ?? string.Empty;
                        await _webRtc.BeginAsOffererAsync(msg.FileRequestId, jellyfinItemId, _connection!, cfg)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await _webRtc.BeginAsAnswererAsync(msg.FileRequestId, _connection!, cfg)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogIceNegotiateStartFailed(_logger, ex, msg.FileRequestId);
                }
            });
        });

        _connection.On<IceSignal>("IceSignal", msg =>
        {
            using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
                msg.FileRequestId,
                signalType: msg.Type.ToString(),
                transportMode: "WebRtc"));

            LogIceSignalReceived(_logger, msg.FileRequestId, msg.Type.ToString());
            _webRtc.HandleIceSignal(msg);
        });

        _connection.On<RelayChunk>("RelayReceiveChunk", msg =>
        {
            using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
                msg.FileRequestId,
                transportMode: "Relay"));

            LogRelayChunkReceived(_logger, msg.FileRequestId, msg.ChunkIndex, msg.IsEof);
            _webRtc.EnqueueRelayChunk(msg);
        });

        _connection.On<RelayTransferStart>("RelayTransferStart", msg =>
        {
            using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
                msg.FileRequestId,
                role: msg.Role.ToString(),
                transportMode: "Relay"));

            LogRelayTransferStartReceived(_logger, msg.FileRequestId);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _webRtc.StartRelayReceiveModeAsync(msg, _connection!).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogRelayTransferStartFailed(_logger, ex, msg.FileRequestId);
                }
            });
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

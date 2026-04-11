using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Maintains the persistent SignalR connection to the federation server.
/// Dispatches incoming messages to the appropriate local services.
/// </summary>
public class FederationSignalRService : IAsyncDisposable
{
    private readonly ILogger<FederationSignalRService> _logger;
    private readonly HolePunchService _holePunch;
    private readonly LibrarySyncService _librarySync;
    private readonly IPluginConfigurationProvider _configProvider;
    private HubConnection? _connection;

    public FederationSignalRService(
        ILogger<FederationSignalRService> logger,
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

    public async Task StartAsync(PluginConfiguration config, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(config.FederationServerUrl) ||
            string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogWarning("Federation server URL or API key not configured — skipping SignalR connection");
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl($"{config.FederationServerUrl.TrimEnd('/')}/hubs/federation?apiKey={config.ApiKey}&client=plugin")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<HolePunchRequest>("HolePunchRequest", async req =>
        {
            _logger.LogInformation("Hole punch request received for file request {Id}", req.FileRequestId);
            try { await _holePunch.ExecuteAsync(req, _connection); }
            catch (Exception ex) { _logger.LogError(ex, "ExecuteAsync failed for request {Id}", req.FileRequestId); }
        });

        _connection.On<FileRequestNotification>("FileRequestNotification", async notification =>
        {
            _logger.LogInformation(
                "Incoming file request {Id} for item {ItemId} from server {From}",
                notification.FileRequestId, notification.JellyfinItemId, notification.RequestingServerId);
            try { await _holePunch.PrepareAndSignalReadyAsync(notification, _connection); }
            catch (Exception ex) { _logger.LogError(ex, "PrepareAndSignalReadyAsync failed for request {Id}", notification.FileRequestId); }
        });

        _connection.On<FileRequestStatusUpdate>("FileRequestStatusUpdate", update =>
        {
            _logger.LogInformation(
                "File request {Id} status: {Status} {Reason}",
                update.FileRequestId, update.Status, update.FailureReason ?? string.Empty);
        });

        _connection.On<CancelTransfer>("CancelTransfer", msg =>
        {
            _logger.LogInformation("Cancelling transfer for request {Id}", msg.FileRequestId);
            _holePunch.Cancel(msg.FileRequestId);
        });

        _connection.Reconnecting += _ =>
        {
            _logger.LogWarning("SignalR reconnecting...");
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            _logger.LogInformation("SignalR reconnected — re-syncing library");
            try
            {
                var cfg = _configProvider.GetConfiguration();
                await _librarySync.SyncAsync(cfg);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Library re-sync after reconnect failed"); }
        };

        await _connection.StartAsync(ct);
        _logger.LogInformation("Connected to federation server at {Url}", config.FederationServerUrl);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}

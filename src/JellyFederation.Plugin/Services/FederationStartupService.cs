using JellyFederation.Plugin.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Hosted service that runs at Jellyfin startup:
/// connects SignalR to the federation server and performs the initial library sync.
/// Also re-syncs whenever the Jellyfin library changes.
/// Retries the SignalR connection in the background if the federation server is not
/// reachable at startup time.
/// </summary>
public class FederationStartupService(
    LibrarySyncService librarySync,
    FederationSignalRService signalR,
    ILibraryManager libraryManager,
    IPluginConfigurationProvider configProvider,
    ILogger<FederationStartupService> logger) : IHostedService
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
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
            if (signalR.IsConnected)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                continue;
            }

            var config = configProvider.GetConfiguration();

            try
            {
                await signalR.StartAsync(config, ct);
                await librarySync.SyncAsync(config, ct);
                attempt = 0; // reset backoff on success
                logger.LogInformation("Federation plugin connected successfully");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var delayMs = attempt < delaysMs.Length ? delaysMs[attempt] : 60_000;
                attempt++;
                logger.LogWarning(ex,
                    "Failed to connect to federation server (attempt {Attempt}). Retrying in {Delay}s",
                    attempt, delayMs / 1000);
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
            if (!await _syncLock.WaitAsync(0)) return;
            try
            {
                var config = configProvider.GetConfiguration();
                await librarySync.SyncAsync(config);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Library re-sync failed");
            }
            finally
            {
                _syncLock.Release();
            }
        });
    }
}

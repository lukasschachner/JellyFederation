using JellyFederation.Server.Data;
using JellyFederation.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Services;

/// <summary>
/// Periodically scans for file requests stuck in non-terminal states
/// (Pending, HolePunching, Transferring) and fails them after a timeout.
/// This handles cases where a plugin crashes mid-transfer and never reports back.
/// </summary>
public partial class StaleRequestCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<StaleRequestCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogCleanupServiceStarted(logger, CheckInterval.TotalMinutes, StaleThreshold.TotalHours);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupStaleRequestsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCleanupError(logger, ex);
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        LogCleanupServiceStopped(logger);
    }

    private async Task CleanupStaleRequestsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<FileRequestNotifier>();

        var cutoff = DateTime.UtcNow - StaleThreshold;

        var stale = await db.FileRequests
            .Where(r =>
                (r.Status == FileRequestStatus.Pending ||
                 r.Status == FileRequestStatus.HolePunching ||
                 r.Status == FileRequestStatus.Transferring) &&
                r.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            LogNoStaleRequests(logger);
            return;
        }

        LogCleaningUpStale(logger, stale.Count, StaleThreshold.TotalHours);

        foreach (var req in stale)
        {
            var previousStatus = req.Status;
            req.Status = FileRequestStatus.Failed;
            req.FailureReason =
                $"Request timed out after {StaleThreshold.TotalHours:0} hour(s) in {previousStatus} state. " +
                "The plugin may have crashed or lost connectivity.";
            LogMarkedStaleRequestFailed(logger, req.Id, previousStatus);
        }

        await db.SaveChangesAsync(ct);

        foreach (var req in stale)
        {
            try { await notifier.NotifyStatusAsync(req); }
            catch (Exception ex)
            {
                LogNotifyFailed(logger, ex, req.Id);
            }
        }
    }
}

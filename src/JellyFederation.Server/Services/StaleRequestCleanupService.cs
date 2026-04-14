using JellyFederation.Server.Data;
using JellyFederation.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Services;

/// <summary>
///     Periodically scans for file requests stuck in non-terminal states
///     (Pending, HolePunching, Transferring) and fails them after a timeout.
///     This handles cases where a plugin crashes mid-transfer and never reports back.
/// </summary>
public partial class StaleRequestCleanupService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(1);
    private readonly ILogger<StaleRequestCleanupService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    ///     Periodically scans for file requests stuck in non-terminal states
    ///     (Pending, HolePunching, Transferring) and fails them after a timeout.
    ///     This handles cases where a plugin crashes mid-transfer and never reports back.
    /// </summary>
    public StaleRequestCleanupService(IServiceScopeFactory scopeFactory,
        ILogger<StaleRequestCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogCleanupServiceStarted(_logger, CheckInterval.TotalMinutes, StaleThreshold.TotalHours);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupStaleRequestsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCleanupError(_logger, ex);
            }

            await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);
        }

        LogCleanupServiceStopped(_logger);
    }

    private async Task CleanupStaleRequestsAsync(CancellationToken ct)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        await using var scope1 = scope.ConfigureAwait(false);
        var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<FileRequestNotifier>();

        var cutoff = DateTime.UtcNow - StaleThreshold;

        var stale = await db.FileRequests
            .Where(r =>
                (r.Status == FileRequestStatus.Pending ||
                 r.Status == FileRequestStatus.HolePunching ||
                 r.Status == FileRequestStatus.Transferring) &&
                r.CreatedAt < cutoff)
            .ToListAsync(ct).ConfigureAwait(false);

        if (stale.Count == 0)
        {
            LogNoStaleRequests(_logger);
            return;
        }

        LogCleaningUpStale(_logger, stale.Count, StaleThreshold.TotalHours);

        foreach (var req in stale)
        {
            var previousStatus = req.Status;
            req.Status = FileRequestStatus.Failed;
            req.FailureReason =
                $"Request timed out after {StaleThreshold.TotalHours:0} hour(s) in {previousStatus} state. " +
                "The plugin may have crashed or lost connectivity.";
            LogMarkedStaleRequestFailed(_logger, req.Id, previousStatus);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var req in stale)
            try
            {
                await notifier.NotifyStatusAsync(req).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogNotifyFailed(_logger, ex, req.Id);
            }
    }
}
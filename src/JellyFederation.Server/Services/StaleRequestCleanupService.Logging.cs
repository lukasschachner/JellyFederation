using JellyFederation.Shared.Models;

namespace JellyFederation.Server.Services;

public partial class StaleRequestCleanupService
{
    [LoggerMessage(1, LogLevel.Error, "Unhandled error during stale request cleanup")]
    private static partial void LogCleanupError(ILogger logger, Exception ex);

    [LoggerMessage(2, LogLevel.Warning, "Cleaning up {Count} stale file request(s) older than {Hours}h")]
    private static partial void LogCleaningUpStale(ILogger logger, int count, double hours);

    [LoggerMessage(3, LogLevel.Warning, "Failed to notify parties for stale request {Id}")]
    private static partial void LogNotifyFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(4, LogLevel.Information,
        "Stale request cleanup service started (interval={IntervalMinutes}m, threshold={ThresholdHours}h)")]
    private static partial void LogCleanupServiceStarted(ILogger logger, double intervalMinutes, double thresholdHours);

    [LoggerMessage(5, LogLevel.Information, "Stale request cleanup service stopped")]
    private static partial void LogCleanupServiceStopped(ILogger logger);

    [LoggerMessage(6, LogLevel.Debug, "No stale file requests found")]
    private static partial void LogNoStaleRequests(ILogger logger);

    [LoggerMessage(7, LogLevel.Warning, "Marked stale request {Id} as failed (previous status {PreviousStatus})")]
    private static partial void LogMarkedStaleRequestFailed(ILogger logger, Guid id, FileRequestStatus previousStatus);
}
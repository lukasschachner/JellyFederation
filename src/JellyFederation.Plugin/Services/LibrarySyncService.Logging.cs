using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using JellyFederation.Shared.Telemetry;
using System.Diagnostics;

namespace JellyFederation.Plugin.Services;

public partial class LibrarySyncService
{
    private static void TagSpanOutcome(string outcome, Exception? ex = null) =>
        FederationTelemetry.SetOutcome(Activity.Current, outcome, ex);

    [LoggerMessage(1, LogLevel.Warning, "Library sync skipped: Federation Server URL and API key must be configured.")]
    private static partial void LogSyncSkipped(ILogger logger);

    [LoggerMessage(2, LogLevel.Debug, "Library sync already in progress; queued a follow-up run.")]
    private static partial void LogSyncAlreadyInProgress(ILogger logger);

    [LoggerMessage(3, LogLevel.Information, "Starting library sync to federation server")]
    private static partial void LogStartingSync(ILogger logger);

    [LoggerMessage(4, LogLevel.Information, "Synced {Count} items to federation server")]
    private static partial void LogSyncedItems(ILogger logger, int count);

    [LoggerMessage(5, LogLevel.Information,
        "Preview sync stats: embedded={Embedded}, fallbackUrl={Fallback}, missing={Missing}, " +
        "budgetUsed={BudgetUsedKb}KB, budgetRemaining={BudgetRemainingKb}KB, chunks={ChunkCount}")]
    private static partial void LogPreviewSyncStats(ILogger logger, int embedded, int fallback, int missing, int budgetUsedKb, int budgetRemainingKb, int chunkCount);

    [LoggerMessage(6, LogLevel.Information,
        "Preview sync stats: embedded=0, fallbackUrl=0, missing={Missing}, budgetUsed=0KB, budgetRemaining={BudgetRemainingKb}KB, chunks=0")]
    private static partial void LogPreviewSyncStatsEmpty(ILogger logger, int missing, int budgetRemainingKb);

    [LoggerMessage(7, LogLevel.Warning, "Could not get file size for {Path}")]
    private static partial void LogFileSizeFailed(ILogger logger, Exception ex, string path);

    [LoggerMessage(8, LogLevel.Warning, "Access denied getting file size for {Path}")]
    private static partial void LogFileSizeAccessDenied(ILogger logger, Exception ex, string path);

    [LoggerMessage(9, LogLevel.Debug, "Could not embed preview for item {Id}")]
    private static partial void LogEmbedPreviewFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(10, LogLevel.Debug, "Could not read image slot {Type} for item {Id}")]
    private static partial void LogReadImageSlotFailed(ILogger logger, Exception ex, ImageType type, Guid id);
}

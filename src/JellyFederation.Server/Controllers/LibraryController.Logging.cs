using Microsoft.Extensions.Logging;
using JellyFederation.Shared.Telemetry;
using System.Diagnostics;

namespace JellyFederation.Server.Controllers;

public partial class LibraryController
{
    private static void TagSpanOutcome(string outcome, Exception? ex = null) =>
        FederationTelemetry.SetOutcome(Activity.Current, outcome, ex);

    [LoggerMessage(1, LogLevel.Information, "Library sync started for {ServerId}: incoming={IncomingCount}, replaceAll={ReplaceAll}")]
    private static partial void LogSyncStarted(ILogger logger, Guid serverId, int incomingCount, bool replaceAll);

    [LoggerMessage(2, LogLevel.Information, "Library sync completed for {ServerId}: incoming={IncomingCount}, added={Added}, updated={Updated}, removed={Removed}")]
    private static partial void LogSyncCompleted(ILogger logger, Guid serverId, int incomingCount, int added, int updated, int removed);

    [LoggerMessage(3, LogLevel.Warning, "Invalid media type filter '{Type}' on {Endpoint}")]
    private static partial void LogInvalidMediaTypeFilter(ILogger logger, string type, string endpoint);

    [LoggerMessage(4, LogLevel.Debug, "Mine returned for {ServerId}: total={Total}, page={Page}, pageSize={PageSize}, search='{Search}', type='{Type}'")]
    private static partial void LogMineReturned(ILogger logger, Guid serverId, int total, int page, int pageSize, string? search, string? type);

    [LoggerMessage(5, LogLevel.Debug, "Mine counts returned for {ServerId}: all={Total}, search='{Search}'")]
    private static partial void LogMineCountsReturned(ILogger logger, Guid serverId, int total, string? search);

    [LoggerMessage(6, LogLevel.Warning, "SetRequestable item {ItemId} not found for {ServerId}")]
    private static partial void LogSetRequestableNotFound(ILogger logger, Guid itemId, Guid serverId);

    [LoggerMessage(7, LogLevel.Information, "SetRequestable updated item {ItemId} for {ServerId} -> {IsRequestable}")]
    private static partial void LogSetRequestableUpdated(ILogger logger, Guid itemId, Guid serverId, bool isRequestable);

    [LoggerMessage(8, LogLevel.Debug, "Browse returned for {ServerId}: total={Total}, page={Page}, pageSize={PageSize}, search='{Search}', type='{Type}'")]
    private static partial void LogBrowseReturned(ILogger logger, Guid serverId, int total, int page, int pageSize, string? search, string? type);

    [LoggerMessage(9, LogLevel.Debug, "Browse counts returned for {ServerId}: all={Total}, search='{Search}'")]
    private static partial void LogBrowseCountsReturned(ILogger logger, Guid serverId, int total, string? search);
}

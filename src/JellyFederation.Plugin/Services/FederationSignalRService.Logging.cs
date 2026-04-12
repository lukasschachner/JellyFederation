using Microsoft.Extensions.Logging;
using JellyFederation.Shared.Telemetry;
using System.Diagnostics;

namespace JellyFederation.Plugin.Services;

public partial class FederationSignalRService
{
    private static void TagSpanOutcome(string outcome, Exception? ex = null) =>
        FederationTelemetry.SetOutcome(Activity.Current, outcome, ex);

    [LoggerMessage(1, LogLevel.Warning, "Federation server URL or API key not configured — skipping SignalR connection")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(2, LogLevel.Information, "Hole punch request received for file request {Id}")]
    private static partial void LogHolePunchRequestReceived(ILogger logger, Guid id);

    [LoggerMessage(3, LogLevel.Error, "ExecuteAsync failed for request {Id}")]
    private static partial void LogExecuteAsyncFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(4, LogLevel.Information, "Incoming file request {Id} for item {ItemId} from server {From}")]
    private static partial void LogIncomingFileRequest(ILogger logger, Guid id, string itemId, Guid from);

    [LoggerMessage(5, LogLevel.Error, "PrepareAndSignalReadyAsync failed for request {Id}")]
    private static partial void LogPrepareAndSignalReadyFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(6, LogLevel.Information, "File request {Id} status: {Status} {Reason}")]
    private static partial void LogFileRequestStatus(ILogger logger, Guid id, string status, string reason);

    [LoggerMessage(7, LogLevel.Information, "Cancelling transfer for request {Id}")]
    private static partial void LogCancellingTransfer(ILogger logger, Guid id);

    [LoggerMessage(8, LogLevel.Warning, "SignalR reconnecting...")]
    private static partial void LogReconnecting(ILogger logger);

    [LoggerMessage(9, LogLevel.Information, "SignalR reconnected — re-syncing library")]
    private static partial void LogReconnected(ILogger logger);

    [LoggerMessage(10, LogLevel.Warning, "Library re-sync after reconnect failed")]
    private static partial void LogResyncAfterReconnectFailed(ILogger logger, Exception ex);

    [LoggerMessage(11, LogLevel.Information, "Connected to federation server at {Url}")]
    private static partial void LogConnected(ILogger logger, string url);
}

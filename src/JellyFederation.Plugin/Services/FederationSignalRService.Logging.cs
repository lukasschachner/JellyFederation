using System.Diagnostics;
using JellyFederation.Shared.Telemetry;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

public partial class FederationSignalRService
{
    private static void TagSpanOutcome(string outcome, Exception? ex = null)
    {
        FederationTelemetry.SetOutcome(Activity.Current, outcome, ex);
    }

    [LoggerMessage(1, LogLevel.Warning,
        "Federation server URL or API key not configured — skipping SignalR connection")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(2, LogLevel.Information, "Hole punch request received for file request {Id}")]
    private static partial void LogHolePunchRequestReceived(ILogger logger, Guid id);

    [LoggerMessage(3, LogLevel.Error, "ExecuteAsync failed for request {Id}")]
    private static partial void LogExecuteAsyncFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(4, LogLevel.Information, "Incoming file request {Id} for item {ItemId} from server {From}")]
    private static partial void LogIncomingFileRequest(ILogger logger, Guid id, string itemId, Guid from);

    [LoggerMessage(5, LogLevel.Error, "PrepareAndSignalReadyAsync failed for request {Id}")]
    private static partial void LogPrepareAndSignalReadyFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(6, LogLevel.Information,
        "File request {Id} status: {Status} {Reason} (mode={Mode}, failureCategory={FailureCategory}, failureCode={FailureCode})")]
    private static partial void LogFileRequestStatus(
        ILogger logger,
        Guid id,
        string status,
        string reason,
        string mode,
        string failureCategory,
        string failureCode);

    [LoggerMessage(7, LogLevel.Information, "Cancelling transfer for request {Id}")]
    private static partial void LogCancellingTransfer(ILogger logger, Guid id);

    [LoggerMessage(8, LogLevel.Warning, "SignalR reconnecting...")]
    private static partial void LogReconnecting(ILogger logger);

    [LoggerMessage(9, LogLevel.Information, "SignalR reconnected — re-syncing library")]
    private static partial void LogReconnected(ILogger logger);

    [LoggerMessage(10, LogLevel.Warning, "Library re-sync after reconnect failed")]
    private static partial void LogResyncAfterReconnectFailed(ILogger logger, Exception ex);

    [LoggerMessage(12, LogLevel.Warning,
        "Library re-sync after reconnect returned failure: code={Code} category={Category} message={Message}")]
    private static partial void LogResyncAfterReconnectFailureDescriptor(
        ILogger logger,
        string code,
        string category,
        string message);

    [LoggerMessage(11, LogLevel.Information, "Connected to federation server at {Url}")]
    private static partial void LogConnected(ILogger logger, string url);

    [LoggerMessage(13, LogLevel.Information,
        "IceNegotiateStart received for request {FileRequestId} as {Role}")]
    private static partial void LogIceNegotiateStart(ILogger logger, Guid fileRequestId, string role);

    [LoggerMessage(14, LogLevel.Error, "IceNegotiateStart handling failed for request {FileRequestId}")]
    private static partial void LogIceNegotiateStartFailed(ILogger logger, Exception ex, Guid fileRequestId);

    [LoggerMessage(15, LogLevel.Debug, "IceSignal received for request {FileRequestId}: type={Type}")]
    private static partial void LogIceSignalReceived(ILogger logger, Guid fileRequestId, string type);

    [LoggerMessage(16, LogLevel.Debug,
        "Relay chunk received for request {FileRequestId}: index={ChunkIndex}, isEof={IsEof}")]
    private static partial void LogRelayChunkReceived(ILogger logger, Guid fileRequestId, long chunkIndex,
        bool isEof);

    [LoggerMessage(17, LogLevel.Information, "RelayTransferStart received for request {FileRequestId}")]
    private static partial void LogRelayTransferStartReceived(ILogger logger, Guid fileRequestId);

    [LoggerMessage(18, LogLevel.Error, "RelayTransferStart handling failed for request {FileRequestId}")]
    private static partial void LogRelayTransferStartFailed(ILogger logger, Exception ex, Guid fileRequestId);
}

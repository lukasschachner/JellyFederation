using Microsoft.Extensions.Logging;
using System.Net;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using System.Diagnostics;

namespace JellyFederation.Plugin.Services;

public partial class FileTransferService
{
    private static void TagSpanOutcome(string outcome, Exception? ex = null) =>
        FederationTelemetry.SetOutcome(Activity.Current, outcome, ex);

    [LoggerMessage(1, LogLevel.Warning, "Invalid Jellyfin item ID format: {Id}")]
    private static partial void LogInvalidItemId(ILogger logger, string id);

    [LoggerMessage(2, LogLevel.Error, "Item {Id} not found or has no file path")]
    private static partial void LogItemNotFound(ILogger logger, string id);

    [LoggerMessage(3, LogLevel.Error, "File not found: {Path}")]
    private static partial void LogFileNotFound(ILogger logger, string path);

    [LoggerMessage(4, LogLevel.Information, "Sending {File} ({Size} bytes) to {Remote}")]
    private static partial void LogSendingFile(ILogger logger, string file, long size, IPEndPoint remote);

    [LoggerMessage(5, LogLevel.Information, "File sent successfully: {File}")]
    private static partial void LogFileSent(ILogger logger, string file);

    [LoggerMessage(6, LogLevel.Information, "Send cancelled for request {Id}")]
    private static partial void LogSendCancelled(ILogger logger, Guid id);

    [LoggerMessage(7, LogLevel.Error, "Download directory not configured")]
    private static partial void LogDownloadDirNotConfigured(ILogger logger);

    [LoggerMessage(8, LogLevel.Information, "Receive cancelled for request {Id}")]
    private static partial void LogReceiveCancelled(ILogger logger, Guid id);

    [LoggerMessage(9, LogLevel.Error, "Receive timed out waiting for data")]
    private static partial void LogReceiveTimeout(ILogger logger);

    [LoggerMessage(10, LogLevel.Information, "Received EOF — file transfer complete")]
    private static partial void LogReceivedEof(ILogger logger);

    [LoggerMessage(11, LogLevel.Information, "Receiving {File} ({Size} bytes) → {Path}")]
    private static partial void LogReceivingFile(ILogger logger, string file, long size, string path);

    [LoggerMessage(12, LogLevel.Debug, "Out-of-order packet {Seq} received, expected {ExpectedSeq}")]
    private static partial void LogOutOfOrderPacket(ILogger logger, uint seq, uint expectedSeq);

    [LoggerMessage(13, LogLevel.Warning, "Deleted incomplete file {Path}")]
    private static partial void LogDeletedIncompleteFile(ILogger logger, string path);

    [LoggerMessage(14, LogLevel.Warning, "Could not delete incomplete file {Path}")]
    private static partial void LogCouldNotDeleteIncompleteFile(ILogger logger, Exception ex, string path);

    [LoggerMessage(15, LogLevel.Information, "File saved to {Path}")]
    private static partial void LogFileSaved(ILogger logger, string path);

    [LoggerMessage(16, LogLevel.Warning, "Failed to mark request {Id} complete: {Status}")]
    private static partial void LogMarkCompleteFailed(ILogger logger, Guid id, HttpStatusCode status);

    [LoggerMessage(17, LogLevel.Warning, "Could not notify server of completion for request {Id}")]
    private static partial void LogNotifyCompletionFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(18, LogLevel.Information, "Triggered library scan for {File}")]
    private static partial void LogTriggeredLibraryScan(ILogger logger, string file);

    [LoggerMessage(19, LogLevel.Warning,
        "Download directory {Dir} is not part of any Jellyfin library. " +
        "Add it via Dashboard → Libraries so downloaded files appear automatically. " +
        "Running a full library scan now as fallback.")]
    private static partial void LogDownloadDirNotInLibrary(ILogger logger, string dir);

    [LoggerMessage(20, LogLevel.Debug, "Failed to report transfer progress for {Id}")]
    private static partial void LogReportProgressFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(21, LogLevel.Debug, "ACK timeout for seq {Seq}, retrying ({Attempt}/{Max})")]
    private static partial void LogAckTimeout(ILogger logger, uint seq, int attempt, int max);

    [LoggerMessage(22, LogLevel.Information, "Transfer {Id} mode {Mode} selected ({Reason})")]
    private static partial void LogTransferMode(
        ILogger logger,
        Guid id,
        TransferTransportMode mode,
        TransferSelectionReason reason);

    [LoggerMessage(23, LogLevel.Warning, "QUIC selected for send {Id} but falling back to ARQ ({Reason})")]
    private static partial void LogQuicFallbackBeforeSend(
        ILogger logger,
        Guid id,
        TransferSelectionReason reason);

    [LoggerMessage(24, LogLevel.Warning, "QUIC selected for receive {Id} but falling back to ARQ ({Reason})")]
    private static partial void LogQuicFallbackBeforeReceive(
        ILogger logger,
        Guid id,
        TransferSelectionReason reason);

    [LoggerMessage(25, LogLevel.Warning, "QUIC transfer for {Id} failed; falling back to ARQ. Reason: {Reason}")]
    private static partial void LogQuicRuntimeFallback(
        ILogger logger,
        Guid id,
        string reason);
}

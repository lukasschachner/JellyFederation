using Microsoft.Extensions.Logging;
using System.Net;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using System.Diagnostics;

namespace JellyFederation.Server.Hubs;

public partial class FederationHub
{
    private static void TagSpanOutcome(string outcome, Exception? ex = null) =>
        FederationTelemetry.SetOutcome(Activity.Current, outcome, ex);

    [LoggerMessage(1, LogLevel.Debug, "Web client for server {Name} ({Id}) connected")]
    private static partial void LogWebClientConnected(ILogger logger, string name, Guid id);

    [LoggerMessage(2, LogLevel.Information, "Server {Name} ({Id}) connected from {Ip}")]
    private static partial void LogServerConnected(ILogger logger, string name, Guid id, IPAddress ip);

    [LoggerMessage(3, LogLevel.Information, "Re-sending FileRequestNotification for request {Id} to {Name} (isSender={IsSender})")]
    private static partial void LogResendingNotification(ILogger logger, Guid id, string name, bool isSender);

    [LoggerMessage(4, LogLevel.Warning, "Failed to resend notification for {Id} to {Name}: {Err}")]
    private static partial void LogResendNotificationFailed(ILogger logger, Guid id, string name, string err);

    [LoggerMessage(5, LogLevel.Information, "Server {Name} ({Id}) disconnected")]
    private static partial void LogServerDisconnected(ILogger logger, string name, Guid id);

    [LoggerMessage(6, LogLevel.Warning, "ReportHolePunchReady: file request {Id} not found — ignoring")]
    private static partial void LogHolePunchReadyNotFound(ILogger logger, Guid id);

    [LoggerMessage(7, LogLevel.Information, "ReportHolePunchReady from server {ServerId} for request {RequestId} on port {Port}")]
    private static partial void LogHolePunchReady(ILogger logger, Guid serverId, Guid requestId, int port);

    [LoggerMessage(25, LogLevel.Information,
        "Hole punch capabilities from {ServerId} for request {RequestId}: supportsQuic={SupportsQuic}, thresholdBytes={ThresholdBytes}, overridePublicIp={OverridePublicIp}")]
    private static partial void LogHolePunchCapabilities(
        ILogger logger,
        Guid serverId,
        Guid requestId,
        bool supportsQuic,
        long thresholdBytes,
        string overridePublicIp);

    [LoggerMessage(8, LogLevel.Information, "Using override public IP {Ip} for {ServerId}")]
    private static partial void LogOverrideIp(ILogger logger, IPAddress ip, Guid serverId);

    [LoggerMessage(9, LogLevel.Error, "Unhandled error in ReportHolePunchReady for request {Id}")]
    private static partial void LogHolePunchReadyError(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(10, LogLevel.Warning, "Could not match candidates to file request {Id}")]
    private static partial void LogCandidateMatchFailed(ILogger logger, Guid id);

    [LoggerMessage(11, LogLevel.Information, "Hole punch initiated for request {Id}: {SenderIp}:{SenderPort} <-> {ReceiverIp}:{ReceiverPort}")]
    private static partial void LogHolePunchInitiated(ILogger logger, Guid id, IPAddress senderIp, int senderPort, IPAddress receiverIp, int receiverPort);

    [LoggerMessage(12, LogLevel.Warning, "Hub authentication missing API key for connection {ConnectionId}")]
    private static partial void LogHubAuthenticationMissingApiKey(ILogger logger, string connectionId);

    [LoggerMessage(13, LogLevel.Warning, "Hub authentication failed for connection {ConnectionId}")]
    private static partial void LogHubAuthenticationFailed(ILogger logger, string connectionId);

    [LoggerMessage(14, LogLevel.Warning, "Aborting unauthenticated hub connection {ConnectionId}")]
    private static partial void LogHubConnectionAbortedUnauthenticated(ILogger logger, string connectionId);

    [LoggerMessage(15, LogLevel.Warning, "ReportHolePunchReady from unknown connection {ConnectionId} for request {RequestId}")]
    private static partial void LogHolePunchReadyUnknownConnection(ILogger logger, string connectionId, Guid requestId);

    [LoggerMessage(16, LogLevel.Warning, "ReportHolePunchResult: file request {RequestId} not found")]
    private static partial void LogHolePunchResultNotFound(ILogger logger, Guid requestId);

    [LoggerMessage(17, LogLevel.Information, "ReportHolePunchResult received for {RequestId}: success={Success}, error='{Error}'")]
    private static partial void LogHolePunchResultReceived(ILogger logger, Guid requestId, bool success, string error);

    [LoggerMessage(18, LogLevel.Information, "Transfer started for request {RequestId}")]
    private static partial void LogTransferStarted(ILogger logger, Guid requestId);

    [LoggerMessage(19, LogLevel.Warning, "Hole punch failed and request {RequestId} marked failed: {Reason}")]
    private static partial void LogHolePunchMarkedFailed(ILogger logger, Guid requestId, string reason);

    [LoggerMessage(20, LogLevel.Warning, "Missing public IP for request {RequestId}: senderConn={SenderConnectionId}, receiverConn={ReceiverConnectionId}")]
    private static partial void LogHolePunchMissingPublicIp(ILogger logger, Guid requestId, string senderConnectionId, string receiverConnectionId);

    [LoggerMessage(21, LogLevel.Debug, "Transfer progress ignored for missing request {RequestId}: {BytesTransferred}/{TotalBytes}")]
    private static partial void LogTransferProgressRequestNotFound(ILogger logger, Guid requestId, long bytesTransferred, long totalBytes);

    [LoggerMessage(22, LogLevel.Debug, "Transfer progress forwarded for request {RequestId} to {OwningServerId} and {RequestingServerId}: {BytesTransferred}/{TotalBytes}")]
    private static partial void LogTransferProgressForwarded(ILogger logger, Guid requestId, Guid owningServerId, Guid requestingServerId, long bytesTransferred, long totalBytes);

    [LoggerMessage(23, LogLevel.Debug, "Disconnected connection {ConnectionId} with exception")]
    private static partial void LogDisconnectedWithException(ILogger logger, string connectionId, Exception ex);

    [LoggerMessage(24, LogLevel.Information, "Selected transport mode {Mode} for request {RequestId} ({Reason})")]
    private static partial void LogTransportModeSelected(
        ILogger logger,
        Guid requestId,
        TransferTransportMode mode,
        TransferSelectionReason reason);
}

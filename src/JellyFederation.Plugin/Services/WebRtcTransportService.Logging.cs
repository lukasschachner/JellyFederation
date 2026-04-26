using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

public partial class WebRtcTransportService
{
    [LoggerMessage(1, LogLevel.Information, "ICE negotiation starting as Offerer for request {FileRequestId}")]
    private static partial void LogBeginAsOfferer(ILogger logger, Guid fileRequestId);

    [LoggerMessage(2, LogLevel.Information, "ICE negotiation starting as Answerer for request {FileRequestId}")]
    private static partial void LogBeginAsAnswerer(ILogger logger, Guid fileRequestId);

    [LoggerMessage(3, LogLevel.Information,
        "DataChannel opened for request {FileRequestId} (role={Role})")]
    private static partial void LogDataChannelOpen(ILogger logger, Guid fileRequestId, IceRole role);

    [LoggerMessage(4, LogLevel.Information, "DataChannel closed for request {FileRequestId}")]
    private static partial void LogDataChannelClosed(ILogger logger, Guid fileRequestId);

    [LoggerMessage(5, LogLevel.Information,
        "ICE connection state changed for request {FileRequestId}: {State}")]
    private static partial void LogIceStateChanged(ILogger logger, Guid fileRequestId, string state);

    [LoggerMessage(6, LogLevel.Debug, "SDP offer created for request {FileRequestId}")]
    private static partial void LogSdpOfferCreated(ILogger logger, Guid fileRequestId);

    [LoggerMessage(7, LogLevel.Debug, "SDP answer created for request {FileRequestId}")]
    private static partial void LogSdpAnswerCreated(ILogger logger, Guid fileRequestId);

    [LoggerMessage(8, LogLevel.Debug, "SDP answer applied (remote description set) for request {FileRequestId}")]
    private static partial void LogSdpAnswerApplied(ILogger logger, Guid fileRequestId);

    [LoggerMessage(9, LogLevel.Debug, "Waiting for SDP offer to arrive for request {FileRequestId}")]
    private static partial void LogWaitingForOffer(ILogger logger, Guid fileRequestId);

    [LoggerMessage(10, LogLevel.Information,
        "DataChannel received from offerer for request {FileRequestId}")]
    private static partial void LogDataChannelReceived(ILogger logger, Guid fileRequestId);

    [LoggerMessage(11, LogLevel.Warning,
        "ICE signal {Type} arrived but no session found for request {FileRequestId} — dropping")]
    private static partial void LogIceSignalNoSession(ILogger logger, Guid fileRequestId, string type);

    [LoggerMessage(12, LogLevel.Warning,
        "ICE negotiation timed out (30 s) for request {FileRequestId}")]
    private static partial void LogIceTimeout(ILogger logger, Guid fileRequestId);

    [LoggerMessage(13, LogLevel.Debug,
        "Trickle ICE candidate forwarded for request {FileRequestId}")]
    private static partial void LogCandidateForwarded(ILogger logger, Guid fileRequestId);

    [LoggerMessage(14, LogLevel.Warning,
        "Failed to forward ICE candidate for request {FileRequestId}")]
    private static partial void LogCandidateForwardFailed(ILogger logger, Exception ex, Guid fileRequestId);

    [LoggerMessage(15, LogLevel.Warning,
        "Failed to add remote ICE candidate for request {FileRequestId}")]
    private static partial void LogIceCandidateAddFailed(ILogger logger, Exception ex, Guid fileRequestId);

    [LoggerMessage(16, LogLevel.Warning,
        "ICE failed — engaging relay fallback for request {FileRequestId}")]
    private static partial void LogRelayFallbackEngaged(ILogger logger, Guid fileRequestId);

    [LoggerMessage(17, LogLevel.Warning,
        "ICE failed — switching to relay receive mode for request {FileRequestId}")]
    private static partial void LogRelayReceiveModeEngaged(ILogger logger, Guid fileRequestId);

    [LoggerMessage(18, LogLevel.Warning,
        "Failed to notify peer of relay start for request {FileRequestId}")]
    private static partial void LogRelayNotifyFailed(ILogger logger, Exception ex, Guid fileRequestId);

    [LoggerMessage(19, LogLevel.Debug, "ICE session cleaned up for request {FileRequestId}")]
    private static partial void LogSessionCleaned(ILogger logger, Guid fileRequestId);

    [LoggerMessage(20, LogLevel.Warning, "Failed to report WebRTC transfer start for request {FileRequestId}")]
    private static partial void LogReportWebRtcStartedFailed(ILogger logger, Exception ex, Guid fileRequestId);

    [LoggerMessage(21, LogLevel.Information,
        "WebRTC ICE server configuration for request {FileRequestId}: stun={StunServer}, turnConfigured={TurnConfigured}")]
    private static partial void LogIceServersConfigured(ILogger logger, Guid fileRequestId, string stunServer,
        bool turnConfigured);

    [LoggerMessage(22, LogLevel.Debug,
        "ICE signal handled for request {FileRequestId}: type={Type}, payloadChars={PayloadChars}")]
    private static partial void LogIceSignalHandled(ILogger logger, Guid fileRequestId, string type, int payloadChars);

    [LoggerMessage(23, LogLevel.Debug,
        "Queued ICE signal for request {FileRequestId} because session is not yet available")]
    private static partial void LogIceSignalQueued(ILogger logger, Guid fileRequestId);

    [LoggerMessage(24, LogLevel.Debug,
        "Received candidate signal for request {FileRequestId}; remoteDescriptionApplied={RemoteDescriptionApplied}")]
    private static partial void LogCandidateSignalReceived(ILogger logger, Guid fileRequestId,
        bool remoteDescriptionApplied);

    [LoggerMessage(25, LogLevel.Debug,
        "Queued remote candidate for request {FileRequestId} until remote description is applied")]
    private static partial void LogCandidateQueuedUntilRemoteDescription(ILogger logger, Guid fileRequestId);

    [LoggerMessage(26, LogLevel.Debug,
        "Applied remote ICE candidate immediately for request {FileRequestId}")]
    private static partial void LogCandidateAppliedImmediately(ILogger logger, Guid fileRequestId);

    [LoggerMessage(27, LogLevel.Debug,
        "Flushed {CandidateCount} queued ICE candidates for request {FileRequestId}")]
    private static partial void LogPendingCandidatesFlushed(ILogger logger, Guid fileRequestId, int candidateCount);

    [LoggerMessage(28, LogLevel.Debug,
        "Drained {SignalCount} pending ICE signals for request {FileRequestId}")]
    private static partial void LogPendingSignalsDrained(ILogger logger, Guid fileRequestId, int signalCount);

    [LoggerMessage(29, LogLevel.Warning,
        "Received malformed ICE candidate payload for request {FileRequestId}; candidate was ignored")]
    private static partial void LogMalformedCandidatePayload(ILogger logger, Guid fileRequestId);
}

using System.Net;

namespace JellyFederation.Server.Services;

public partial class ServerConnectionTracker
{
    [LoggerMessage(1, LogLevel.Debug,
        "Replacing stale connection for server {ServerId}: {OldConnectionId} -> {NewConnectionId}")]
    private static partial void LogReplacingStaleConnection(ILogger logger, Guid serverId, string oldConnectionId,
        string newConnectionId);

    [LoggerMessage(2, LogLevel.Information,
        "Registered connection {ConnectionId} for server {ServerId} from {PublicIp}")]
    private static partial void LogRegisteredConnection(ILogger logger, Guid serverId, string connectionId,
        IPAddress publicIp);

    [LoggerMessage(3, LogLevel.Debug, "Unregister requested for connection {ConnectionId}")]
    private static partial void LogUnregisteringConnection(ILogger logger, string connectionId);

    [LoggerMessage(4, LogLevel.Information, "Unregistered connection {ConnectionId} for server {ServerId}")]
    private static partial void LogUnregisteredConnection(ILogger logger, Guid serverId, string connectionId);

    [LoggerMessage(5, LogLevel.Debug, "Unregister requested for unknown connection {ConnectionId}")]
    private static partial void LogUnregisterWithoutServer(ILogger logger, string connectionId);

    [LoggerMessage(6, LogLevel.Information, "Public IP override for connection {ConnectionId}: {Ip}")]
    private static partial void LogPublicIpOverride(ILogger logger, string connectionId, IPAddress ip);

    [LoggerMessage(7, LogLevel.Debug,
        "Staged hole punch candidate for request {RequestId}: server={ServerId}, port={UdpPort}, candidates={CandidateCount}")]
    private static partial void LogHolePunchStaged(ILogger logger, Guid requestId, Guid serverId, int udpPort,
        int candidateCount);

    [LoggerMessage(8, LogLevel.Information,
        "Hole punch request {RequestId} ready to dispatch with {CandidateCount} candidate(s)")]
    private static partial void LogHolePunchReadyToDispatch(ILogger logger, Guid requestId, int candidateCount);
}
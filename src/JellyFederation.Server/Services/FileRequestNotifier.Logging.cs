using JellyFederation.Shared.Models;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Server.Services;

public partial class FileRequestNotifier
{
    [LoggerMessage(1, LogLevel.Debug, "Notify status for request {RequestId} status {Status} (owner {OwningServerId}, requester {RequestingServerId})")]
    private static partial void LogNotifyStatus(ILogger logger, Guid requestId, FileRequestStatus status, Guid owningServerId, Guid requestingServerId);

    [LoggerMessage(2, LogLevel.Debug, "Sending status update for request {RequestId} to server {ServerId} connection {ConnectionId}")]
    private static partial void LogNotifyPluginConnection(ILogger logger, Guid requestId, Guid serverId, string connectionId);

    [LoggerMessage(3, LogLevel.Debug, "No plugin connection for status update request {RequestId} server {ServerId}")]
    private static partial void LogNotifyPluginConnectionMissing(ILogger logger, Guid requestId, Guid serverId);

    [LoggerMessage(4, LogLevel.Debug, "Broadcasted status update for request {RequestId} to browser groups for {OwningServerId} and {RequestingServerId}")]
    private static partial void LogNotifyBrowserGroups(ILogger logger, Guid requestId, Guid owningServerId, Guid requestingServerId);

    [LoggerMessage(5, LogLevel.Information, "Send cancel for request {RequestId} (owner {OwningServerId}, requester {RequestingServerId})")]
    private static partial void LogSendCancel(ILogger logger, Guid requestId, Guid owningServerId, Guid requestingServerId);

    [LoggerMessage(6, LogLevel.Debug, "Sending cancel for request {RequestId} to server {ServerId} connection {ConnectionId}")]
    private static partial void LogCancelPluginConnection(ILogger logger, Guid requestId, Guid serverId, string connectionId);

    [LoggerMessage(7, LogLevel.Debug, "No plugin connection for cancel request {RequestId} server {ServerId}")]
    private static partial void LogCancelPluginConnectionMissing(ILogger logger, Guid requestId, Guid serverId);

    [LoggerMessage(8, LogLevel.Debug, "Broadcasted cancel status for request {RequestId} to browser groups for {OwningServerId} and {RequestingServerId}")]
    private static partial void LogCancelBrowserGroups(ILogger logger, Guid requestId, Guid owningServerId, Guid requestingServerId);
}

using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace JellyFederation.Server.Controllers;

public partial class FileRequestsController
{
    private static void TagSpanOutcome(string outcome, Exception? ex = null) =>
        FederationTelemetry.SetOutcome(Activity.Current, outcome, ex);

    [LoggerMessage(1, LogLevel.Information, "Create file request from {RequestingServerId} to {OwningServerId} for item {ItemId}")]
    private static partial void LogCreateRequested(ILogger logger, Guid requestingServerId, Guid owningServerId, string itemId);

    [LoggerMessage(2, LogLevel.Warning, "Create file request failed: owning server {OwningServerId} not found (requesting {RequestingServerId})")]
    private static partial void LogCreateOwningServerNotFound(ILogger logger, Guid owningServerId, Guid requestingServerId);

    [LoggerMessage(3, LogLevel.Warning, "Create file request forbidden: no accepted invitation between {RequestingServerId} and {OwningServerId}")]
    private static partial void LogCreateForbiddenNoInvitation(ILogger logger, Guid requestingServerId, Guid owningServerId);

    [LoggerMessage(4, LogLevel.Information, "Created file request {RequestId} from {RequestingServerId} to {OwningServerId}")]
    private static partial void LogCreated(ILogger logger, Guid requestId, Guid requestingServerId, Guid owningServerId);

    [LoggerMessage(5, LogLevel.Debug, "Owning plugin offline while creating request {RequestId} for server {OwningServerId}")]
    private static partial void LogCreateOwnerPluginOffline(ILogger logger, Guid requestId, Guid owningServerId);

    [LoggerMessage(6, LogLevel.Debug, "Requesting plugin offline while creating request {RequestId} for server {RequestingServerId}")]
    private static partial void LogCreateRequesterPluginOffline(ILogger logger, Guid requestId, Guid requestingServerId);

    [LoggerMessage(7, LogLevel.Debug, "Returned {Count} file request(s) for server {ServerId}")]
    private static partial void LogListReturned(ILogger logger, Guid serverId, int count);

    [LoggerMessage(8, LogLevel.Warning, "Cancel file request {RequestId} failed: not found for server {ServerId}")]
    private static partial void LogCancelNotFound(ILogger logger, Guid requestId, Guid serverId);

    [LoggerMessage(9, LogLevel.Warning, "Cancel file request {RequestId} forbidden for server {ServerId} (requesting={RequestingServerId}, owning={OwningServerId})")]
    private static partial void LogCancelForbidden(ILogger logger, Guid requestId, Guid serverId, Guid requestingServerId, Guid owningServerId);

    [LoggerMessage(10, LogLevel.Information, "Cancel file request {RequestId} rejected because status is {Status}")]
    private static partial void LogCancelRejectedTerminal(ILogger logger, Guid requestId, FileRequestStatus status);

    [LoggerMessage(11, LogLevel.Information, "Cancelled file request {RequestId} by server {ServerId}")]
    private static partial void LogCancelled(ILogger logger, Guid requestId, Guid serverId);

    [LoggerMessage(12, LogLevel.Warning, "Mark complete failed: file request {RequestId} not found for server {ServerId}")]
    private static partial void LogMarkCompleteNotFound(ILogger logger, Guid requestId, Guid serverId);

    [LoggerMessage(13, LogLevel.Warning, "Mark complete forbidden: file request {RequestId} by server {ServerId} (requesting={RequestingServerId})")]
    private static partial void LogMarkCompleteForbidden(ILogger logger, Guid requestId, Guid serverId, Guid requestingServerId);

    [LoggerMessage(14, LogLevel.Information, "Mark complete conflict: file request {RequestId} is in status {Status}")]
    private static partial void LogMarkCompleteConflict(ILogger logger, Guid requestId, FileRequestStatus status);

    [LoggerMessage(15, LogLevel.Information, "Marked file request {RequestId} as completed by server {ServerId}")]
    private static partial void LogMarkedComplete(ILogger logger, Guid requestId, Guid serverId);
}

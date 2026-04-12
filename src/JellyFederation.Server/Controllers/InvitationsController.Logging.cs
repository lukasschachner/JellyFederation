using JellyFederation.Shared.Models;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Server.Controllers;

public partial class InvitationsController
{
    [LoggerMessage(1, LogLevel.Information, "Invitation send requested from {FromServerId} to {ToServerId}")]
    private static partial void LogInvitationSendRequested(ILogger logger, Guid fromServerId, Guid toServerId);

    [LoggerMessage(2, LogLevel.Warning, "Invitation target not found: from {FromServerId} to {ToServerId}")]
    private static partial void LogInvitationTargetNotFound(ILogger logger, Guid fromServerId, Guid toServerId);

    [LoggerMessage(3, LogLevel.Information, "Invitation relationship already exists between {FromServerId} and {ToServerId}")]
    private static partial void LogInvitationRelationshipExists(ILogger logger, Guid fromServerId, Guid toServerId);

    [LoggerMessage(4, LogLevel.Information, "Invitation {InvitationId} created from {FromServerId} to {ToServerId}")]
    private static partial void LogInvitationCreated(ILogger logger, Guid invitationId, Guid fromServerId, Guid toServerId);

    [LoggerMessage(5, LogLevel.Debug, "Returned {Count} invitation(s) for server {ServerId}")]
    private static partial void LogInvitationListReturned(ILogger logger, Guid serverId, int count);

    [LoggerMessage(6, LogLevel.Warning, "Invitation {InvitationId} not found for response by server {ServerId}")]
    private static partial void LogInvitationRespondNotFound(ILogger logger, Guid invitationId, Guid serverId);

    [LoggerMessage(7, LogLevel.Information, "Invitation {InvitationId} cannot be responded to because status is {Status}")]
    private static partial void LogInvitationRespondNotPending(ILogger logger, Guid invitationId, InvitationStatus status);

    [LoggerMessage(8, LogLevel.Information, "Invitation {InvitationId} responded by {ServerId} accept={Accept}")]
    private static partial void LogInvitationResponded(ILogger logger, Guid invitationId, Guid serverId, bool accept);

    [LoggerMessage(9, LogLevel.Warning, "Invitation {InvitationId} not found for revoke by server {ServerId}")]
    private static partial void LogInvitationRevokeNotFound(ILogger logger, Guid invitationId, Guid serverId);

    [LoggerMessage(10, LogLevel.Information, "Invitation {InvitationId} revoked by server {ServerId}")]
    private static partial void LogInvitationRevoked(ILogger logger, Guid invitationId, Guid serverId);
}

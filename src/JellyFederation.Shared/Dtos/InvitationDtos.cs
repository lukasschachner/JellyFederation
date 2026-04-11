using JellyFederation.Shared.Models;

namespace JellyFederation.Shared.Dtos;

public record SendInvitationRequest(Guid ToServerId);

public record InvitationDto(
    Guid Id,
    Guid FromServerId,
    string FromServerName,
    Guid ToServerId,
    string ToServerName,
    InvitationStatus Status,
    DateTime CreatedAt);

public record RespondToInvitationRequest(bool Accept);

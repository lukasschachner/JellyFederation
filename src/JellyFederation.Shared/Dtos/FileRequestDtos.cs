using JellyFederation.Shared.Models;

namespace JellyFederation.Shared.Dtos;

public record CreateFileRequestDto(string JellyfinItemId, Guid OwningServerId);

public record FileRequestDto(
    Guid Id,
    Guid RequestingServerId,
    string RequestingServerName,
    Guid OwningServerId,
    string OwningServerName,
    string JellyfinItemId,
    string? ItemTitle,
    FileRequestStatus Status,
    string? FailureReason,
    DateTime CreatedAt);

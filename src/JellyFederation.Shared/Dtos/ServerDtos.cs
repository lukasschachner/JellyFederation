namespace JellyFederation.Shared.Dtos;

public record RegisterServerRequest(string Name, string OwnerUserId);

public record RegisterServerResponse(Guid ServerId, string ApiKey);

public record ServerInfoDto(
    Guid Id,
    string Name,
    string OwnerUserId,
    bool IsOnline,
    DateTime LastSeenAt,
    int MediaItemCount);

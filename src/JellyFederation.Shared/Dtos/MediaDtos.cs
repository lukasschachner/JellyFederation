using JellyFederation.Shared.Models;

namespace JellyFederation.Shared.Dtos;

public record MediaItemDto(
    Guid Id,
    Guid ServerId,
    string ServerName,
    string JellyfinItemId,
    string Title,
    MediaType Type,
    int? Year,
    string? Overview,
    string? ImageUrl,
    long FileSizeBytes,
    bool IsRequestable);

public record SyncMediaRequest(List<MediaItemSyncEntry> Items, bool ReplaceAll = true);

public record MediaItemSyncEntry(
    string JellyfinItemId,
    string Title,
    MediaType Type,
    int? Year,
    string? Overview,
    string? ImageUrl,
    long FileSizeBytes);

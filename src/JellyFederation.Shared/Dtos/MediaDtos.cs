using JellyFederation.Shared.Models;

namespace JellyFederation.Shared.Dtos;

public record MediaItemDto
{
    public MediaItemDto(Guid Id,
        Guid ServerId,
        string ServerName,
        string JellyfinItemId,
        string Title,
        MediaType Type,
        int? Year,
        string? Overview,
        string? ImageUrl,
        long FileSizeBytes,
        bool IsRequestable)
    {
        this.Id = Id;
        this.ServerId = ServerId;
        this.ServerName = ServerName;
        this.JellyfinItemId = JellyfinItemId;
        this.Title = Title;
        this.Type = Type;
        this.Year = Year;
        this.Overview = Overview;
        this.ImageUrl = ImageUrl;
        this.FileSizeBytes = FileSizeBytes;
        this.IsRequestable = IsRequestable;
    }

    public Guid Id { get; init; }
    public Guid ServerId { get; init; }
    public string ServerName { get; init; }
    public string JellyfinItemId { get; init; }
    public string Title { get; init; }
    public MediaType Type { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }
    public string? ImageUrl { get; init; }
    public long FileSizeBytes { get; init; }
    public bool IsRequestable { get; init; }

    public void Deconstruct(out Guid Id, out Guid ServerId, out string ServerName, out string JellyfinItemId,
        out string Title, out MediaType Type, out int? Year, out string? Overview, out string? ImageUrl,
        out long FileSizeBytes, out bool IsRequestable)
    {
        Id = this.Id;
        ServerId = this.ServerId;
        ServerName = this.ServerName;
        JellyfinItemId = this.JellyfinItemId;
        Title = this.Title;
        Type = this.Type;
        Year = this.Year;
        Overview = this.Overview;
        ImageUrl = this.ImageUrl;
        FileSizeBytes = this.FileSizeBytes;
        IsRequestable = this.IsRequestable;
    }
}

public record SyncMediaRequest
{
    public SyncMediaRequest(List<MediaItemSyncEntry> Items, bool ReplaceAll = true)
    {
        this.Items = Items;
        this.ReplaceAll = ReplaceAll;
    }

    public List<MediaItemSyncEntry> Items { get; init; }
    public bool ReplaceAll { get; init; }

    public void Deconstruct(out List<MediaItemSyncEntry> Items, out bool ReplaceAll)
    {
        Items = this.Items;
        ReplaceAll = this.ReplaceAll;
    }
}

public record MediaItemSyncEntry
{
    public MediaItemSyncEntry(string JellyfinItemId,
        string Title,
        MediaType Type,
        int? Year,
        string? Overview,
        string? ImageUrl,
        long FileSizeBytes)
    {
        this.JellyfinItemId = JellyfinItemId;
        this.Title = Title;
        this.Type = Type;
        this.Year = Year;
        this.Overview = Overview;
        this.ImageUrl = ImageUrl;
        this.FileSizeBytes = FileSizeBytes;
    }

    public string JellyfinItemId { get; init; }
    public string Title { get; init; }
    public MediaType Type { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }
    public string? ImageUrl { get; init; }
    public long FileSizeBytes { get; init; }

    public void Deconstruct(out string JellyfinItemId, out string Title, out MediaType Type, out int? Year,
        out string? Overview, out string? ImageUrl, out long FileSizeBytes)
    {
        JellyfinItemId = this.JellyfinItemId;
        Title = this.Title;
        Type = this.Type;
        Year = this.Year;
        Overview = this.Overview;
        ImageUrl = this.ImageUrl;
        FileSizeBytes = this.FileSizeBytes;
    }
}
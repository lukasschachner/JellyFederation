namespace JellyFederation.Shared.Models;

public enum MediaType
{
    Movie,
    Series,
    Episode,
    Music,
    Other
}

public class MediaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid ServerId { get; set; }
    public required string JellyfinItemId { get; set; }
    public required string Title { get; set; }
    public MediaType Type { get; set; }
    public int? Year { get; set; }
    public string? Overview { get; set; }
    public string? ImageUrl { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsRequestable { get; set; } = true;
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    public RegisteredServer Server { get; set; } = null!;
}
namespace JellyFederation.Shared.Models;

public class RegisteredServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string OwnerUserId { get; set; }
    public required string ApiKey { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsOnline { get; set; }

    public List<MediaItem> MediaItems { get; set; } = [];
    public List<Invitation> SentInvitations { get; set; } = [];
    public List<Invitation> ReceivedInvitations { get; set; } = [];
}
namespace JellyFederation.Shared.Models;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Declined,
    Revoked
}

public class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid FromServerId { get; set; }
    public required Guid ToServerId { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    public RegisteredServer FromServer { get; set; } = null!;
    public RegisteredServer ToServer { get; set; } = null!;
}
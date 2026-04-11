namespace JellyFederation.Shared.Models;

public enum FileRequestStatus
{
    Pending,
    HolePunching,
    Transferring,
    Completed,
    Failed,
    Cancelled
}

public class FileRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid RequestingServerId { get; set; }
    public required Guid OwningServerId { get; set; }
    public required string JellyfinItemId { get; set; }
    public FileRequestStatus Status { get; set; } = FileRequestStatus.Pending;
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public RegisteredServer RequestingServer { get; set; } = null!;
    public RegisteredServer OwningServer { get; set; } = null!;
}

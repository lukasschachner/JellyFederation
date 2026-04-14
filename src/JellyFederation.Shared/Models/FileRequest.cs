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
    public TransferTransportMode? SelectedTransportMode { get; set; }
    public TransferSelectionReason? TransportSelectionReason { get; set; }
    public TransferFailureCategory? FailureCategory { get; set; }
    public string? FailureReason { get; set; }
    public long BytesTransferred { get; set; }
    public long? TotalBytes { get; set; }
    public DateTime? TransferStartedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public RegisteredServer RequestingServer { get; set; } = null!;
    public RegisteredServer OwningServer { get; set; } = null!;
}
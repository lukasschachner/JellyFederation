using JellyFederation.Shared.Models;

namespace JellyFederation.Shared.Dtos;

public record CreateFileRequestDto
{
    public CreateFileRequestDto(string JellyfinItemId, Guid OwningServerId)
    {
        this.JellyfinItemId = JellyfinItemId;
        this.OwningServerId = OwningServerId;
    }

    public string JellyfinItemId { get; init; }
    public Guid OwningServerId { get; init; }

    public void Deconstruct(out string JellyfinItemId, out Guid OwningServerId)
    {
        JellyfinItemId = this.JellyfinItemId;
        OwningServerId = this.OwningServerId;
    }
}

public record FileRequestDto
{
    public FileRequestDto(Guid Id,
        Guid RequestingServerId,
        string RequestingServerName,
        Guid OwningServerId,
        string OwningServerName,
        string JellyfinItemId,
        string? ItemTitle,
        FileRequestStatus Status,
        TransferTransportMode? SelectedTransportMode,
        TransferFailureCategory? FailureCategory,
        long BytesTransferred,
        long? TotalBytes,
        string? FailureReason,
        ErrorContract? Failure,
        DateTime CreatedAt)
    {
        this.Id = Id;
        this.RequestingServerId = RequestingServerId;
        this.RequestingServerName = RequestingServerName;
        this.OwningServerId = OwningServerId;
        this.OwningServerName = OwningServerName;
        this.JellyfinItemId = JellyfinItemId;
        this.ItemTitle = ItemTitle;
        this.Status = Status;
        this.SelectedTransportMode = SelectedTransportMode;
        this.FailureCategory = FailureCategory;
        this.BytesTransferred = BytesTransferred;
        this.TotalBytes = TotalBytes;
        this.FailureReason = FailureReason;
        this.Failure = Failure;
        this.CreatedAt = CreatedAt;
    }

    public Guid Id { get; init; }
    public Guid RequestingServerId { get; init; }
    public string RequestingServerName { get; init; }
    public Guid OwningServerId { get; init; }
    public string OwningServerName { get; init; }
    public string JellyfinItemId { get; init; }
    public string? ItemTitle { get; init; }
    public FileRequestStatus Status { get; init; }
    public TransferTransportMode? SelectedTransportMode { get; init; }
    public TransferFailureCategory? FailureCategory { get; init; }
    public long BytesTransferred { get; init; }
    public long? TotalBytes { get; init; }
    public string? FailureReason { get; init; }
    public ErrorContract? Failure { get; init; }
    public DateTime CreatedAt { get; init; }

    public void Deconstruct(out Guid Id, out Guid RequestingServerId, out string RequestingServerName,
        out Guid OwningServerId, out string OwningServerName, out string JellyfinItemId, out string? ItemTitle,
        out FileRequestStatus Status, out TransferTransportMode? SelectedTransportMode,
        out TransferFailureCategory? FailureCategory, out long BytesTransferred, out long? TotalBytes,
        out string? FailureReason, out ErrorContract? Failure, out DateTime CreatedAt)
    {
        Id = this.Id;
        RequestingServerId = this.RequestingServerId;
        RequestingServerName = this.RequestingServerName;
        OwningServerId = this.OwningServerId;
        OwningServerName = this.OwningServerName;
        JellyfinItemId = this.JellyfinItemId;
        ItemTitle = this.ItemTitle;
        Status = this.Status;
        SelectedTransportMode = this.SelectedTransportMode;
        FailureCategory = this.FailureCategory;
        BytesTransferred = this.BytesTransferred;
        TotalBytes = this.TotalBytes;
        FailureReason = this.FailureReason;
        Failure = this.Failure;
        CreatedAt = this.CreatedAt;
    }
}

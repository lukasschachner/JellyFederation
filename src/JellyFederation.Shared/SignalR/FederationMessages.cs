using JellyFederation.Shared.Models;

namespace JellyFederation.Shared.SignalR;

/// <summary>
///     Sent by the federation server to both peers to begin hole punching.
///     Each peer should open a UDP socket, bind it, and send keepalive packets
///     to the RemoteEndpoint while simultaneously listening on LocalPort.
/// </summary>
public record HolePunchRequest
{
    /// <summary>
    ///     Sent by the federation server to both peers to begin hole punching.
    ///     Each peer should open a UDP socket, bind it, and send keepalive packets
    ///     to the RemoteEndpoint while simultaneously listening on LocalPort.
    /// </summary>
    public HolePunchRequest(Guid FileRequestId,
        string RemoteEndpoint, // "ip:port" of the peer to punch through to
        int LocalPort, // UDP port the peer should bind on their side
        HolePunchRole Role,
        TransferTransportMode SelectedTransportMode = TransferTransportMode.ArqUdp,
        TransferSelectionReason TransportSelectionReason = TransferSelectionReason.DefaultArq)
    {
        this.FileRequestId = FileRequestId;
        this.RemoteEndpoint = RemoteEndpoint;
        this.LocalPort = LocalPort;
        this.Role = Role;
        this.SelectedTransportMode = SelectedTransportMode;
        this.TransportSelectionReason = TransportSelectionReason;
    }

    public Guid FileRequestId { get; init; }
    public string RemoteEndpoint { get; init; }
    public int LocalPort { get; init; }
    public HolePunchRole Role { get; init; }
    public TransferTransportMode SelectedTransportMode { get; init; }
    public TransferSelectionReason TransportSelectionReason { get; init; }

    public void Deconstruct(out Guid FileRequestId,
        out string RemoteEndpoint, // "ip:port" of the peer to punch through to
        out int LocalPort, // UDP port the peer should bind on their side
        out HolePunchRole Role, out TransferTransportMode SelectedTransportMode,
        out TransferSelectionReason TransportSelectionReason)
    {
        FileRequestId = this.FileRequestId;
        RemoteEndpoint = this.RemoteEndpoint;
        LocalPort = this.LocalPort;
        Role = this.Role;
        SelectedTransportMode = this.SelectedTransportMode;
        TransportSelectionReason = this.TransportSelectionReason;
    }
}

public enum HolePunchRole
{
    Sender, // Will push the file once the hole is punched
    Receiver // Will accept the file once the hole is punched
}

/// <summary>
///     Sent by a plugin to the federation server once it has bound its UDP socket
///     and is ready for hole punching. The server waits for both peers before
///     dispatching HolePunchRequest to each.
/// </summary>
public record HolePunchReady
{
    /// <summary>
    ///     Sent by a plugin to the federation server once it has bound its UDP socket
    ///     and is ready for hole punching. The server waits for both peers before
    ///     dispatching HolePunchRequest to each.
    /// </summary>
    public HolePunchReady(Guid FileRequestId,
        int UdpPort, // The port this plugin bound locally
        string? OverridePublicIp = null, // Optional: override the IP the server uses for this peer
        bool SupportsQuic = false,
        long LargeFileThresholdBytes = 0)
    {
        this.FileRequestId = FileRequestId;
        this.UdpPort = UdpPort;
        this.OverridePublicIp = OverridePublicIp;
        this.SupportsQuic = SupportsQuic;
        this.LargeFileThresholdBytes = LargeFileThresholdBytes;
    }

    public Guid FileRequestId { get; init; }
    public int UdpPort { get; init; }
    public string? OverridePublicIp { get; init; }
    public bool SupportsQuic { get; init; }
    public long LargeFileThresholdBytes { get; init; }

    public void Deconstruct(out Guid FileRequestId, out int UdpPort, // The port this plugin bound locally
        out string? OverridePublicIp, // Optional: override the IP the server uses for this peer
        out bool SupportsQuic, out long LargeFileThresholdBytes)
    {
        FileRequestId = this.FileRequestId;
        UdpPort = this.UdpPort;
        OverridePublicIp = this.OverridePublicIp;
        SupportsQuic = this.SupportsQuic;
        LargeFileThresholdBytes = this.LargeFileThresholdBytes;
    }
}

/// <summary>
///     Sent by a plugin to the federation server to report hole punch outcome.
/// </summary>
public record HolePunchResult
{
    /// <summary>
    ///     Sent by a plugin to the federation server to report hole punch outcome.
    /// </summary>
    public HolePunchResult(Guid FileRequestId,
        bool Success,
        string? Error,
        FailureDescriptor? Failure = null)
    {
        this.FileRequestId = FileRequestId;
        this.Success = Success;
        this.Error = Error;
        this.Failure = Failure;
    }

    public Guid FileRequestId { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public FailureDescriptor? Failure { get; init; }

    public void Deconstruct(out Guid FileRequestId, out bool Success, out string? Error, out FailureDescriptor? Failure)
    {
        FileRequestId = this.FileRequestId;
        Success = this.Success;
        Error = this.Error;
        Failure = this.Failure;
    }
}

/// <summary>
///     Sent by the federation server to both plugins involved in a file request.
///     IsSender=true for the owning server (will send the file),
///     IsSender=false for the requesting server (will receive the file).
/// </summary>
public record FileRequestNotification
{
    /// <summary>
    ///     Sent by the federation server to both plugins involved in a file request.
    ///     IsSender=true for the owning server (will send the file),
    ///     IsSender=false for the requesting server (will receive the file).
    /// </summary>
    public FileRequestNotification(Guid FileRequestId,
        string JellyfinItemId,
        Guid RequestingServerId,
        bool IsSender = true)
    {
        this.FileRequestId = FileRequestId;
        this.JellyfinItemId = JellyfinItemId;
        this.RequestingServerId = RequestingServerId;
        this.IsSender = IsSender;
    }

    public Guid FileRequestId { get; init; }
    public string JellyfinItemId { get; init; }
    public Guid RequestingServerId { get; init; }
    public bool IsSender { get; init; }

    public void Deconstruct(out Guid FileRequestId, out string JellyfinItemId, out Guid RequestingServerId,
        out bool IsSender)
    {
        FileRequestId = this.FileRequestId;
        JellyfinItemId = this.JellyfinItemId;
        RequestingServerId = this.RequestingServerId;
        IsSender = this.IsSender;
    }
}

/// <summary>
///     Sent by the federation server to update a plugin on the status of a file request.
/// </summary>
public record FileRequestStatusUpdate
{
    /// <summary>
    ///     Sent by the federation server to update a plugin on the status of a file request.
    /// </summary>
    public FileRequestStatusUpdate(Guid FileRequestId,
        string Status,
        string? FailureReason,
        ErrorContract? Failure = null,
        TransferTransportMode? SelectedTransportMode = null,
        TransferFailureCategory? FailureCategory = null,
        long? BytesTransferred = null,
        long? TotalBytes = null)
    {
        this.FileRequestId = FileRequestId;
        this.Status = Status;
        this.FailureReason = FailureReason;
        this.Failure = Failure;
        this.SelectedTransportMode = SelectedTransportMode;
        this.FailureCategory = FailureCategory;
        this.BytesTransferred = BytesTransferred;
        this.TotalBytes = TotalBytes;
    }

    public Guid FileRequestId { get; init; }
    public string Status { get; init; }
    public string? FailureReason { get; init; }
    public ErrorContract? Failure { get; init; }
    public TransferTransportMode? SelectedTransportMode { get; init; }
    public TransferFailureCategory? FailureCategory { get; init; }
    public long? BytesTransferred { get; init; }
    public long? TotalBytes { get; init; }

    public void Deconstruct(out Guid FileRequestId, out string Status, out string? FailureReason, out ErrorContract? Failure,
        out TransferTransportMode? SelectedTransportMode, out TransferFailureCategory? FailureCategory,
        out long? BytesTransferred, out long? TotalBytes)
    {
        FileRequestId = this.FileRequestId;
        Status = this.Status;
        FailureReason = this.FailureReason;
        Failure = this.Failure;
        SelectedTransportMode = this.SelectedTransportMode;
        FailureCategory = this.FailureCategory;
        BytesTransferred = this.BytesTransferred;
        TotalBytes = this.TotalBytes;
    }
}

/// <summary>
///     Sent by the receiving plugin to the federation server to report transfer progress.
///     The server forwards this to web browser clients watching the request.
/// </summary>
public record TransferProgress
{
    /// <summary>
    ///     Sent by the receiving plugin to the federation server to report transfer progress.
    ///     The server forwards this to web browser clients watching the request.
    /// </summary>
    public TransferProgress(Guid FileRequestId,
        long BytesReceived,
        long TotalBytes)
    {
        this.FileRequestId = FileRequestId;
        this.BytesReceived = BytesReceived;
        this.TotalBytes = TotalBytes;
    }

    public Guid FileRequestId { get; init; }
    public long BytesReceived { get; init; }
    public long TotalBytes { get; init; }

    public void Deconstruct(out Guid FileRequestId, out long BytesReceived, out long TotalBytes)
    {
        FileRequestId = this.FileRequestId;
        BytesReceived = this.BytesReceived;
        TotalBytes = this.TotalBytes;
    }
}

/// <summary>
///     Sent by the federation server to both plugins to abort an in-progress transfer.
/// </summary>
public record CancelTransfer
{
    /// <summary>
    ///     Sent by the federation server to both plugins to abort an in-progress transfer.
    /// </summary>
    public CancelTransfer(Guid FileRequestId)
    {
        this.FileRequestId = FileRequestId;
    }

    public Guid FileRequestId { get; init; }

    public void Deconstruct(out Guid FileRequestId)
    {
        FileRequestId = this.FileRequestId;
    }
}

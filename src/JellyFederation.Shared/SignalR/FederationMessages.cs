namespace JellyFederation.Shared.SignalR;

/// <summary>
/// Sent by the federation server to both peers to begin hole punching.
/// Each peer should open a UDP socket, bind it, and send keepalive packets
/// to the RemoteEndpoint while simultaneously listening on LocalPort.
/// </summary>
public record HolePunchRequest(
    Guid FileRequestId,
    string RemoteEndpoint,   // "ip:port" of the peer to punch through to
    int LocalPort,           // UDP port the peer should bind on their side
    HolePunchRole Role);

public enum HolePunchRole
{
    Sender,    // Will push the file once the hole is punched
    Receiver   // Will accept the file once the hole is punched
}

/// <summary>
/// Sent by a plugin to the federation server once it has bound its UDP socket
/// and is ready for hole punching. The server waits for both peers before
/// dispatching HolePunchRequest to each.
/// </summary>
public record HolePunchReady(
    Guid FileRequestId,
    int UdpPort,                   // The port this plugin bound locally
    string? OverridePublicIp = null); // Optional: override the IP the server uses for this peer

/// <summary>
/// Sent by a plugin to the federation server to report hole punch outcome.
/// </summary>
public record HolePunchResult(
    Guid FileRequestId,
    bool Success,
    string? Error);

/// <summary>
/// Sent by the federation server to both plugins involved in a file request.
/// IsSender=true for the owning server (will send the file),
/// IsSender=false for the requesting server (will receive the file).
/// </summary>
public record FileRequestNotification(
    Guid FileRequestId,
    string JellyfinItemId,
    Guid RequestingServerId,
    bool IsSender = true);

/// <summary>
/// Sent by the federation server to update a plugin on the status of a file request.
/// </summary>
public record FileRequestStatusUpdate(
    Guid FileRequestId,
    string Status,
    string? FailureReason);

/// <summary>
/// Sent by the receiving plugin to the federation server to report transfer progress.
/// The server forwards this to web browser clients watching the request.
/// </summary>
public record TransferProgress(
    Guid FileRequestId,
    long BytesReceived,
    long TotalBytes);

/// <summary>
/// Sent by the federation server to both plugins to abort an in-progress transfer.
/// </summary>
public record CancelTransfer(Guid FileRequestId);

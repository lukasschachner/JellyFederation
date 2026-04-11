using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Implements UDP hole punching and then hands the established connection
/// to the file transfer service.
///
/// Protocol:
///   1. Plugin binds a UDP socket on an ephemeral port.
///   2. Plugin reports the port to the federation server (HolePunchReady).
///   3. Federation server tells both peers the other's public endpoint.
///   4. Both sides repeatedly send UDP probes to the remote endpoint
///      while listening for an incoming probe — this punches through NAT.
///   5. Once a probe is received, hole is established; file transfer begins.
/// </summary>
public class HolePunchService(
    ILogger<HolePunchService> logger,
    FileTransferService fileTransfer)
{
    private const int ProbeIntervalMs = 200;
    private const int PunchTimeoutMs = 15_000;
    private const int ProbePayload = 0x4A46; // "JF" magic

    /// <summary>
    /// Called when the owning server receives a FileRequestNotification.
    /// Binds a UDP socket and tells the federation server we're ready.
    /// </summary>
    public async Task PrepareAndSignalReadyAsync(
        FileRequestNotification notification,
        HubConnection connection)
    {
        var config = FederationPlugin.Instance!.Configuration;

        // Reuse existing socket if we already prepared for this request (reconnect scenario)
        int localPort;
        if (PendingSockets.TryGetValue(notification.FileRequestId, out var existing))
        {
            localPort = ((IPEndPoint)existing.Socket.LocalEndPoint!).Port;
            logger.LogInformation("Reusing existing UDP socket on port {Port} for file request {Id}",
                localPort, notification.FileRequestId);
        }
        else
        {
            var bindPort = config.HolePunchPort > 0 ? config.HolePunchPort : 0;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, bindPort));
            }
            catch (SocketException ex) when (bindPort != 0)
            {
                logger.LogWarning(ex,
                    "Configured port {Port} is already in use for request {Id} — falling back to ephemeral port",
                    bindPort, notification.FileRequestId);
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            }
            localPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
            logger.LogInformation("Bound UDP socket on port {Port} for file request {Id}",
                localPort, notification.FileRequestId);
            PendingSockets[notification.FileRequestId] = (socket, notification.JellyfinItemId, notification.IsSender);
        }

        var overrideIp = string.IsNullOrWhiteSpace(config.OverridePublicIp) ? null : config.OverridePublicIp;
        await connection.SendAsync("ReportHolePunchReady",
            new HolePunchReady(notification.FileRequestId, localPort, overrideIp));
    }

    /// <summary>
    /// Called when the requesting server is told to begin a hole punch.
    /// Also called for the owning server (Sender role).
    /// </summary>
    public async Task ExecuteAsync(HolePunchRequest request, HubConnection connection)
    {
        Socket socket;
        string? jellyfinItemId = null;
        bool isSender = request.Role == HolePunchRole.Sender;

        if (PendingSockets.TryRemove(request.FileRequestId, out var pending))
        {
            socket = pending.Socket;
            jellyfinItemId = pending.JellyfinItemId;
        }
        else
        {
            // Receiver side: bind a fresh socket on the assigned local port
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, request.LocalPort));
        }

        var parts = request.RemoteEndpoint.Split(':');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var remoteIp) ||
            !int.TryParse(parts[1], out var remotePort))
        {
            logger.LogError("Invalid remote endpoint: {Ep}", request.RemoteEndpoint);
            await connection.SendAsync("ReportHolePunchResult",
                new HolePunchResult(request.FileRequestId, false, "Invalid remote endpoint"));
            return;
        }

        var remoteEp = new IPEndPoint(remoteIp, remotePort);
        logger.LogInformation("Starting hole punch to {Ep} (role: {Role})", remoteEp, request.Role);

        using var cts = new CancellationTokenSource(PunchTimeoutMs);
        var probe = BitConverter.GetBytes(ProbePayload);

        try
        {
            // Punch loop: send probes and listen concurrently.
            // We only need to wait for recvTask — once a probe arrives the hole is open.
            // Cancel the send loop immediately after to avoid waiting the full timeout.
            var sendTask = SendProbesAsync(socket, remoteEp, probe, cts.Token);
            var recvTask = WaitForProbeAsync(socket, probe, remoteEp, cts.Token);

            await recvTask; // throws OperationCanceledException if 15s elapses
            cts.Cancel();   // stop sending probes now that hole is open
            try { await sendTask; } catch (OperationCanceledException) { }

            logger.LogInformation("Hole punched successfully to {Ep}", remoteEp);
            await connection.SendAsync("ReportHolePunchResult",
                new HolePunchResult(request.FileRequestId, true, null));

            // Hand off to file transfer
            var config = FederationPlugin.Instance!.Configuration;
            if (isSender && jellyfinItemId is not null)
                await fileTransfer.SendFileAsync(request.FileRequestId, jellyfinItemId, socket, remoteEp, config);
            else if (!isSender)
                await fileTransfer.ReceiveFileAsync(request.FileRequestId, socket, remoteEp, config, connection);
        }
        catch (OperationCanceledException)
        {
            const string error = "Timed out. The peer may be behind symmetric NAT — "
                + "port forwarding is required for direct transfers in this case.";
            logger.LogWarning("Hole punch timed out for request {Id}", request.FileRequestId);
            try
            {
                await connection.SendAsync("ReportHolePunchResult",
                    new HolePunchResult(request.FileRequestId, false, error));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send HolePunchResult for request {Id} — connection may be closed", request.FileRequestId);
            }
            socket.Dispose();
        }
    }

    private static async Task SendProbesAsync(
        Socket socket, IPEndPoint remote, byte[] probe, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await socket.SendToAsync(probe, SocketFlags.None, remote, ct); }
            catch (SocketException) { /* remote not yet reachable — keep trying */ }
            await Task.Delay(ProbeIntervalMs, ct);
        }
    }

    private async Task WaitForProbeAsync(
        Socket socket, byte[] expectedProbe, IPEndPoint remoteEp, CancellationToken ct)
    {
        var buffer = new byte[256];
        while (!ct.IsCancellationRequested)
        {
            var received = await socket.ReceiveAsync(buffer, SocketFlags.None, ct);
            if (received == expectedProbe.Length &&
                buffer.AsSpan(0, received).SequenceEqual(expectedProbe))
            {
                logger.LogInformation("Received valid probe from {Ep} ({Bytes} bytes)", remoteEp, received);
                return;
            }
            logger.LogDebug("Received {Bytes} bytes (not a valid probe)", received);
        }
    }

    public void Cancel(Guid fileRequestId)
    {
        // Cancel ongoing transfer (covers both send and receive)
        fileTransfer.Cancel(fileRequestId);
        // Clean up any pending socket waiting for HolePunchRequest
        if (PendingSockets.TryRemove(fileRequestId, out var pending))
            pending.Socket.Dispose();
    }

    // Temporary storage for sockets bound before HolePunchRequest arrives (owner side)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary
        <Guid, (Socket Socket, string JellyfinItemId, bool IsSender)> PendingSockets = new();
}

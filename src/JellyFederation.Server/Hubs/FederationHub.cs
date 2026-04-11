using JellyFederation.Server.Data;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace JellyFederation.Server.Hubs;

/// <summary>
/// Main SignalR hub. Plugins connect here for:
///   - Presence (online/offline tracking)
///   - Hole punch signaling (rendezvous before P2P transfer)
///   - File request notifications
/// </summary>
public class FederationHub(
    FederationDbContext db,
    ServerConnectionTracker tracker,
    ILogger<FederationHub> logger) : Hub
{
    // Called by plugin on connect (after setting API key as query param)
    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var apiKey = http?.Request.Query["apiKey"].ToString();
        if (string.IsNullOrEmpty(apiKey))
        {
            Context.Abort();
            return;
        }

        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null)
        {
            Context.Abort();
            return;
        }

        // Web browser clients pass client=web — add to a group for live updates but don't
        // register in the tracker (that would stomp the real plugin connection).
        // Plugin connections (any other value, including absent) register normally.
        var clientType = http?.Request.Query["client"].ToString();
        if (clientType == "web")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"server:{server.Id}");
            logger.LogDebug("Web client for server {Name} ({Id}) connected", server.Name, server.Id);
        }
        else
        {
            var publicIp = GetPublicIp();
            tracker.Register(server.Id, Context.ConnectionId, publicIp);

            server.IsOnline = true;
            server.LastSeenAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation("Server {Name} ({Id}) connected from {Ip}", server.Name, server.Id, publicIp);

            // Re-send any Pending file request notifications this server may have missed while offline
            await ResendPendingNotificationsAsync(server);
        }

        await base.OnConnectedAsync();
    }

    private async Task ResendPendingNotificationsAsync(RegisteredServer server)
    {
        var pending = await db.FileRequests
            .Where(r => (r.Status == FileRequestStatus.Pending || r.Status == FileRequestStatus.HolePunching) &&
                        (r.OwningServerId == server.Id || r.RequestingServerId == server.Id))
            .ToListAsync();

        foreach (var req in pending)
        {
            bool isSender = req.OwningServerId == server.Id;
            logger.LogInformation(
                "Re-sending FileRequestNotification for request {Id} to {Name} (isSender={IsSender})",
                req.Id, server.Name, isSender);
            try
            {
                await Clients.Caller.SendAsync(
                    "FileRequestNotification",
                    new FileRequestNotification(req.Id, req.JellyfinItemId, req.RequestingServerId, isSender));
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to resend notification for {Id} to {Name}: {Err}",
                    req.Id, server.Name, ex.Message);
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var serverId = tracker.GetServerId(Context.ConnectionId);
        tracker.Unregister(Context.ConnectionId);

        if (serverId is not null)
        {
            var server = await db.Servers.FindAsync(serverId);
            if (server is not null)
            {
                server.IsOnline = false;
                await db.SaveChangesAsync();
                logger.LogInformation("Server {Name} ({Id}) disconnected", server.Name, server.Id);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Plugin reports it has bound a UDP socket and is ready for hole punching.
    /// Once both peers check in, the server dispatches HolePunchRequest to each.
    /// </summary>
    public async Task ReportHolePunchReady(HolePunchReady message)
    {
        try
        {
            var serverId = tracker.GetServerId(Context.ConnectionId);
            if (serverId is null) return;

            var request = await db.FileRequests.FindAsync(message.FileRequestId);
            if (request is null)
            {
                logger.LogWarning("ReportHolePunchReady: file request {Id} not found — ignoring", message.FileRequestId);
                return;
            }

            logger.LogInformation(
                "ReportHolePunchReady from server {ServerId} for request {RequestId} on port {Port}",
                serverId, message.FileRequestId, message.UdpPort);

            // Use plugin-supplied override IP if provided (e.g. Docker/NAT scenarios)
            if (!string.IsNullOrWhiteSpace(message.OverridePublicIp) &&
                IPAddress.TryParse(message.OverridePublicIp, out var overrideIp))
            {
                tracker.SetPublicIpOverride(Context.ConnectionId, overrideIp);
                logger.LogInformation("Using override public IP {Ip} for {ServerId}", overrideIp, serverId);
            }

            if (tracker.TryAddHolePunchReady(
                message.FileRequestId,
                serverId.Value,
                Context.ConnectionId,
                message.UdpPort,
                out var candidates))
            {
                await DispatchHolePunch(request, candidates);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in ReportHolePunchReady for request {Id}", message.FileRequestId);
        }
    }

    /// <summary>
    /// Plugin reports the outcome of the hole punch attempt.
    /// </summary>
    public async Task ReportHolePunchResult(HolePunchResult result)
    {
        var request = await db.FileRequests.FindAsync(result.FileRequestId);
        if (request is null) return;

        if (result.Success)
        {
            request.Status = FileRequestStatus.Transferring;
        }
        else
        {
            request.Status = FileRequestStatus.Failed;
            request.FailureReason = $"Hole punch failed: {result.Error}. "
                + "The server may be behind symmetric NAT. Consider configuring port forwarding.";
        }

        await db.SaveChangesAsync();

        // Notify both sides of the status update
        await NotifyFileRequestStatus(request);
    }

    private async Task DispatchHolePunch(FileRequest request, HolePunchCandidate[] candidates)
    {
        // Identify which candidate is sender (owner) and which is receiver (requester)
        var sender = candidates.FirstOrDefault(c => c.ServerId == request.OwningServerId);
        var receiver = candidates.FirstOrDefault(c => c.ServerId == request.RequestingServerId);

        if (sender is null || receiver is null)
        {
            logger.LogWarning("Could not match candidates to file request {Id}", request.Id);
            return;
        }

        var senderIp = tracker.GetPublicIp(sender.ConnectionId);
        var receiverIp = tracker.GetPublicIp(receiver.ConnectionId);

        if (senderIp is null || receiverIp is null) return;

        request.Status = FileRequestStatus.HolePunching;
        await db.SaveChangesAsync();

        // Tell sender: punch to receiver's public UDP endpoint
        await Clients.Client(sender.ConnectionId).SendAsync(
            "HolePunchRequest",
            new HolePunchRequest(
                request.Id,
                RemoteEndpoint: $"{receiverIp}:{receiver.UdpPort}",
                LocalPort: sender.UdpPort,
                Role: HolePunchRole.Sender));

        // Tell receiver: punch to sender's public UDP endpoint
        await Clients.Client(receiver.ConnectionId).SendAsync(
            "HolePunchRequest",
            new HolePunchRequest(
                request.Id,
                RemoteEndpoint: $"{senderIp}:{sender.UdpPort}",
                LocalPort: receiver.UdpPort,
                Role: HolePunchRole.Receiver));

        logger.LogInformation(
            "Hole punch initiated for request {Id}: {SenderIp}:{SenderPort} <-> {ReceiverIp}:{ReceiverPort}",
            request.Id, senderIp, sender.UdpPort, receiverIp, receiver.UdpPort);
    }

    /// <summary>
    /// Plugin (receiver) reports bytes transferred so the dashboard can show a progress bar.
    /// </summary>
    public async Task ReportTransferProgress(TransferProgress progress)
    {
        var request = await db.FileRequests.FindAsync(progress.FileRequestId);
        if (request is null) return;

        // Forward to browser clients watching either server
        await Clients.Group($"server:{request.OwningServerId}").SendAsync("TransferProgress", progress);
        await Clients.Group($"server:{request.RequestingServerId}").SendAsync("TransferProgress", progress);
    }

    private async Task NotifyFileRequestStatus(FileRequest request)
    {
        var update = new FileRequestStatusUpdate(
            request.Id,
            request.Status.ToString(),
            request.FailureReason);

        // Notify plugin connections (for transfer logic)
        var senderConn = tracker.GetConnectionId(request.OwningServerId);
        var receiverConn = tracker.GetConnectionId(request.RequestingServerId);

        if (senderConn is not null)
            await Clients.Client(senderConn).SendAsync("FileRequestStatusUpdate", update);

        if (receiverConn is not null)
            await Clients.Client(receiverConn).SendAsync("FileRequestStatusUpdate", update);

        // Notify browser clients watching either server's dashboard
        await Clients.Group($"server:{request.OwningServerId}").SendAsync("FileRequestStatusUpdate", update);
        await Clients.Group($"server:{request.RequestingServerId}").SendAsync("FileRequestStatusUpdate", update);
    }

    private IPAddress GetPublicIp()
    {
        var http = Context.GetHttpContext();
        var forwarded = http?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded) &&
            IPAddress.TryParse(forwarded.Split(',')[0].Trim(), out var fwdIp))
            return fwdIp;

        return http?.Connection.RemoteIpAddress ?? IPAddress.Loopback;
    }
}

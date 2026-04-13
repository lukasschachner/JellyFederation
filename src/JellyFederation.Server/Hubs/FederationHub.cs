using JellyFederation.Server.Data;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Diagnostics;

namespace JellyFederation.Server.Hubs;

/// <summary>
/// Main SignalR hub. Plugins connect here for:
///   - Presence (online/offline tracking)
///   - Hole punch signaling (rendezvous before P2P transfer)
///   - File request notifications
/// </summary>
public partial class FederationHub(
    FederationDbContext db,
    ServerConnectionTracker tracker,
    FileRequestNotifier notifier,
    ILogger<FederationHub> logger) : Hub
{
    /// <summary>
    /// Resolves the server from the query-string API key.
    /// Returns null if the key is missing or invalid.
    /// </summary>
    private async Task<RegisteredServer?> AuthenticateAsync()
    {
        var http = Context.GetHttpContext();
        var apiKey = http?.Request.Query["apiKey"].ToString();
        if (string.IsNullOrEmpty(apiKey))
        {
            LogHubAuthenticationMissingApiKey(logger, Context.ConnectionId);
            return null;
        }

        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null)
            LogHubAuthenticationFailed(logger, Context.ConnectionId);
        return server;
    }

    // Called by plugin on connect (after setting API key as query param)
    public override async Task OnConnectedAsync()
    {
        var server = await AuthenticateAsync();
        if (server is null)
        {
            LogHubConnectionAbortedUnauthenticated(logger, Context.ConnectionId);
            Context.Abort();
            return;
        }

        var http = Context.GetHttpContext();

        // Web browser clients pass client=web — add to a group for live updates but don't
        // register in the tracker (that would stomp the real plugin connection).
        // Plugin connections (any other value, including absent) register normally.
        var clientType = http?.Request.Query["client"].ToString();
        if (clientType == "web")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"server:{server.Id}");
            LogWebClientConnected(logger, server.Name, server.Id);
        }
        else
        {
            var publicIp = GetPublicIp();
            tracker.Register(server.Id, Context.ConnectionId, publicIp);

            server.IsOnline = true;
            server.LastSeenAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            LogServerConnected(logger, server.Name, server.Id, publicIp);

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
            LogResendingNotification(logger, req.Id, server.Name, isSender);
            try
            {
                await Clients.Caller.SendAsync(
                    "FileRequestNotification",
                    new FileRequestNotification(req.Id, req.JellyfinItemId, req.RequestingServerId, isSender));
            }
            catch (Exception ex)
            {
                LogResendNotificationFailed(logger, req.Id, server.Name, ex.Message);
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
            LogDisconnectedWithException(logger, Context.ConnectionId, exception);

        var serverId = tracker.GetServerId(Context.ConnectionId);
        tracker.Unregister(Context.ConnectionId);

        if (serverId is not null)
        {
            var server = await db.Servers.FindAsync(serverId);
            if (server is not null)
            {
                server.IsOnline = false;
                await db.SaveChangesAsync();
                LogServerDisconnected(logger, server.Name, server.Id);
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
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "holepunch.ready", "server", correlationId, releaseVersion: "server");

        try
        {
            var serverId = tracker.GetServerId(Context.ConnectionId);
            if (serverId is null)
            {
                LogHolePunchReadyUnknownConnection(logger, Context.ConnectionId, message.FileRequestId);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
                return;
            }

            var request = await db.FileRequests.FindAsync(message.FileRequestId);
            if (request is null)
            {
                LogHolePunchReadyNotFound(logger, message.FileRequestId);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
                return;
            }

            LogHolePunchReady(logger, serverId.Value, message.FileRequestId, message.UdpPort);
            LogHolePunchCapabilities(
                logger,
                serverId.Value,
                message.FileRequestId,
                message.SupportsQuic,
                message.LargeFileThresholdBytes,
                string.IsNullOrWhiteSpace(message.OverridePublicIp) ? "(auto)" : message.OverridePublicIp);

            // Use plugin-supplied override IP if provided (e.g. Docker/NAT scenarios)
            if (!string.IsNullOrWhiteSpace(message.OverridePublicIp) &&
                IPAddress.TryParse(message.OverridePublicIp, out var overrideIp))
            {
                tracker.SetPublicIpOverride(Context.ConnectionId, overrideIp);
                LogOverrideIp(logger, overrideIp, serverId.Value);
            }

            if (tracker.TryAddHolePunchReady(
                message.FileRequestId,
                serverId.Value,
                Context.ConnectionId,
                message.UdpPort,
                message.SupportsQuic,
                message.LargeFileThresholdBytes,
                out var candidates))
            {
                await DispatchHolePunch(request, candidates);
            }

            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
            FederationMetrics.RecordOperation("holepunch.ready", "server", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed);
        }
        catch (Exception ex)
        {
            LogHolePunchReadyError(logger, ex, message.FileRequestId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, ex);
            FederationMetrics.RecordOperation("holepunch.ready", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
        }
    }

    /// <summary>
    /// Plugin reports the outcome of the hole punch attempt.
    /// </summary>
    public async Task ReportHolePunchResult(HolePunchResult result)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "holepunch.result", "server", correlationId, releaseVersion: "server");

        var request = await db.FileRequests.FindAsync(result.FileRequestId);
        if (request is null)
        {
            LogHolePunchResultNotFound(logger, result.FileRequestId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            return;
        }

        LogHolePunchResultReceived(logger, result.FileRequestId, result.Success, result.Error ?? string.Empty);

        if (result.Success)
        {
            request.Status = FileRequestStatus.Transferring;
            request.TransferStartedAt = DateTime.UtcNow;
            request.FailureCategory = null;
            LogTransferStarted(logger, result.FileRequestId);
        }
        else
        {
            request.Status = FileRequestStatus.Failed;
            request.FailureCategory = TransferFailureCategory.Connectivity;
            request.FailureReason = $"Hole punch failed: {result.Error}. "
                + "The server may be behind symmetric NAT. Consider configuring port forwarding.";
            LogHolePunchMarkedFailed(logger, result.FileRequestId, request.FailureReason);
        }

        await db.SaveChangesAsync();

        // Notify both sides of the status update via the shared notifier
        await notifier.NotifyStatusAsync(request);
        var outcome = result.Success ? FederationTelemetry.OutcomeSuccess : FederationTelemetry.OutcomeError;
        FederationTelemetry.SetOutcome(activity, outcome);
        FederationMetrics.RecordOperation("holepunch.result", "server", outcome, startedAt.Elapsed);
    }

    private async Task DispatchHolePunch(FileRequest request, HolePunchCandidate[] candidates)
    {
        // Identify which candidate is sender (owner) and which is receiver (requester)
        var sender = candidates.FirstOrDefault(c => c.ServerId == request.OwningServerId);
        var receiver = candidates.FirstOrDefault(c => c.ServerId == request.RequestingServerId);

        if (sender is null || receiver is null)
        {
            LogCandidateMatchFailed(logger, request.Id);
            return;
        }

        var senderIp = tracker.GetPublicIp(sender.ConnectionId);
        var receiverIp = tracker.GetPublicIp(receiver.ConnectionId);

        if (senderIp is null || receiverIp is null)
        {
            LogHolePunchMissingPublicIp(logger, request.Id, sender.ConnectionId, receiver.ConnectionId);
            return;
        }

        var selection = await SelectTransportModeAsync(request, sender, receiver);
        request.SelectedTransportMode = selection.Mode;
        request.TransportSelectionReason = selection.Reason;

        request.Status = FileRequestStatus.HolePunching;
        await db.SaveChangesAsync();

        // Tell sender: punch to receiver's public UDP endpoint
        await Clients.Client(sender.ConnectionId).SendAsync(
            "HolePunchRequest",
            new HolePunchRequest(
                request.Id,
                RemoteEndpoint: $"{receiverIp}:{receiver.UdpPort}",
                LocalPort: sender.UdpPort,
                Role: HolePunchRole.Sender,
                SelectedTransportMode: selection.Mode,
                TransportSelectionReason: selection.Reason));

        // Tell receiver: punch to sender's public UDP endpoint
        await Clients.Client(receiver.ConnectionId).SendAsync(
            "HolePunchRequest",
            new HolePunchRequest(
                request.Id,
                RemoteEndpoint: $"{senderIp}:{sender.UdpPort}",
                LocalPort: receiver.UdpPort,
                Role: HolePunchRole.Receiver,
                SelectedTransportMode: selection.Mode,
                TransportSelectionReason: selection.Reason));

        LogHolePunchInitiated(logger, request.Id, senderIp, sender.UdpPort, receiverIp, receiver.UdpPort);
        LogTransportModeSelected(logger, request.Id, selection.Mode, selection.Reason);
        FederationMetrics.RecordOperation(
            $"file.transfer.mode.{selection.Mode.ToString().ToLowerInvariant()}",
            "server",
            "selected",
            TimeSpan.Zero);
    }

    private async Task<TransferSelection> SelectTransportModeAsync(
        FileRequest request,
        HolePunchCandidate sender,
        HolePunchCandidate receiver)
    {
        if (!sender.SupportsQuic || !receiver.SupportsQuic)
            return new(TransferTransportMode.ArqUdp, TransferSelectionReason.QuicUnsupportedPeer);

        var item = await db.MediaItems
            .Where(m => m.ServerId == request.OwningServerId && m.JellyfinItemId == request.JellyfinItemId)
            .Select(m => new { m.FileSizeBytes })
            .FirstOrDefaultAsync();

        if (item is null)
            return new(TransferTransportMode.ArqUdp, TransferSelectionReason.NegotiationFailed);

        request.TotalBytes = item.FileSizeBytes;

        var threshold = Math.Max(sender.LargeFileThresholdBytes, receiver.LargeFileThresholdBytes);
        if (item.FileSizeBytes >= threshold)
            return new(TransferTransportMode.Quic, TransferSelectionReason.LargeFileQuic);

        return new(TransferTransportMode.ArqUdp, TransferSelectionReason.DefaultArq);
    }

    /// <summary>
    /// Plugin (receiver) reports bytes transferred so the dashboard can show a progress bar.
    /// </summary>
    public async Task ReportTransferProgress(TransferProgress progress)
    {
        var request = await db.FileRequests.FindAsync(progress.FileRequestId);
        if (request is null)
        {
            LogTransferProgressRequestNotFound(logger, progress.FileRequestId, progress.BytesReceived, progress.TotalBytes);
            return;
        }

        request.BytesTransferred = progress.BytesReceived;
        request.TotalBytes = progress.TotalBytes;
        await db.SaveChangesAsync();

        // Forward to browser clients watching either server
        await Clients.Group($"server:{request.OwningServerId}").SendAsync("TransferProgress", progress);
        await Clients.Group($"server:{request.RequestingServerId}").SendAsync("TransferProgress", progress);
        LogTransferProgressForwarded(logger, request.Id, request.OwningServerId, request.RequestingServerId, progress.BytesReceived, progress.TotalBytes);
    }

    private IPAddress GetPublicIp()
    {
        var http = Context.GetHttpContext();
        // Rely on ForwardedHeaders middleware (configured in Program.cs) which
        // only trusts known proxies and sets RemoteIpAddress correctly.
        return http?.Connection.RemoteIpAddress ?? IPAddress.Loopback;
    }
}

using System.Diagnostics;
using System.Net;
using JellyFederation.Data;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Hubs;

/// <summary>
///     Main SignalR hub. Plugins connect here for:
///     - Presence (online/offline tracking)
///     - Hole punch signaling (rendezvous before P2P transfer)
///     - File request notifications
/// </summary>
public partial class FederationHub : Hub
{
    private readonly FederationDbContext _db;
    private readonly ILogger<FederationHub> _logger;
    private readonly FileRequestNotifier _notifier;
    private readonly ServerConnectionTracker _tracker;

    /// <summary>
    ///     Main SignalR hub. Plugins connect here for:
    ///     - Presence (online/offline tracking)
    ///     - Hole punch signaling (rendezvous before P2P transfer)
    ///     - File request notifications
    /// </summary>
    public FederationHub(FederationDbContext db,
        ServerConnectionTracker tracker,
        FileRequestNotifier notifier,
        ILogger<FederationHub> logger)
    {
        _db = db;
        _tracker = tracker;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    ///     Resolves the server from the query-string API key.
    ///     Returns null if the key is missing or invalid.
    /// </summary>
    private async Task<RegisteredServer?> AuthenticateAsync()
    {
        var http = Context.GetHttpContext();
        var apiKey = http?.Request.Query["apiKey"].ToString();
        if (string.IsNullOrEmpty(apiKey))
        {
            LogHubAuthenticationMissingApiKey(_logger, Context.ConnectionId);
            return null;
        }

        var server = await _db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey).ConfigureAwait(false);
        if (server is null)
            LogHubAuthenticationFailed(_logger, Context.ConnectionId);
        return server;
    }

    // Called by plugin on connect (after setting API key as query param)
    public override async Task OnConnectedAsync()
    {
        var server = await AuthenticateAsync().ConfigureAwait(false);
        if (server is null)
        {
            LogHubConnectionAbortedUnauthenticated(_logger, Context.ConnectionId);
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
            await Groups.AddToGroupAsync(Context.ConnectionId, $"server:{server.Id}").ConfigureAwait(false);
            LogWebClientConnected(_logger, server.Name, server.Id);
        }
        else
        {
            var publicIp = GetPublicIp();
            _tracker.Register(server.Id, Context.ConnectionId, publicIp);

            server.IsOnline = true;
            server.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync().ConfigureAwait(false);

            LogServerConnected(_logger, server.Name, server.Id, publicIp);

            // Re-send any Pending file request notifications this server may have missed while offline
            await ResendPendingNotificationsAsync(server).ConfigureAwait(false);
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    private async Task ResendPendingNotificationsAsync(RegisteredServer server)
    {
        var pending = await _db.FileRequests
            .Where(r => (r.Status == FileRequestStatus.Pending || r.Status == FileRequestStatus.HolePunching) &&
                        (r.OwningServerId == server.Id || r.RequestingServerId == server.Id))
            .ToListAsync().ConfigureAwait(false);

        foreach (var req in pending)
        {
            var isSender = req.OwningServerId == server.Id;
            LogResendingNotification(_logger, req.Id, server.Name, isSender);
            try
            {
                await Clients.Caller.SendAsync(
                        "FileRequestNotification",
                        new FileRequestNotification(req.Id, req.JellyfinItemId, req.RequestingServerId, isSender))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogResendNotificationFailed(_logger, req.Id, server.Name, ex.Message);
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
            LogDisconnectedWithException(_logger, Context.ConnectionId, exception);

        var serverId = _tracker.GetServerId(Context.ConnectionId);
        _tracker.Unregister(Context.ConnectionId);

        if (serverId is not null)
        {
            var server = await _db.Servers.FindAsync(serverId).ConfigureAwait(false);
            if (server is not null)
            {
                server.IsOnline = false;
                await _db.SaveChangesAsync().ConfigureAwait(false);
                LogServerDisconnected(_logger, server.Name, server.Id);
            }
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    ///     Plugin reports it has bound a UDP socket and is ready for hole punching.
    ///     Once both peers check in, the server dispatches HolePunchRequest to each.
    /// </summary>
    public async Task ReportHolePunchReady(HolePunchReady message)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "holepunch.ready", "server", correlationId,
            releaseVersion: "server");

        try
        {
            var serverId = _tracker.GetServerId(Context.ConnectionId);
            if (serverId is null)
            {
                LogHolePunchReadyUnknownConnection(_logger, Context.ConnectionId, message.FileRequestId);
                var failure = FailureDescriptor.Authorization(
                    "holepunch.ready.unauthorized_connection",
                    "Connection is not associated with a registered server.",
                    correlationId);
                LogWorkflowFailureDescriptor(_logger, message.FileRequestId, failure.Code, failure.Category.ToString(),
                    failure.Message);
                FederationTelemetry.SetFailure(activity, failure);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
                FederationMetrics.RecordOperation("holepunch.ready", "server", FederationTelemetry.OutcomeError,
                    startedAt.Elapsed, failureCategory: failure.Category.ToString(), failureCode: failure.Code);
                return;
            }

            var request = await _db.FileRequests.FindAsync(message.FileRequestId).ConfigureAwait(false);
            if (request is null)
            {
                LogHolePunchReadyNotFound(_logger, message.FileRequestId);
                var failure = FailureDescriptor.NotFound(
                    "holepunch.ready.request_not_found",
                    "File request not found.",
                    correlationId);
                LogWorkflowFailureDescriptor(_logger, message.FileRequestId, failure.Code, failure.Category.ToString(),
                    failure.Message);
                FederationTelemetry.SetFailure(activity, failure);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
                FederationMetrics.RecordOperation("holepunch.ready", "server", FederationTelemetry.OutcomeError,
                    startedAt.Elapsed, failureCategory: failure.Category.ToString(), failureCode: failure.Code);
                return;
            }

            LogHolePunchReady(_logger, serverId.Value, message.FileRequestId, message.UdpPort);
            LogHolePunchCapabilities(
                _logger,
                serverId.Value,
                message.FileRequestId,
                message.SupportsQuic,
                message.LargeFileThresholdBytes,
                string.IsNullOrWhiteSpace(message.OverridePublicIp) ? "(auto)" : message.OverridePublicIp);

            // Use plugin-supplied override IP if provided (e.g. Docker/NAT scenarios)
            if (!string.IsNullOrWhiteSpace(message.OverridePublicIp) &&
                IPAddress.TryParse(message.OverridePublicIp, out var overrideIp))
            {
                _tracker.SetPublicIpOverride(Context.ConnectionId, overrideIp);
                LogOverrideIp(_logger, overrideIp, serverId.Value);
            }

            if (_tracker.TryAddHolePunchReady(
                    message.FileRequestId,
                    serverId.Value,
                    Context.ConnectionId,
                    message.UdpPort,
                    message.SupportsQuic,
                    message.LargeFileThresholdBytes,
                    out var candidates))
                await DispatchHolePunch(request, candidates).ConfigureAwait(false);

            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
            FederationMetrics.RecordOperation("holepunch.ready", "server", FederationTelemetry.OutcomeSuccess,
                startedAt.Elapsed);
        }
        catch (Exception ex)
        {
            LogHolePunchReadyError(_logger, ex, message.FileRequestId);
            var failure = FailureDescriptor.Unexpected(
                "holepunch.ready.unexpected",
                $"Unexpected hole punch ready failure: {TelemetryRedaction.SanitizeErrorMessage(ex.Message)}",
                correlationId);
            LogWorkflowFailureDescriptor(_logger, message.FileRequestId, failure.Code, failure.Category.ToString(),
                failure.Message);
            FederationTelemetry.SetFailure(activity, failure);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, ex);
            FederationMetrics.RecordOperation("holepunch.ready", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed, failureCategory: failure.Category.ToString(), failureCode: failure.Code);
        }
    }

    /// <summary>
    ///     Plugin reports the outcome of the hole punch attempt.
    /// </summary>
    public async Task ReportHolePunchResult(HolePunchResult result)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "holepunch.result", "server", correlationId,
            releaseVersion: "server");

        var request = await _db.FileRequests.FindAsync(result.FileRequestId).ConfigureAwait(false);
        if (request is null)
        {
            LogHolePunchResultNotFound(_logger, result.FileRequestId);
            var failure = FailureDescriptor.NotFound(
                "holepunch.result.request_not_found",
                "File request not found.",
                correlationId);
            LogWorkflowFailureDescriptor(_logger, result.FileRequestId, failure.Code, failure.Category.ToString(),
                failure.Message);
            FederationTelemetry.SetFailure(activity, failure);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("holepunch.result", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed, failureCategory: failure.Category.ToString(), failureCode: failure.Code);
            return;
        }

        LogHolePunchResultReceived(_logger, result.FileRequestId, result.Success, result.Error ?? string.Empty);

        if (result.Success)
        {
            request.Status = FileRequestStatus.Transferring;
            request.TransferStartedAt = DateTime.UtcNow;
            request.FailureCategory = null;
            LogTransferStarted(_logger, result.FileRequestId);
        }
        else
        {
            request.Status = FileRequestStatus.Failed;
            var mappedFailure = result.Failure ?? FailureDescriptor.Connectivity(
                "holepunch.result.failed",
                $"Hole punch failed: {result.Error}. The server may be behind symmetric NAT. Consider configuring port forwarding.",
                correlationId);
            request.FailureCategory = TransferFailureCategory.Connectivity;
            request.FailureReason = mappedFailure.Message;
            LogWorkflowFailureDescriptor(_logger, result.FileRequestId, mappedFailure.Code,
                mappedFailure.Category.ToString(), mappedFailure.Message);
            LogHolePunchMarkedFailed(_logger, result.FileRequestId, request.FailureReason);
            FederationTelemetry.SetFailure(activity, mappedFailure);
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);

        // Notify both sides of the status update via the shared notifier
        await _notifier.NotifyStatusAsync(request).ConfigureAwait(false);
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
            LogCandidateMatchFailed(_logger, request.Id);
            return;
        }

        var senderIp = _tracker.GetPublicIp(sender.ConnectionId);
        var receiverIp = _tracker.GetPublicIp(receiver.ConnectionId);

        if (senderIp is null || receiverIp is null)
        {
            LogHolePunchMissingPublicIp(_logger, request.Id, sender.ConnectionId, receiver.ConnectionId);
            return;
        }

        var selection = await SelectTransportModeAsync(request, sender, receiver).ConfigureAwait(false);
        request.SelectedTransportMode = selection.Mode;
        request.TransportSelectionReason = selection.Reason;

        request.Status = FileRequestStatus.HolePunching;
        await _db.SaveChangesAsync().ConfigureAwait(false);

        // Tell sender: punch to receiver's public UDP endpoint
        await Clients.Client(sender.ConnectionId).SendAsync(
            "HolePunchRequest",
            new HolePunchRequest(
                request.Id,
                $"{receiverIp}:{receiver.UdpPort}",
                sender.UdpPort,
                HolePunchRole.Sender,
                selection.Mode,
                selection.Reason)).ConfigureAwait(false);

        // Tell receiver: punch to sender's public UDP endpoint
        await Clients.Client(receiver.ConnectionId).SendAsync(
            "HolePunchRequest",
            new HolePunchRequest(
                request.Id,
                $"{senderIp}:{sender.UdpPort}",
                receiver.UdpPort,
                HolePunchRole.Receiver,
                selection.Mode,
                selection.Reason)).ConfigureAwait(false);

        LogHolePunchInitiated(_logger, request.Id, senderIp, sender.UdpPort, receiverIp, receiver.UdpPort);
        LogTransportModeSelected(_logger, request.Id, selection.Mode, selection.Reason);
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
            return new TransferSelection(TransferTransportMode.ArqUdp, TransferSelectionReason.QuicUnsupportedPeer);

        var item = await _db.MediaItems
            .Where(m => m.ServerId == request.OwningServerId && m.JellyfinItemId == request.JellyfinItemId)
            .Select(m => new { m.FileSizeBytes })
            .FirstOrDefaultAsync().ConfigureAwait(false);

        if (item is null)
            return new TransferSelection(TransferTransportMode.ArqUdp, TransferSelectionReason.NegotiationFailed);

        request.TotalBytes = item.FileSizeBytes;

        var threshold = Math.Max(sender.LargeFileThresholdBytes, receiver.LargeFileThresholdBytes);
        if (item.FileSizeBytes >= threshold)
            return new TransferSelection(TransferTransportMode.Quic, TransferSelectionReason.LargeFileQuic);

        return new TransferSelection(TransferTransportMode.ArqUdp, TransferSelectionReason.DefaultArq);
    }

    /// <summary>
    ///     Plugin (receiver) reports bytes transferred so the dashboard can show a progress bar.
    /// </summary>
    public async Task ReportTransferProgress(TransferProgress progress)
    {
        var request = await _db.FileRequests.FindAsync(progress.FileRequestId).ConfigureAwait(false);
        if (request is null)
        {
            LogTransferProgressRequestNotFound(_logger, progress.FileRequestId, progress.BytesReceived,
                progress.TotalBytes);
            return;
        }

        request.BytesTransferred = progress.BytesReceived;
        request.TotalBytes = progress.TotalBytes;
        await _db.SaveChangesAsync().ConfigureAwait(false);

        // Forward to browser clients watching either server
        await Clients.Group($"server:{request.OwningServerId}").SendAsync("TransferProgress", progress)
            .ConfigureAwait(false);
        await Clients.Group($"server:{request.RequestingServerId}").SendAsync("TransferProgress", progress)
            .ConfigureAwait(false);
        LogTransferProgressForwarded(_logger, request.Id, request.OwningServerId, request.RequestingServerId,
            progress.BytesReceived, progress.TotalBytes);
    }

    private IPAddress GetPublicIp()
    {
        var http = Context.GetHttpContext();
        // Rely on ForwardedHeaders middleware (configured in Program.cs) which
        // only trusts known proxies and sets RemoteIpAddress correctly.
        return http?.Connection.RemoteIpAddress ?? IPAddress.Loopback;
    }
}

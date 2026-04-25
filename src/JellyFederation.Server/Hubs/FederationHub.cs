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
    private const int MaxIceSignalPayloadChars = 128 * 1024;
    private const int MaxRelayChunkBytes = 32 * 1024;

    private readonly FederationDbContext _db;
    private readonly ILogger<FederationHub> _logger;
    private readonly FileRequestNotifier _notifier;
    private readonly WebSessionService _sessions;
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
        WebSessionService sessions,
        ILogger<FederationHub> logger)
    {
        _db = db;
        _tracker = tracker;
        _notifier = notifier;
        _sessions = sessions;
        _logger = logger;
    }

    /// <summary>
    ///     Resolves the server from the hub API key.
    ///     Preferred transports pass the key via X-Api-Key or Authorization: Bearer.
    ///     Browser SignalR clients may fall back to access_token because WebSocket APIs cannot set custom headers.
    ///     The legacy apiKey query parameter is retained for backwards compatibility.
    /// </summary>
    private async Task<RegisteredServer?> AuthenticateAsync()
    {
        var http = Context.GetHttpContext();
        var apiKey = ResolveApiKey(http);
        RegisteredServer? server;
        if (string.IsNullOrEmpty(apiKey))
        {
            if (http is null)
            {
                LogHubAuthenticationMissingApiKey(_logger, Context.ConnectionId);
                return null;
            }

            server = await _sessions.AuthenticateCookieAsync(http.Request, asTracking: true).ConfigureAwait(false);
            if (server is null)
                LogHubAuthenticationMissingApiKey(_logger, Context.ConnectionId);
            return server;
        }

        // AsTracking required — server.IsOnline/LastSeenAt are mutated in OnConnectedAsync.
        server = await _db.Servers
            .AsTracking()
            .FirstOrDefaultAsync(s => s.ApiKey == apiKey).ConfigureAwait(false);
        if (server is null)
            LogHubAuthenticationFailed(_logger, Context.ConnectionId);
        return server;
    }

    private static string? ResolveApiKey(HttpContext? http)
    {
        if (http is null)
            return null;

        if (http.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            var apiKey = apiKeyHeader.ToString();
            if (!string.IsNullOrWhiteSpace(apiKey))
                return apiKey;
        }

        if (http.Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            var authorization = authorizationHeader.ToString();
            const string bearerPrefix = "Bearer ";
            if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var bearerToken = authorization[bearerPrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(bearerToken))
                    return bearerToken;
            }
        }

        var accessToken = http.Request.Query["access_token"].ToString();
        if (!string.IsNullOrWhiteSpace(accessToken))
            return accessToken;

        var legacyApiKey = http.Request.Query["apiKey"].ToString();
        return string.IsNullOrWhiteSpace(legacyApiKey) ? null : legacyApiKey;
    }

    // Called by plugin on connect.
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
            .AsNoTracking()
            .Where(r => (r.Status == FileRequestStatus.Pending || r.Status == FileRequestStatus.HolePunching) &&
                        (r.OwningServerId == server.Id || r.RequestingServerId == server.Id))
            .Select(r => new { r.Id, r.JellyfinItemId, r.RequestingServerId, r.OwningServerId })
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
            var server = await _db.Servers
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == serverId)
                .ConfigureAwait(false);
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

            var request = await _db.FileRequests
                .AsTracking()
                .FirstOrDefaultAsync(r => r.Id == message.FileRequestId)
                .ConfigureAwait(false);
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
                    out var candidates,
                    message.SupportsIce))
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

        var senderId = _tracker.GetServerId(Context.ConnectionId);
        if (senderId is null)
        {
            LogHubWorkflowUnknownConnection(_logger, nameof(ReportHolePunchResult), Context.ConnectionId,
                result.FileRequestId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("holepunch.result", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed, failureCategory: FailureCategory.Authorization.ToString(),
                failureCode: "holepunch.result.unauthorized_connection");
            return;
        }

        var request = await _db.FileRequests
            .AsTracking()
            .FirstOrDefaultAsync(r => r.Id == result.FileRequestId)
            .ConfigureAwait(false);
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

        if (senderId.Value != request.OwningServerId && senderId.Value != request.RequestingServerId)
        {
            var failure = FailureDescriptor.Authorization(
                "holepunch.result.forbidden",
                "Only participating servers may report hole punch results.",
                correlationId);
            LogHubWorkflowUnauthorizedParticipant(_logger, nameof(ReportHolePunchResult), result.FileRequestId,
                senderId.Value, request.OwningServerId, request.RequestingServerId);
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
        // candidates is guaranteed to have exactly 2 distinct-server entries at dispatch time.
        var sender   = candidates[0].ServerId == request.OwningServerId      ? candidates[0] : candidates[1];
        var receiver = candidates[0].ServerId == request.RequestingServerId  ? candidates[0] : candidates[1];

        if (sender.ServerId != request.OwningServerId || receiver.ServerId != request.RequestingServerId)
        {
            LogCandidateMatchFailed(_logger, request.Id);
            return;
        }

        // If both peers support WebRTC ICE, bypass hole-punch and start ICE negotiation instead.
        if (sender.SupportsIce && receiver.SupportsIce)
        {
            request.SelectedTransportMode = TransferTransportMode.WebRtc;
            request.TransportSelectionReason = TransferSelectionReason.IceNegotiated;
            request.Status = FileRequestStatus.HolePunching;
            await _db.SaveChangesAsync().ConfigureAwait(false);

            // Start the answerer first so it has a session ready before the offerer can send SDP.
            await Clients.Client(receiver.ConnectionId).SendAsync(
                "IceNegotiateStart",
                new IceNegotiateStart(request.Id, IceRole.Answerer)).ConfigureAwait(false);

            await Clients.Client(sender.ConnectionId).SendAsync(
                "IceNegotiateStart",
                new IceNegotiateStart(request.Id, IceRole.Offerer)).ConfigureAwait(false);

            LogIceNegotiationStarted(_logger, request.Id);
            LogTransportModeSelected(_logger, request.Id, TransferTransportMode.WebRtc, TransferSelectionReason.IceNegotiated);
            FederationMetrics.RecordOperation("file.transfer.mode.webrtc", "server", "selected", TimeSpan.Zero);
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

    /// <summary>
    ///     Forwards an ICE signal (offer, answer, or trickle candidate) from one peer to the other.
    ///     The server treats the payload as opaque — it only routes by FileRequestId.
    /// </summary>
    public async Task ForwardIceSignal(IceSignal signal)
    {
        if (signal.Payload.Length > MaxIceSignalPayloadChars)
            return;

        var senderId = _tracker.GetServerId(Context.ConnectionId);
        if (senderId is null)
        {
            LogForwardIceSignalUnknownConnection(_logger, Context.ConnectionId, signal.FileRequestId);
            return;
        }

        // Project only the routing fields — no need to load or track the full FileRequest.
        var routing = await _db.FileRequests
            .Where(r => r.Id == signal.FileRequestId)
            .Select(r => new { r.OwningServerId, r.RequestingServerId })
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (routing is null)
        {
            LogForwardIceSignalNotFound(_logger, signal.FileRequestId);
            return;
        }

        if (senderId.Value != routing.OwningServerId && senderId.Value != routing.RequestingServerId)
        {
            LogHubWorkflowUnauthorizedParticipant(_logger, nameof(ForwardIceSignal), signal.FileRequestId,
                senderId.Value, routing.OwningServerId, routing.RequestingServerId);
            return;
        }

        var targetServerId = senderId.Value == routing.OwningServerId
            ? routing.RequestingServerId
            : routing.OwningServerId;

        var targetConnectionId = _tracker.GetConnectionId(targetServerId);
        if (targetConnectionId is null)
        {
            LogForwardIceSignalPeerOffline(_logger, signal.FileRequestId, targetServerId);
            return;
        }

        await Clients.Client(targetConnectionId).SendAsync("IceSignal", signal).ConfigureAwait(false);
        LogForwardedIceSignal(_logger, signal.FileRequestId, signal.Type.ToString(), senderId.Value, targetServerId);
    }

    /// <summary>
    ///     Receives a relay chunk from the sender plugin and forwards it to the receiver plugin.
    ///     Used when direct ICE negotiation fails. The server never buffers more than one chunk.
    /// </summary>
    public async Task RelaySendChunk(RelayChunk chunk)
    {
        if (chunk.Data.Length > MaxRelayChunkBytes)
            return;

        var senderId = _tracker.GetServerId(Context.ConnectionId);
        if (senderId is null)
        {
            LogRelaySendChunkUnknownConnection(_logger, Context.ConnectionId, chunk.FileRequestId);
            return;
        }

        // Project only routing IDs — we never mutate this entity.
        var routing = await _db.FileRequests
            .Where(r => r.Id == chunk.FileRequestId)
            .Select(r => new { r.OwningServerId, r.RequestingServerId })
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (routing is null)
        {
            LogRelaySendChunkNotFound(_logger, chunk.FileRequestId);
            return;
        }

        if (senderId.Value != routing.OwningServerId)
        {
            LogHubWorkflowUnauthorizedParticipant(_logger, nameof(RelaySendChunk), chunk.FileRequestId,
                senderId.Value, routing.OwningServerId, routing.RequestingServerId);
            return;
        }

        var receiverConnectionId = _tracker.GetConnectionId(routing.RequestingServerId);
        if (receiverConnectionId is null)
        {
            LogRelaySendChunkReceiverOffline(_logger, chunk.FileRequestId, routing.RequestingServerId);
            return;
        }

        await Clients.Client(receiverConnectionId).SendAsync("RelayReceiveChunk", chunk).ConfigureAwait(false);
        LogRelayChunkForwarded(_logger, chunk.FileRequestId, chunk.ChunkIndex, chunk.IsEof);
    }

    /// <summary>
    ///     Notifies the receiver plugin that relay mode has been engaged by the sender.
    /// </summary>
    public async Task ForwardRelayTransferStart(RelayTransferStart message)
    {
        var senderId = _tracker.GetServerId(Context.ConnectionId);
        if (senderId is null)
        {
            LogHubWorkflowUnknownConnection(_logger, nameof(ForwardRelayTransferStart), Context.ConnectionId,
                message.FileRequestId);
            return;
        }

        var request = await _db.FileRequests
            .FirstOrDefaultAsync(r => r.Id == message.FileRequestId).ConfigureAwait(false);
        if (request is null)
            return;

        if (senderId.Value != request.OwningServerId)
        {
            LogHubWorkflowUnauthorizedParticipant(_logger, nameof(ForwardRelayTransferStart), message.FileRequestId,
                senderId.Value, request.OwningServerId, request.RequestingServerId);
            return;
        }

        request.SelectedTransportMode = TransferTransportMode.Relay;
        request.TransportSelectionReason = TransferSelectionReason.IceFailed;
        await _db.SaveChangesAsync().ConfigureAwait(false);
        await _notifier.NotifyStatusAsync(request).ConfigureAwait(false);

        var targetConnectionId = _tracker.GetConnectionId(request.RequestingServerId);
        if (targetConnectionId is null)
            return;

        await Clients.Client(targetConnectionId).SendAsync("RelayTransferStart", message).ConfigureAwait(false);
        LogRelayTransferStartForwarded(_logger, message.FileRequestId, request.RequestingServerId);
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
        var senderId = _tracker.GetServerId(Context.ConnectionId);
        if (senderId is null)
        {
            LogHubWorkflowUnknownConnection(_logger, nameof(ReportTransferProgress), Context.ConnectionId,
                progress.FileRequestId);
            return;
        }

        // Project only the IDs needed for SignalR group routing — progress is transient,
        // no DB write per tick (terminal state is written by MarkCompleted / ReportHolePunchResult).
        var routing = await _db.FileRequests
            .Where(r => r.Id == progress.FileRequestId)
            .Select(r => new { r.Id, r.OwningServerId, r.RequestingServerId })
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (routing is null)
        {
            LogTransferProgressRequestNotFound(_logger, progress.FileRequestId, progress.BytesReceived,
                progress.TotalBytes);
            return;
        }

        if (senderId.Value != routing.RequestingServerId)
        {
            LogHubWorkflowUnauthorizedParticipant(_logger, nameof(ReportTransferProgress), progress.FileRequestId,
                senderId.Value, routing.OwningServerId, routing.RequestingServerId);
            return;
        }

        await Clients.Group($"server:{routing.OwningServerId}").SendAsync("TransferProgress", progress)
            .ConfigureAwait(false);
        await Clients.Group($"server:{routing.RequestingServerId}").SendAsync("TransferProgress", progress)
            .ConfigureAwait(false);
        LogTransferProgressForwarded(_logger, routing.Id, routing.OwningServerId, routing.RequestingServerId,
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

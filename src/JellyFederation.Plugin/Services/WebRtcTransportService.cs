using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Diagnostics;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace JellyFederation.Plugin.Services;

/// <summary>
///     Manages WebRTC ICE negotiation and DataChannel file transfer sessions.
///     Replaces the UDP hole-punch path when both peers advertise SupportsIce=true.
///     The existing HolePunchService is retained unchanged for backward-compat peers.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WebRTC negotiation depends on SIPSorcery peer connection callbacks and is covered by integration/manual transport tests rather than line coverage.")]
public partial class WebRtcTransportService
{
    private const int IceTimeoutMs = 30_000;
    private const string DataChannelLabel = "transfer";

    private readonly ConcurrentDictionary<Guid, IceNegotiationSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<IceSignal>> _pendingSignals = new();
    private readonly ConcurrentDictionary<Guid, string> _streamUrls = new();
    private readonly FileTransferService _fileTransfer;
    private readonly LocalStreamEndpoint _streamEndpoint;
    private readonly IPluginConfigurationProvider _configProvider;
    private readonly ILogger<WebRtcTransportService> _logger;

    public WebRtcTransportService(
        FileTransferService fileTransfer,
        LocalStreamEndpoint streamEndpoint,
        IPluginConfigurationProvider configProvider,
        ILogger<WebRtcTransportService> logger)
    {
        _fileTransfer = fileTransfer;
        _streamEndpoint = streamEndpoint;
        _configProvider = configProvider;
        _logger = logger;
    }

    /// <summary>
    ///     Called when server sends IceNegotiateStart with Role=Offerer (sender peer).
    ///     Creates RTCPeerConnection, creates DataChannel, generates SDP offer, starts trickle candidates.
    /// </summary>
    public async Task BeginAsOffererAsync(
        Guid fileRequestId,
        string jellyfinItemId,
        HubConnection connection,
        PluginConfiguration config)
    {
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            fileRequestId,
            correlationId,
            role: IceRole.Offerer.ToString(),
            transportMode: TransferTransportMode.WebRtc.ToString()));

        LogBeginAsOfferer(_logger, fileRequestId);
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            "ice.negotiate.offerer", ActivityKind.Client);

        var pc = CreatePeerConnection(config, fileRequestId);
        var cts = new CancellationTokenSource();
        var session = new IceNegotiationSession(fileRequestId, pc, IceRole.Offerer, cts);
        _sessions[fileRequestId] = session;
        DrainPendingSignals(fileRequestId);

        // Wire up trickle candidate forwarding
        pc.onicecandidate += candidate =>
        {
            if (candidate is null) return;
            var payload = JsonSerializer.Serialize(new
            {
                candidate = candidate.candidate,
                sdpMid = candidate.sdpMid,
                sdpMLineIndex = candidate.sdpMLineIndex
            });
            _ = ForwardCandidateAsync(connection, fileRequestId, payload, cts.Token);
        };

        // Create the data channel before the offer so it appears in the SDP
        var dc = await pc.createDataChannel(DataChannelLabel).ConfigureAwait(false);
        session.DataChannel = dc;

        dc.onopen += () =>
        {
            LogDataChannelOpen(_logger, fileRequestId, IceRole.Offerer);
            session.State = IceSessionState.Connected;
            _ = Task.Run(async () =>
            {
                try
                {
                    await ReportWebRtcTransferStartedAsync(fileRequestId, connection, cts.Token)
                        .ConfigureAwait(false);
                    await _fileTransfer.SendDataChannelAsync(fileRequestId, jellyfinItemId, dc, config, cts.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    CleanupSession(fileRequestId);
                }
            });
        };
        dc.onclose += () => LogDataChannelClosed(_logger, fileRequestId);

        // ICE connection state changes
        pc.onconnectionstatechange += state =>
        {
            LogIceStateChanged(_logger, fileRequestId, state.ToString());
            if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
                _ = TriggerRelayFallbackAsync(fileRequestId, jellyfinItemId, connection, config, cts.Token);
        };

        var offer = pc.createOffer();
        await pc.setLocalDescription(offer).ConfigureAwait(false);
        LogSdpOfferCreated(_logger, fileRequestId);

        await connection.SendAsync("ForwardIceSignal",
            new IceSignal(fileRequestId, IceSignalType.Offer, offer.sdp),
            cts.Token).ConfigureAwait(false);

        StartOffererConnectionTimeout(fileRequestId, jellyfinItemId, connection, config);
    }

    /// <summary>
    ///     Called when server sends IceNegotiateStart with Role=Answerer (receiver peer).
    ///     Creates RTCPeerConnection, waits for offer, creates SDP answer, starts trickle candidates.
    /// </summary>
    public async Task BeginAsAnswererAsync(
        Guid fileRequestId,
        HubConnection connection,
        PluginConfiguration config)
    {
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            fileRequestId,
            correlationId,
            role: IceRole.Answerer.ToString(),
            transportMode: TransferTransportMode.WebRtc.ToString()));

        LogBeginAsAnswerer(_logger, fileRequestId);
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            "ice.negotiate.answerer", ActivityKind.Client);

        var pc = CreatePeerConnection(config, fileRequestId);
        var cts = new CancellationTokenSource();
        var offerTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new IceNegotiationSession(fileRequestId, pc, IceRole.Answerer, cts)
        {
            OfferTcs = offerTcs
        };
        _sessions[fileRequestId] = session;
        DrainPendingSignals(fileRequestId);

        // Wire up trickle candidate forwarding
        pc.onicecandidate += candidate =>
        {
            if (candidate is null) return;
            var payload = JsonSerializer.Serialize(new
            {
                candidate = candidate.candidate,
                sdpMid = candidate.sdpMid,
                sdpMLineIndex = candidate.sdpMLineIndex
            });
            _ = ForwardCandidateAsync(connection, fileRequestId, payload, cts.Token);
        };

        // Data channel arrives from offerer
        pc.ondatachannel += dc =>
        {
            session.DataChannel = dc;
            LogDataChannelReceived(_logger, fileRequestId);

            var receiveStarted = 0;
            void StartReceive()
            {
                if (Interlocked.Exchange(ref receiveStarted, 1) == 1) return;

                LogDataChannelOpen(_logger, fileRequestId, IceRole.Answerer);
                session.State = IceSessionState.Connected;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ReportWebRtcTransferStartedAsync(fileRequestId, connection, cts.Token)
                            .ConfigureAwait(false);
                        await _fileTransfer.ReceiveDataChannelAsync(fileRequestId, dc, config, connection, cts.Token)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        CleanupSession(fileRequestId);
                    }
                });
            }

            dc.onopen += StartReceive;
            dc.onclose += () => LogDataChannelClosed(_logger, fileRequestId);

            // SIPSorcery can invoke ondatachannel after the DataChannel has already reached open.
            // In that case the onopen callback above will not fire and no receiver will be attached.
            if (dc.readyState == RTCDataChannelState.open)
                StartReceive();
        };

        pc.onconnectionstatechange += state =>
        {
            LogIceStateChanged(_logger, fileRequestId, state.ToString());
            if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
                _ = TriggerRelayReceiveAsync(fileRequestId, connection, config, cts.Token);
        };

        // Wait for offer to arrive via HandleIceSignalAsync
        LogWaitingForOffer(_logger, fileRequestId);
        string sdpOffer;
        try
        {
            sdpOffer = await offerTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(IceTimeoutMs), cts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            LogIceTimeout(_logger, fileRequestId);
            CleanupSession(fileRequestId);
            return;
        }

        var remoteOffer = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = sdpOffer
        };
        pc.setRemoteDescription(remoteOffer);
        session.RemoteDescriptionApplied = true;
        FlushPendingCandidates(session);

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer).ConfigureAwait(false);
        LogSdpAnswerCreated(_logger, fileRequestId);

        await connection.SendAsync("ForwardIceSignal",
            new IceSignal(fileRequestId, IceSignalType.Answer, answer.sdp),
            cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Routes an incoming IceSignal to the correct peer connection.
    ///     Called by the SignalR message handler when the server forwards an IceSignal.
    /// </summary>
    public void HandleIceSignal(IceSignal signal)
    {
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            signal.FileRequestId,
            signalType: signal.Type.ToString(),
            transportMode: TransferTransportMode.WebRtc.ToString()));

        LogIceSignalHandled(_logger, signal.FileRequestId, signal.Type.ToString(), signal.Payload.Length);

        if (!_sessions.TryGetValue(signal.FileRequestId, out var session))
        {
            var pending = _pendingSignals.GetOrAdd(signal.FileRequestId, _ => new ConcurrentQueue<IceSignal>());
            pending.Enqueue(signal);
            LogIceSignalNoSession(_logger, signal.FileRequestId, signal.Type.ToString());
            LogIceSignalQueued(_logger, signal.FileRequestId);
            return;
        }

        switch (signal.Type)
        {
            case IceSignalType.Offer:
                // Answerer: unblock BeginAsAnswererAsync to proceed with answer
                session.OfferTcs?.TrySetResult(signal.Payload);
                break;

            case IceSignalType.Answer:
                // Offerer: apply remote description
                session.PeerConnection.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.answer,
                    sdp = signal.Payload
                });
                session.RemoteDescriptionApplied = true;
                FlushPendingCandidates(session);
                LogSdpAnswerApplied(_logger, signal.FileRequestId);
                break;

            case IceSignalType.Candidate:
                try
                {
                    LogCandidateSignalReceived(_logger, signal.FileRequestId, session.RemoteDescriptionApplied);

                    var init = JsonSerializer.Deserialize<IceCandidateInit>(signal.Payload);
                    if (init is null || string.IsNullOrWhiteSpace(init.Candidate))
                    {
                        LogMalformedCandidatePayload(_logger, signal.FileRequestId);
                        break;
                    }

                    var candidate = new RTCIceCandidateInit
                    {
                        candidate = init.Candidate,
                        sdpMid = init.SdpMid,
                        sdpMLineIndex = (ushort)(init.SdpMLineIndex ?? 0)
                    };

                    if (session.RemoteDescriptionApplied)
                    {
                        AddRemoteCandidate(session, candidate);
                        LogCandidateAppliedImmediately(_logger, signal.FileRequestId);
                    }
                    else
                    {
                        session.PendingCandidates.Enqueue(candidate);
                        LogCandidateQueuedUntilRemoteDescription(_logger, signal.FileRequestId);
                    }
                }
                catch (Exception ex)
                {
                    LogIceCandidateAddFailed(_logger, ex, signal.FileRequestId);
                }
                break;
        }
    }

    /// <summary>
    ///     Cancels and cleans up an in-progress ICE session.
    /// </summary>
    public void Cancel(Guid fileRequestId)
    {
        CleanupSession(fileRequestId);
        _fileTransfer.Cancel(fileRequestId);
    }

    /// <summary>
    ///     Starts a streaming (non-saving) transfer as answerer: same ICE flow as US1 but instead of
    ///     writing to disk the DataChannel bytes are piped into <see cref="LocalStreamEndpoint"/>.
    /// </summary>
    /// <returns>The localhost URL Jellyfin can play, or null if ICE negotiation fails.</returns>
    public async Task<string?> StartStreamingTransferAsync(
        Guid fileRequestId, HubConnection connection, CancellationToken ct = default)
    {
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            fileRequestId,
            correlationId,
            role: IceRole.Answerer.ToString(),
            transportMode: TransferTransportMode.WebRtc.ToString()));

        var config = _configProvider.GetConfiguration();
        var pc = CreatePeerConnection(config, fileRequestId);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var offerTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var streamUrlTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var session = new IceNegotiationSession(fileRequestId, pc, IceRole.Answerer, cts)
        {
            OfferTcs = offerTcs
        };
        _sessions[fileRequestId] = session;
        DrainPendingSignals(fileRequestId);

        pc.onicecandidate += candidate =>
        {
            if (candidate is null) return;
            var payload = JsonSerializer.Serialize(new
            {
                candidate = candidate.candidate,
                sdpMid = candidate.sdpMid,
                sdpMLineIndex = candidate.sdpMLineIndex
            });
            _ = ForwardCandidateAsync(connection, fileRequestId, payload, cts.Token);
        };

        pc.ondatachannel += dc =>
        {
            session.DataChannel = dc;
            dc.onopen += () =>
            {
                LogDataChannelOpen(_logger, fileRequestId, IceRole.Answerer);
                session.State = IceSessionState.Connected;
                var pipeReader = _fileTransfer.ReceiveStreamingAsync(fileRequestId, dc, cts.Token);
                _ = Task.Run(async () =>
                {
                    var token = Guid.NewGuid();
                    var url = await _streamEndpoint.RegisterStreamAsync(token, pipeReader, cts.Token)
                        .ConfigureAwait(false);
                    _streamUrls[fileRequestId] = url;
                    streamUrlTcs.TrySetResult(url);
                });
            };
            dc.onclose += () =>
            {
                LogDataChannelClosed(_logger, fileRequestId);
                streamUrlTcs.TrySetResult(null);
                _streamUrls.TryRemove(fileRequestId, out _);
                CleanupSession(fileRequestId);
            };
        };

        pc.onconnectionstatechange += state =>
        {
            if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
            {
                streamUrlTcs.TrySetResult(null);
                _streamUrls.TryRemove(fileRequestId, out _);
                CleanupSession(fileRequestId);
            }
        };

        string sdpOffer;
        try
        {
            sdpOffer = await offerTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(IceTimeoutMs), cts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            LogIceTimeout(_logger, fileRequestId);
            CleanupSession(fileRequestId);
            return null;
        }

        pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdpOffer });
        session.RemoteDescriptionApplied = true;
        FlushPendingCandidates(session);
        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer).ConfigureAwait(false);

        await connection.SendAsync("ForwardIceSignal",
            new IceSignal(fileRequestId, IceSignalType.Answer, answer.sdp),
            cts.Token).ConfigureAwait(false);

        return await streamUrlTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(IceTimeoutMs), cts.Token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns the active streaming URL for the given file request, or null if not streaming.
    /// </summary>
    public string? GetStreamUrl(Guid fileRequestId)
    {
        return _streamUrls.TryGetValue(fileRequestId, out var url) ? url : null;
    }

    /// <summary>
    ///     Delivers a relay chunk to the waiting ReceiveRelayAsync loop.
    ///     Called from the SignalR RelayReceiveChunk handler.
    /// </summary>
    public void EnqueueRelayChunk(RelayChunk chunk) => _fileTransfer.EnqueueRelayChunk(chunk);

    /// <summary>
    ///     Switches this peer to relay receive mode when the sender notifies us via RelayTransferStart.
    ///     Called from the SignalR RelayTransferStart handler.
    /// </summary>
    public async Task StartRelayReceiveModeAsync(RelayTransferStart message, HubConnection connection)
    {
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            message.FileRequestId,
            role: message.Role.ToString(),
            transportMode: TransferTransportMode.Relay.ToString()));

        var config = _configProvider.GetConfiguration();
        await TriggerRelayReceiveAsync(message.FileRequestId, connection, config, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task ReportWebRtcTransferStartedAsync(Guid fileRequestId, HubConnection connection, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            fileRequestId,
            transportMode: TransferTransportMode.WebRtc.ToString()));

        try
        {
            await connection.SendAsync("ReportHolePunchResult", new HolePunchResult(fileRequestId, true, null), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReportWebRtcStartedFailed(_logger, ex, fileRequestId);
        }
    }

    private RTCPeerConnection CreatePeerConnection(PluginConfiguration config, Guid fileRequestId)
    {
        var stunServer = string.IsNullOrWhiteSpace(config.StunServer)
            ? "stun.l.google.com:19302"
            : config.StunServer;

        var iceServers = new List<RTCIceServer>
        {
            new() { urls = $"stun:{stunServer}" }
        };

        if (!string.IsNullOrWhiteSpace(config.TurnServer))
        {
            iceServers.Add(new RTCIceServer
            {
                urls = config.TurnServer,
                username = string.IsNullOrWhiteSpace(config.TurnUsername) ? null : config.TurnUsername,
                credential = string.IsNullOrWhiteSpace(config.TurnCredential) ? null : config.TurnCredential
            });
        }

        var configuration = new RTCConfiguration
        {
            iceServers = iceServers
        };

        LogIceServersConfigured(_logger, fileRequestId, stunServer, !string.IsNullOrWhiteSpace(config.TurnServer));

        var pc = new RTCPeerConnection(configuration);
        return pc;
    }

    private async Task ForwardCandidateAsync(
        HubConnection connection, Guid fileRequestId, string candidateJson, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            fileRequestId,
            transportMode: TransferTransportMode.WebRtc.ToString(),
            signalType: IceSignalType.Candidate.ToString()));

        try
        {
            await connection.SendAsync("ForwardIceSignal",
                new IceSignal(fileRequestId, IceSignalType.Candidate, candidateJson),
                ct).ConfigureAwait(false);
            LogCandidateForwarded(_logger, fileRequestId);
        }
        catch (Exception ex)
        {
            LogCandidateForwardFailed(_logger, ex, fileRequestId);
        }
    }

    private async Task TriggerRelayFallbackAsync(
        Guid fileRequestId, string jellyfinItemId, HubConnection connection,
        PluginConfiguration config, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            fileRequestId,
            transportMode: TransferTransportMode.Relay.ToString()));

        if (!_sessions.TryGetValue(fileRequestId, out var session) || session.State == IceSessionState.Relay)
            return;

        session.State = IceSessionState.Relay;
        LogRelayFallbackEngaged(_logger, fileRequestId);

        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            "ice.relay.fallback", ActivityKind.Client);
        FederationMetrics.RecordOperation(
            $"file.transfer.mode.{TransferTransportMode.Relay.ToString().ToLowerInvariant()}",
            "plugin", "selected", TimeSpan.Zero, FederationPlugin.ReleaseVersion);

        // Notify receiver to switch to relay receive mode
        try
        {
            await connection.SendAsync("ForwardRelayTransferStart",
                new RelayTransferStart(fileRequestId, IceRole.Offerer), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogRelayNotifyFailed(_logger, ex, fileRequestId);
        }

        try
        {
            await _fileTransfer.SendRelayAsync(fileRequestId, jellyfinItemId, connection, config, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            CleanupSession(fileRequestId);
        }
    }

    private async Task TriggerRelayReceiveAsync(
        Guid fileRequestId, HubConnection connection, PluginConfiguration config, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(
            fileRequestId,
            transportMode: TransferTransportMode.Relay.ToString()));

        if (!_sessions.TryGetValue(fileRequestId, out var session) || session.State == IceSessionState.Relay)
            return;

        session.State = IceSessionState.Relay;
        LogRelayReceiveModeEngaged(_logger, fileRequestId);
        try
        {
            await _fileTransfer.ReceiveRelayAsync(fileRequestId, connection, config, ct).ConfigureAwait(false);
        }
        finally
        {
            CleanupSession(fileRequestId);
        }
    }

    private void CleanupSession(Guid fileRequestId)
    {
        using var scope = _logger.BeginScope(FederationLogScopes.ForFileRequest(fileRequestId));

        _pendingSignals.TryRemove(fileRequestId, out _);
        _streamUrls.TryRemove(fileRequestId, out _);

        if (_sessions.TryRemove(fileRequestId, out var session))
        {
            try
            {
                session.Cts.Cancel();
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "ICE session cancellation skipped for {FileRequestId}", fileRequestId);
            }

            try
            {
                session.PeerConnection.Close("cleanup");
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "ICE peer connection already closed for {FileRequestId}", fileRequestId);
            }

            session.Cts.Dispose();
            LogSessionCleaned(_logger, fileRequestId);
        }
    }

    private void DrainPendingSignals(Guid fileRequestId)
    {
        if (!_pendingSignals.TryRemove(fileRequestId, out var pending))
            return;

        var drainedCount = 0;
        while (pending.TryDequeue(out var signal))
        {
            drainedCount++;
            HandleIceSignal(signal);
        }

        LogPendingSignalsDrained(_logger, fileRequestId, drainedCount);
    }

    private void FlushPendingCandidates(IceNegotiationSession session)
    {
        var flushedCount = 0;
        while (session.PendingCandidates.TryDequeue(out var candidate))
        {
            flushedCount++;
            AddRemoteCandidate(session, candidate);
        }

        LogPendingCandidatesFlushed(_logger, session.FileRequestId, flushedCount);
    }

    private void AddRemoteCandidate(IceNegotiationSession session, RTCIceCandidateInit candidate)
    {
        try
        {
            session.PeerConnection.addIceCandidate(candidate);
        }
        catch (Exception ex)
        {
            LogIceCandidateAddFailed(_logger, ex, session.FileRequestId);
        }
    }

    private void StartOffererConnectionTimeout(
        Guid fileRequestId,
        string jellyfinItemId,
        HubConnection connection,
        PluginConfiguration config)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(IceTimeoutMs).ConfigureAwait(false);

                if (!_sessions.TryGetValue(fileRequestId, out var session) ||
                    session.State is IceSessionState.Connected or IceSessionState.Relay)
                    return;

                await TriggerRelayFallbackAsync(fileRequestId, jellyfinItemId, connection, config, session.Cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogTrace(ex, "ICE offerer connection timeout cancelled for {FileRequestId}", fileRequestId);
            }
        });
    }

    private record IceCandidateInit
    {
        public string? Candidate { get; init; }
        public string? SdpMid { get; init; }
        public ushort? SdpMLineIndex { get; init; }
    }
}

/// <summary>
///     Holds per-FileRequest ICE negotiation state.
/// </summary>
internal sealed class IceNegotiationSession
{
    public IceNegotiationSession(Guid fileRequestId, RTCPeerConnection peerConnection,
        IceRole role, CancellationTokenSource cts)
    {
        FileRequestId = fileRequestId;
        PeerConnection = peerConnection;
        Role = role;
        Cts = cts;
        CreatedAt = DateTimeOffset.UtcNow;
        State = IceSessionState.Gathering;
    }

    public Guid FileRequestId { get; }
    public RTCPeerConnection PeerConnection { get; }
    public RTCDataChannel? DataChannel { get; set; }
    public ConcurrentQueue<RTCIceCandidateInit> PendingCandidates { get; } = new();
    public bool RemoteDescriptionApplied { get; set; }
    public IceRole Role { get; }
    public IceSessionState State { get; set; }
    public DateTimeOffset CreatedAt { get; }
    public CancellationTokenSource Cts { get; }

    /// <summary>Set by answerer to unblock answer creation once offer arrives.</summary>
    public TaskCompletionSource<string>? OfferTcs { get; init; }
}

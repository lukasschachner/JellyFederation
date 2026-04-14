using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Sockets;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
///     Implements UDP hole punching and then hands the established connection
///     to the file transfer service.
///     Protocol:
///     1. Plugin binds a UDP socket on an ephemeral port.
///     2. Plugin reports the port to the federation server (HolePunchReady).
///     3. Federation server tells both peers the other's public endpoint.
///     4. Both sides repeatedly send UDP probes to the remote endpoint
///     while listening for an incoming probe — this punches through NAT.
///     5. Once a probe is received, hole is established; file transfer begins.
/// </summary>
public partial class HolePunchService
{
    private const int ProbeIntervalMs = 200;
    private const int PunchTimeoutMs = 15_000;
    private const int ProbePayload = 0x4A46; // "JF" magic
    private readonly IPluginConfigurationProvider _configProvider;
    private readonly FileTransferService _fileTransfer;

    private readonly ILogger<HolePunchService> _logger;

    // Temporary storage for sockets bound before HolePunchRequest arrives (owner side).
    // Instance field (not static) because this service is registered as a singleton.
    private readonly ConcurrentDictionary
        <Guid, (Socket Socket, string JellyfinItemId, bool IsSender)> _pendingSockets = new();

    /// <summary>
    ///     Implements UDP hole punching and then hands the established connection
    ///     to the file transfer service.
    ///     Protocol:
    ///     1. Plugin binds a UDP socket on an ephemeral port.
    ///     2. Plugin reports the port to the federation server (HolePunchReady).
    ///     3. Federation server tells both peers the other's public endpoint.
    ///     4. Both sides repeatedly send UDP probes to the remote endpoint
    ///     while listening for an incoming probe — this punches through NAT.
    ///     5. Once a probe is received, hole is established; file transfer begins.
    /// </summary>
    public HolePunchService(ILogger<HolePunchService> logger,
        FileTransferService fileTransfer,
        IPluginConfigurationProvider configProvider)
    {
        _logger = logger;
        _fileTransfer = fileTransfer;
        _configProvider = configProvider;
    }

    /// <summary>
    ///     Called when the owning server receives a FileRequestNotification.
    ///     Binds a UDP socket and tells the federation server we're ready.
    /// </summary>
    public async Task PrepareAndSignalReadyAsync(
        FileRequestNotification notification,
        HubConnection connection)
    {
        var config = _configProvider.GetConfiguration();

        // Reuse existing socket if we already prepared for this request (reconnect scenario)
        int localPort;
        if (_pendingSockets.TryGetValue(notification.FileRequestId, out var existing))
        {
            localPort = ((IPEndPoint)existing.Socket.LocalEndPoint!).Port;
            LogReusingSocket(_logger, localPort, notification.FileRequestId);
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
                LogPortInUse(_logger, ex, bindPort, notification.FileRequestId);
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            }

            localPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
            LogBoundSocket(_logger, localPort, notification.FileRequestId);
            _pendingSockets[notification.FileRequestId] = (socket, notification.JellyfinItemId, notification.IsSender);
        }

        var overrideIp = string.IsNullOrWhiteSpace(config.OverridePublicIp) ? null : config.OverridePublicIp;
        var quicSupported = QuicConnection.IsSupported;
        var supportsQuic = config.PreferQuicForLargeFiles && quicSupported;
        var threshold = config.LargeFileQuicThresholdBytes > 0
            ? config.LargeFileQuicThresholdBytes
            : 512L * 1024 * 1024;
        LogHolePunchReadinessCapabilities(
            _logger,
            notification.FileRequestId,
            config.PreferQuicForLargeFiles,
            quicSupported,
            supportsQuic,
            threshold,
            localPort,
            overrideIp ?? "(auto)");
        await connection.SendAsync("ReportHolePunchReady",
                new HolePunchReady(notification.FileRequestId, localPort, overrideIp, supportsQuic, threshold,
                    SupportsIce: true))
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Called when the requesting server is told to begin a hole punch.
    ///     Also called for the owning server (Sender role).
    /// </summary>
    public async Task ExecuteAsync(HolePunchRequest request, HubConnection connection)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Client);
        FederationTelemetry.SetCommonTags(activity, "holepunch.execute", "plugin", correlationId,
            releaseVersion: FederationPlugin.ReleaseVersion);
        using var inFlight =
            FederationMetrics.BeginInflight("holepunch.execute", "plugin", FederationPlugin.ReleaseVersion);

        Socket socket;
        string? jellyfinItemId = null;
        var isSender = request.Role == HolePunchRole.Sender;

        if (_pendingSockets.TryRemove(request.FileRequestId, out var pending))
        {
            socket = pending.Socket;
            jellyfinItemId = pending.JellyfinItemId;
        }
        else
        {
            // Receiver side: bind a fresh socket on the assigned local port
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, request.LocalPort));
            }
            catch (SocketException ex)
            {
                LogBindFailed(_logger, ex, request.LocalPort, request.FileRequestId);
                socket.Dispose();
                try
                {
                    var bindFailure = FailureDescriptor.Connectivity(
                        "holepunch.bind_failed",
                        $"Port bind failed: {ex.Message}",
                        correlationId);
                    LogFailureDescriptor(_logger, request.FileRequestId, bindFailure.Code, bindFailure.Category.ToString(),
                        bindFailure.Message);
                    await connection.SendAsync("ReportHolePunchResult",
                            new HolePunchResult(request.FileRequestId, false, bindFailure.Message, bindFailure))
                        .ConfigureAwait(false);
                }
                catch (Exception sendEx)
                {
                    LogReportBindFailureFailed(_logger, sendEx, request.FileRequestId);
                }

                return;
            }
        }

        var remoteOutcome = ParseRemoteEndpoint(request.RemoteEndpoint, correlationId);
        if (remoteOutcome.IsFailure)
        {
            var failure = remoteOutcome.Failure!;
            LogInvalidEndpoint(_logger, request.RemoteEndpoint);
            LogFailureDescriptor(_logger, request.FileRequestId, failure.Code, failure.Category.ToString(), failure.Message);
            await connection.SendAsync("ReportHolePunchResult",
                new HolePunchResult(request.FileRequestId, false, failure.Message, failure)).ConfigureAwait(false);
            return;
        }

        var remoteEp = remoteOutcome.RequireValue();
        LogStartingHolePunch(_logger, remoteEp, request.Role);

        using var cts = new CancellationTokenSource(PunchTimeoutMs);
        var probe = BitConverter.GetBytes(ProbePayload);

        try
        {
            // Punch loop: send probes and listen concurrently.
            // We only need to wait for recvTask — once a probe arrives the hole is open.
            // Cancel the send loop immediately after to avoid waiting the full timeout.
            var sendTask = SendProbesAsync(socket, remoteEp, probe, cts.Token);
            var recvTask = WaitForProbeAsync(socket, probe, remoteEp, cts.Token);

            await recvTask.ConfigureAwait(false); // throws OperationCanceledException if 15s elapses
            await cts.CancelAsync().ConfigureAwait(false); // stop sending probes now that hole is open
            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            LogHolePunchSuccess(_logger, remoteEp);
            await connection.SendAsync("ReportHolePunchResult",
                    new HolePunchResult(request.FileRequestId, true, null), cts.Token)
                .ConfigureAwait(false);

            // Hand off to file transfer
            var config = _configProvider.GetConfiguration();
            LogTransportMode(_logger, request.FileRequestId, request.SelectedTransportMode,
                request.TransportSelectionReason);
            switch (isSender)
            {
                case true when jellyfinItemId is not null:
                    await _fileTransfer.SendFileAsync(
                        request.FileRequestId,
                        jellyfinItemId,
                        socket,
                        remoteEp,
                        config,
                        request.SelectedTransportMode,
                        request.TransportSelectionReason).ConfigureAwait(false);
                    break;
                case false:
                    await _fileTransfer.ReceiveFileAsync(
                        request.FileRequestId,
                        socket,
                        remoteEp,
                        config,
                        connection,
                        request.SelectedTransportMode,
                        request.TransportSelectionReason).ConfigureAwait(false);
                    break;
            }

            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
            FederationMetrics.RecordOperation("holepunch.execute", "plugin", FederationTelemetry.OutcomeSuccess,
                startedAt.Elapsed, FederationPlugin.ReleaseVersion);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            const string error = "Timed out. The peer may be behind symmetric NAT — "
                                 + "port forwarding is required for direct transfers in this case.";
            LogHolePunchTimeout(_logger, request.FileRequestId);
            try
            {
                var timeoutFailure = FailureDescriptor.Timeout("holepunch.timeout", error, correlationId);
                LogFailureDescriptor(_logger, request.FileRequestId, timeoutFailure.Code,
                    timeoutFailure.Category.ToString(), timeoutFailure.Message);
                await connection.SendAsync("ReportHolePunchResult",
                        new HolePunchResult(request.FileRequestId, false, timeoutFailure.Message, timeoutFailure), cts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogSendResultFailed(_logger, ex, request.FileRequestId);
            }

            socket.Dispose();
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeTimeout);
            FederationMetrics.RecordTimeout("holepunch.execute", "plugin", FederationPlugin.ReleaseVersion);
            FederationMetrics.RecordOperation("holepunch.execute", "plugin", FederationTelemetry.OutcomeTimeout,
                startedAt.Elapsed, FederationPlugin.ReleaseVersion);
        }
        catch (OperationCanceledException ex)
        {
            LogUnexpectedCancellation(_logger, ex, request.FileRequestId);
            try
            {
                var cancelledFailure = FailureDescriptor.Cancelled(
                    "holepunch.cancelled",
                    "Transfer was cancelled after hole punch. Check peer connectivity and retry.",
                    correlationId);
                LogFailureDescriptor(_logger, request.FileRequestId, cancelledFailure.Code,
                    cancelledFailure.Category.ToString(), cancelledFailure.Message);
                await connection.SendAsync("ReportHolePunchResult",
                    new HolePunchResult(request.FileRequestId, false, cancelledFailure.Message, cancelledFailure),
                    cts.Token).ConfigureAwait(false);
            }
            catch (Exception sendEx)
            {
                LogSendResultFailed(_logger, sendEx, request.FileRequestId);
            }

            socket.Dispose();
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
            FederationMetrics.RecordOperation("holepunch.execute", "plugin", FederationTelemetry.OutcomeCancelled,
                startedAt.Elapsed, FederationPlugin.ReleaseVersion);
        }
        catch (Exception ex)
        {
            LogTransferExecutionFailed(_logger, ex, request.FileRequestId);
            try
            {
                var runtimeFailure = FailureDescriptor.Reliability(
                    "holepunch.transfer_failed",
                    $"Transfer execution failed: {TelemetryRedaction.SanitizeErrorMessage(ex.Message)}",
                    correlationId);
                LogFailureDescriptor(_logger, request.FileRequestId, runtimeFailure.Code,
                    runtimeFailure.Category.ToString(), runtimeFailure.Message);
                await connection.SendAsync("ReportHolePunchResult",
                        new HolePunchResult(
                            request.FileRequestId,
                            false,
                            runtimeFailure.Message,
                            runtimeFailure), cts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception sendEx)
            {
                LogSendResultFailed(_logger, sendEx, request.FileRequestId);
            }

            socket.Dispose();
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, ex);
            FederationMetrics.RecordOperation("holepunch.execute", "plugin", FederationTelemetry.OutcomeError,
                startedAt.Elapsed, FederationPlugin.ReleaseVersion);
            throw;
        }
    }

    private static async Task SendProbesAsync(
        Socket socket, IPEndPoint remote, byte[] probe, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await socket.SendToAsync(probe, SocketFlags.None, remote, ct).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                /* remote not yet reachable — keep trying */
            }

            await Task.Delay(ProbeIntervalMs, ct).ConfigureAwait(false);
        }
    }

    private async Task WaitForProbeAsync(
        Socket socket, byte[] expectedProbe, IPEndPoint remoteEp, CancellationToken ct)
    {
        var buffer = new byte[256];
        while (!ct.IsCancellationRequested)
        {
            var received = await socket.ReceiveAsync(buffer, SocketFlags.None, ct).ConfigureAwait(false);
            if (received == expectedProbe.Length &&
                buffer.AsSpan(0, received).SequenceEqual(expectedProbe))
            {
                LogProbeReceived(_logger, remoteEp, received);
                return;
            }

            LogInvalidProbe(_logger, received);
        }
    }

    /// <summary>
    ///     Returns the Jellyfin item ID stored during PrepareAndSignalReadyAsync,
    ///     or null if no pending socket exists for this request.
    ///     Used by the ICE path to hand off the item ID without consuming the socket entry.
    /// </summary>
    public string? GetPendingJellyfinItemId(Guid fileRequestId) =>
        _pendingSockets.TryGetValue(fileRequestId, out var pending) ? pending.JellyfinItemId : null;

    public void Cancel(Guid fileRequestId)
    {
        // Cancel ongoing transfer (covers both send and receive)
        _fileTransfer.Cancel(fileRequestId);
        // Clean up any pending socket waiting for HolePunchRequest
        if (_pendingSockets.TryRemove(fileRequestId, out var pending))
            pending.Socket.Dispose();
    }

    private static OperationOutcome<IPEndPoint> ParseRemoteEndpoint(string remoteEndpoint, string correlationId)
    {
        var parts = remoteEndpoint.Split(':');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var remoteIp) ||
            !int.TryParse(parts[1], out var remotePort))
            return OperationOutcome<IPEndPoint>.Fail(FailureDescriptor.Validation(
                "holepunch.remote_endpoint_invalid",
                "Invalid remote endpoint",
                correlationId));

        return OperationOutcome<IPEndPoint>.Success(new IPEndPoint(remoteIp, remotePort));
    }
}

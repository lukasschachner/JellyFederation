using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Telemetry;
using JellyFederation.Shared.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

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
public partial class HolePunchService(
    ILogger<HolePunchService> logger,
    FileTransferService fileTransfer,
    IPluginConfigurationProvider configProvider)
{
    private const int ProbeIntervalMs = 200;
    private const int PunchTimeoutMs = 15_000;
    private const int ProbePayload = 0x4A46; // "JF" magic

    // Temporary storage for sockets bound before HolePunchRequest arrives (owner side).
    // Instance field (not static) because this service is registered as a singleton.
    private readonly System.Collections.Concurrent.ConcurrentDictionary
        <Guid, (Socket Socket, string JellyfinItemId, bool IsSender)> _pendingSockets = new();

    /// <summary>
    /// Called when the owning server receives a FileRequestNotification.
    /// Binds a UDP socket and tells the federation server we're ready.
    /// </summary>
    public async Task PrepareAndSignalReadyAsync(
        FileRequestNotification notification,
        HubConnection connection)
    {
        var config = configProvider.GetConfiguration();

        // Reuse existing socket if we already prepared for this request (reconnect scenario)
        int localPort;
        if (_pendingSockets.TryGetValue(notification.FileRequestId, out var existing))
        {
            localPort = ((IPEndPoint)existing.Socket.LocalEndPoint!).Port;
            LogReusingSocket(logger, localPort, notification.FileRequestId);
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
                LogPortInUse(logger, ex, bindPort, notification.FileRequestId);
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            }
            localPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
            LogBoundSocket(logger, localPort, notification.FileRequestId);
            _pendingSockets[notification.FileRequestId] = (socket, notification.JellyfinItemId, notification.IsSender);
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
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Client);
        FederationTelemetry.SetCommonTags(activity, "holepunch.execute", "plugin", correlationId, releaseVersion: FederationPlugin.ReleaseVersion);
        using var inFlight = FederationMetrics.BeginInflight("holepunch.execute", "plugin", FederationPlugin.ReleaseVersion);

        Socket socket;
        string? jellyfinItemId = null;
        bool isSender = request.Role == HolePunchRole.Sender;

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
                LogBindFailed(logger, ex, request.LocalPort, request.FileRequestId);
                socket.Dispose();
                try
                {
                    await connection.SendAsync("ReportHolePunchResult",
                        new HolePunchResult(request.FileRequestId, false, $"Port bind failed: {ex.Message}"));
                }
                catch (Exception sendEx)
                {
                    LogReportBindFailureFailed(logger, sendEx, request.FileRequestId);
                }
                return;
            }
        }

        var parts = request.RemoteEndpoint.Split(':');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var remoteIp) ||
            !int.TryParse(parts[1], out var remotePort))
        {
            LogInvalidEndpoint(logger, request.RemoteEndpoint);
            await connection.SendAsync("ReportHolePunchResult",
                new HolePunchResult(request.FileRequestId, false, "Invalid remote endpoint"));
            return;
        }

        var remoteEp = new IPEndPoint(remoteIp, remotePort);
        LogStartingHolePunch(logger, remoteEp, request.Role);

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

            LogHolePunchSuccess(logger, remoteEp);
            await connection.SendAsync("ReportHolePunchResult",
                new HolePunchResult(request.FileRequestId, true, null));

            // Hand off to file transfer
            var config = configProvider.GetConfiguration();
            if (isSender && jellyfinItemId is not null)
                await fileTransfer.SendFileAsync(request.FileRequestId, jellyfinItemId, socket, remoteEp, config);
            else if (!isSender)
                await fileTransfer.ReceiveFileAsync(request.FileRequestId, socket, remoteEp, config, connection);

            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
            FederationMetrics.RecordOperation("holepunch.execute", "plugin", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
        }
        catch (OperationCanceledException)
        {
            const string error = "Timed out. The peer may be behind symmetric NAT — "
                + "port forwarding is required for direct transfers in this case.";
            LogHolePunchTimeout(logger, request.FileRequestId);
            try
            {
                await connection.SendAsync("ReportHolePunchResult",
                    new HolePunchResult(request.FileRequestId, false, error));
            }
            catch (Exception ex)
            {
                LogSendResultFailed(logger, ex, request.FileRequestId);
            }
            socket.Dispose();
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeTimeout);
            FederationMetrics.RecordTimeout("holepunch.execute", "plugin", FederationPlugin.ReleaseVersion);
            FederationMetrics.RecordOperation("holepunch.execute", "plugin", FederationTelemetry.OutcomeTimeout, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
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
                LogProbeReceived(logger, remoteEp, received);
                return;
            }
            LogInvalidProbe(logger, received);
        }
    }

    public void Cancel(Guid fileRequestId)
    {
        // Cancel ongoing transfer (covers both send and receive)
        fileTransfer.Cancel(fileRequestId);
        // Clean up any pending socket waiting for HolePunchRequest
        if (_pendingSockets.TryRemove(fileRequestId, out var pending))
            pending.Socket.Dispose();
    }
}

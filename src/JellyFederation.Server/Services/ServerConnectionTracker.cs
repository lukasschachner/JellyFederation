using System.Collections.Concurrent;
using System.Net;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;

namespace JellyFederation.Server.Services;

/// <summary>
///     Tracks which SignalR connection belongs to which registered server,
///     and stores the public endpoint reported by each connected plugin.
/// </summary>
public partial class ServerConnectionTracker
{
    private readonly ConcurrentDictionary<Guid, IDisposable> _activeConnections = new();

    // connectionId -> public IP (from HTTP context)
    private readonly ConcurrentDictionary<string, IPAddress> _connectionPublicIp = new();

    // connectionId -> serverId
    private readonly ConcurrentDictionary<string, Guid> _connectionToServer = new();

    // fileRequestId -> (connectionId, udpPort) for hole punch staging
    private readonly ConcurrentDictionary<Guid, HolePunchCandidate[]> _holePunchStaging = new();

    private readonly ILogger<ServerConnectionTracker> _logger;

    // serverId -> connectionId
    private readonly ConcurrentDictionary<Guid, string> _serverToConnection = new();

    /// <summary>
    ///     Tracks which SignalR connection belongs to which registered server,
    ///     and stores the public endpoint reported by each connected plugin.
    /// </summary>
    public ServerConnectionTracker(ILogger<ServerConnectionTracker> logger)
    {
        _logger = logger;
    }

    public void Register(Guid serverId, string connectionId, IPAddress publicIp)
    {
        // Remove any stale mapping for this server
        if (_serverToConnection.TryGetValue(serverId, out var oldConn))
        {
            LogReplacingStaleConnection(_logger, serverId, oldConn, connectionId);
            _connectionToServer.TryRemove(oldConn, out _);
            _connectionPublicIp.TryRemove(oldConn, out _);
        }

        _serverToConnection[serverId] = connectionId;
        _connectionToServer[connectionId] = serverId;
        _connectionPublicIp[connectionId] = publicIp;
        if (!_activeConnections.ContainsKey(serverId))
            _activeConnections[serverId] = FederationMetrics.BeginInflight("signalr.connection", "server");
        LogRegisteredConnection(_logger, serverId, connectionId, publicIp);
    }

    public void Unregister(string connectionId)
    {
        LogUnregisteringConnection(_logger, connectionId);
        if (_connectionToServer.TryRemove(connectionId, out var serverId))
        {
            _serverToConnection.TryRemove(serverId, out _);
            if (_activeConnections.TryRemove(serverId, out var scope))
                scope.Dispose();
            LogUnregisteredConnection(_logger, serverId, connectionId);
        }
        else
        {
            LogUnregisterWithoutServer(_logger, connectionId);
        }

        _connectionPublicIp.TryRemove(connectionId, out _);
    }

    public string? GetConnectionId(Guid serverId)
    {
        return _serverToConnection.TryGetValue(serverId, out var conn) ? conn : null;
    }

    public Guid? GetServerId(string connectionId)
    {
        return _connectionToServer.TryGetValue(connectionId, out var id) ? id : null;
    }

    public IPAddress? GetPublicIp(string connectionId)
    {
        return _connectionPublicIp.TryGetValue(connectionId, out var ip) ? ip : null;
    }

    public void SetPublicIpOverride(string connectionId, IPAddress ip)
    {
        _connectionPublicIp[connectionId] = ip;
        LogPublicIpOverride(_logger, connectionId, ip);
    }

    /// <summary>
    ///     Records that a peer is ready for hole punching on the given file request.
    ///     Returns true if BOTH peers are now ready (triggers punch).
    /// </summary>
    public bool TryAddHolePunchReady(
        Guid fileRequestId,
        Guid serverId,
        string connectionId,
        int udpPort,
        bool supportsQuic,
        long largeFileThresholdBytes,
        out HolePunchCandidate[] candidates)
    {
        var threshold = largeFileThresholdBytes > 0 ? largeFileThresholdBytes : long.MaxValue;
        var entry = new HolePunchCandidate(serverId, connectionId, udpPort, supportsQuic, threshold);

        candidates = _holePunchStaging.AddOrUpdate(
            fileRequestId,
            _ => [entry],
            (_, existing) =>
            {
                // Replace any existing entry for this server (handles reconnect/resend)
                var others = existing.Where(c => c.ServerId != serverId).ToArray();
                return [.. others, entry];
            });
        LogHolePunchStaged(_logger, fileRequestId, serverId, udpPort, candidates.Length);

        // Dispatch only when we have exactly one entry per side (distinct servers)
        if (candidates.Select(c => c.ServerId).Distinct().Count() >= 2)
        {
            _holePunchStaging.TryRemove(fileRequestId, out _);
            LogHolePunchReadyToDispatch(_logger, fileRequestId, candidates.Length);
            return true;
        }

        return false;
    }
}

public record HolePunchCandidate
{
    public HolePunchCandidate(Guid ServerId,
        string ConnectionId,
        int UdpPort,
        bool SupportsQuic,
        long LargeFileThresholdBytes)
    {
        this.ServerId = ServerId;
        this.ConnectionId = ConnectionId;
        this.UdpPort = UdpPort;
        this.SupportsQuic = SupportsQuic;
        this.LargeFileThresholdBytes = LargeFileThresholdBytes;
    }

    public Guid ServerId { get; init; }
    public string ConnectionId { get; init; }
    public int UdpPort { get; init; }
    public bool SupportsQuic { get; init; }
    public long LargeFileThresholdBytes { get; init; }

    public void Deconstruct(out Guid ServerId, out string ConnectionId, out int UdpPort, out bool SupportsQuic,
        out long LargeFileThresholdBytes)
    {
        ServerId = this.ServerId;
        ConnectionId = this.ConnectionId;
        UdpPort = this.UdpPort;
        SupportsQuic = this.SupportsQuic;
        LargeFileThresholdBytes = this.LargeFileThresholdBytes;
    }
}

public readonly record struct TransferSelection
{
    public TransferSelection(TransferTransportMode Mode,
        TransferSelectionReason Reason)
    {
        this.Mode = Mode;
        this.Reason = Reason;
    }

    public TransferTransportMode Mode { get; init; }
    public TransferSelectionReason Reason { get; init; }

    public void Deconstruct(out TransferTransportMode Mode, out TransferSelectionReason Reason)
    {
        Mode = this.Mode;
        Reason = this.Reason;
    }
}
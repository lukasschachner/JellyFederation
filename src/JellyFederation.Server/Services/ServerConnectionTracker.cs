using System.Collections.Concurrent;
using System.Net;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Server.Services;

/// <summary>
/// Tracks which SignalR connection belongs to which registered server,
/// and stores the public endpoint reported by each connected plugin.
/// </summary>
public partial class ServerConnectionTracker(ILogger<ServerConnectionTracker> logger)
{
    // serverId -> connectionId
    private readonly ConcurrentDictionary<Guid, string> _serverToConnection = new();
    // connectionId -> serverId
    private readonly ConcurrentDictionary<string, Guid> _connectionToServer = new();
    // connectionId -> public IP (from HTTP context)
    private readonly ConcurrentDictionary<string, IPAddress> _connectionPublicIp = new();
    // fileRequestId -> (connectionId, udpPort) for hole punch staging
    private readonly ConcurrentDictionary<Guid, HolePunchCandidate[]> _holePunchStaging = new();
    private readonly ConcurrentDictionary<Guid, IDisposable> _activeConnections = new();

    public void Register(Guid serverId, string connectionId, IPAddress publicIp)
    {
        // Remove any stale mapping for this server
        if (_serverToConnection.TryGetValue(serverId, out var oldConn))
        {
            LogReplacingStaleConnection(logger, serverId, oldConn, connectionId);
            _connectionToServer.TryRemove(oldConn, out _);
            _connectionPublicIp.TryRemove(oldConn, out _);
        }

        _serverToConnection[serverId] = connectionId;
        _connectionToServer[connectionId] = serverId;
        _connectionPublicIp[connectionId] = publicIp;
        if (!_activeConnections.ContainsKey(serverId))
            _activeConnections[serverId] = FederationMetrics.BeginInflight("signalr.connection", "server");
        LogRegisteredConnection(logger, serverId, connectionId, publicIp);
    }

    public void Unregister(string connectionId)
    {
        LogUnregisteringConnection(logger, connectionId);
        if (_connectionToServer.TryRemove(connectionId, out var serverId))
        {
            _serverToConnection.TryRemove(serverId, out _);
            if (_activeConnections.TryRemove(serverId, out var scope))
                scope.Dispose();
            LogUnregisteredConnection(logger, serverId, connectionId);
        }
        else
        {
            LogUnregisterWithoutServer(logger, connectionId);
        }

        _connectionPublicIp.TryRemove(connectionId, out _);
    }

    public string? GetConnectionId(Guid serverId) =>
        _serverToConnection.TryGetValue(serverId, out var conn) ? conn : null;

    public Guid? GetServerId(string connectionId) =>
        _connectionToServer.TryGetValue(connectionId, out var id) ? id : null;

    public IPAddress? GetPublicIp(string connectionId) =>
        _connectionPublicIp.TryGetValue(connectionId, out var ip) ? ip : null;

    public void SetPublicIpOverride(string connectionId, IPAddress ip)
    {
        _connectionPublicIp[connectionId] = ip;
        LogPublicIpOverride(logger, connectionId, ip);
    }

    /// <summary>
    /// Records that a peer is ready for hole punching on the given file request.
    /// Returns true if BOTH peers are now ready (triggers punch).
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
        LogHolePunchStaged(logger, fileRequestId, serverId, udpPort, candidates.Length);

        // Dispatch only when we have exactly one entry per side (distinct servers)
        if (candidates.Select(c => c.ServerId).Distinct().Count() >= 2)
        {
            _holePunchStaging.TryRemove(fileRequestId, out _);
            LogHolePunchReadyToDispatch(logger, fileRequestId, candidates.Length);
            return true;
        }

        return false;
    }
}

public record HolePunchCandidate(
    Guid ServerId,
    string ConnectionId,
    int UdpPort,
    bool SupportsQuic,
    long LargeFileThresholdBytes);

public readonly record struct TransferSelection(
    TransferTransportMode Mode,
    TransferSelectionReason Reason);

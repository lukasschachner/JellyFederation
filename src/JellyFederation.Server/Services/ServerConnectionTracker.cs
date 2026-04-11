using System.Collections.Concurrent;
using System.Net;

namespace JellyFederation.Server.Services;

/// <summary>
/// Tracks which SignalR connection belongs to which registered server,
/// and stores the public endpoint reported by each connected plugin.
/// </summary>
public class ServerConnectionTracker
{
    // serverId -> connectionId
    private readonly ConcurrentDictionary<Guid, string> _serverToConnection = new();
    // connectionId -> serverId
    private readonly ConcurrentDictionary<string, Guid> _connectionToServer = new();
    // connectionId -> public IP (from HTTP context)
    private readonly ConcurrentDictionary<string, IPAddress> _connectionPublicIp = new();
    // fileRequestId -> (connectionId, udpPort) for hole punch staging
    private readonly ConcurrentDictionary<Guid, HolePunchCandidate[]> _holePunchStaging = new();

    public void Register(Guid serverId, string connectionId, IPAddress publicIp)
    {
        // Remove any stale mapping for this server
        if (_serverToConnection.TryGetValue(serverId, out var oldConn))
        {
            _connectionToServer.TryRemove(oldConn, out _);
            _connectionPublicIp.TryRemove(oldConn, out _);
        }

        _serverToConnection[serverId] = connectionId;
        _connectionToServer[connectionId] = serverId;
        _connectionPublicIp[connectionId] = publicIp;
    }

    public void Unregister(string connectionId)
    {
        if (_connectionToServer.TryRemove(connectionId, out var serverId))
            _serverToConnection.TryRemove(serverId, out _);

        _connectionPublicIp.TryRemove(connectionId, out _);
    }

    public string? GetConnectionId(Guid serverId) =>
        _serverToConnection.TryGetValue(serverId, out var conn) ? conn : null;

    public Guid? GetServerId(string connectionId) =>
        _connectionToServer.TryGetValue(connectionId, out var id) ? id : null;

    public IPAddress? GetPublicIp(string connectionId) =>
        _connectionPublicIp.TryGetValue(connectionId, out var ip) ? ip : null;

    public void SetPublicIpOverride(string connectionId, IPAddress ip) =>
        _connectionPublicIp[connectionId] = ip;

    /// <summary>
    /// Records that a peer is ready for hole punching on the given file request.
    /// Returns true if BOTH peers are now ready (triggers punch).
    /// </summary>
    public bool TryAddHolePunchReady(
        Guid fileRequestId,
        Guid serverId,
        string connectionId,
        int udpPort,
        out HolePunchCandidate[] candidates)
    {
        var entry = new HolePunchCandidate(serverId, connectionId, udpPort);

        candidates = _holePunchStaging.AddOrUpdate(
            fileRequestId,
            _ => [entry],
            (_, existing) =>
            {
                // Replace any existing entry for this server (handles reconnect/resend)
                var others = existing.Where(c => c.ServerId != serverId).ToArray();
                return [.. others, entry];
            });

        // Dispatch only when we have exactly one entry per side (distinct servers)
        if (candidates.Select(c => c.ServerId).Distinct().Count() >= 2)
        {
            _holePunchStaging.TryRemove(fileRequestId, out _);
            return true;
        }

        return false;
    }
}

public record HolePunchCandidate(Guid ServerId, string ConnectionId, int UdpPort);

using System.Net;
using JellyFederation.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class ServerConnectionTrackerTests
{
    [Fact]
    public void Register_ReplacesStaleConnectionAndUnregistersCurrentConnection()
    {
        var tracker = CreateTracker();
        var serverId = Guid.NewGuid();

        tracker.Register(serverId, "conn-1", IPAddress.Parse("203.0.113.10"));
        tracker.Register(serverId, "conn-2", IPAddress.Parse("203.0.113.11"));

        Assert.Equal("conn-2", tracker.GetConnectionId(serverId));
        Assert.Null(tracker.GetServerId("conn-1"));
        Assert.Null(tracker.GetPublicIp("conn-1"));
        Assert.Equal(serverId, tracker.GetServerId("conn-2"));
        Assert.Equal(IPAddress.Parse("203.0.113.11"), tracker.GetPublicIp("conn-2"));

        tracker.Unregister("conn-2");

        Assert.Null(tracker.GetConnectionId(serverId));
        Assert.Null(tracker.GetServerId("conn-2"));
        Assert.Null(tracker.GetPublicIp("conn-2"));
    }

    [Fact]
    public void HolePunchStaging_ReplacesSamePeerAndDispatchesDistinctPeersOnce()
    {
        var tracker = CreateTracker();
        var fileRequestId = Guid.NewGuid();
        var serverA = Guid.NewGuid();
        var serverB = Guid.NewGuid();

        var firstReady = tracker.TryAddHolePunchReady(
            fileRequestId,
            serverA,
            "a-1",
            udpPort: 10_000,
            supportsQuic: false,
            largeFileThresholdBytes: 0,
            out var firstCandidates,
            supportsIce: false);

        var replacementReady = tracker.TryAddHolePunchReady(
            fileRequestId,
            serverA,
            "a-2",
            udpPort: 10_001,
            supportsQuic: true,
            largeFileThresholdBytes: 128,
            out var replacementCandidates,
            supportsIce: true);

        var dispatchReady = tracker.TryAddHolePunchReady(
            fileRequestId,
            serverB,
            "b-1",
            udpPort: 20_000,
            supportsQuic: true,
            largeFileThresholdBytes: 256,
            out var dispatchCandidates,
            supportsIce: true);

        Assert.False(firstReady);
        var first = Assert.Single(firstCandidates);
        Assert.Equal(long.MaxValue, first.LargeFileThresholdBytes);

        Assert.False(replacementReady);
        var replacement = Assert.Single(replacementCandidates);
        Assert.Equal(serverA, replacement.ServerId);
        Assert.Equal("a-2", replacement.ConnectionId);
        Assert.Equal(10_001, replacement.UdpPort);
        Assert.True(replacement.SupportsQuic);
        Assert.Equal(128, replacement.LargeFileThresholdBytes);
        Assert.True(replacement.SupportsIce);

        Assert.True(dispatchReady);
        Assert.Equal([serverA, serverB], dispatchCandidates.Select(c => c.ServerId).ToArray());
        Assert.Equal(["a-2", "b-1"], dispatchCandidates.Select(c => c.ConnectionId).ToArray());

        var afterDispatchReady = tracker.TryAddHolePunchReady(
            fileRequestId,
            serverA,
            "a-3",
            udpPort: 10_002,
            supportsQuic: false,
            largeFileThresholdBytes: 512,
            out var afterDispatchCandidates);

        Assert.False(afterDispatchReady);
        Assert.Equal("a-3", Assert.Single(afterDispatchCandidates).ConnectionId);
    }

    [Fact]
    public void IceCandidacyStaging_ReplacesSamePeerAndDispatchesDistinctPeersOnce()
    {
        var tracker = CreateTracker();
        var fileRequestId = Guid.NewGuid();
        var serverA = Guid.NewGuid();
        var serverB = Guid.NewGuid();

        Assert.False(tracker.TryAddIceCandidacy(fileRequestId, serverA, "a-1", out var firstCandidates));
        Assert.Equal("a-1", Assert.Single(firstCandidates).ConnectionId);

        Assert.False(tracker.TryAddIceCandidacy(fileRequestId, serverA, "a-2", out var replacementCandidates));
        Assert.Equal("a-2", Assert.Single(replacementCandidates).ConnectionId);

        Assert.True(tracker.TryAddIceCandidacy(fileRequestId, serverB, "b-1", out var dispatchCandidates));
        Assert.Equal([serverA, serverB], dispatchCandidates.Select(c => c.ServerId).ToArray());
        Assert.Equal(["a-2", "b-1"], dispatchCandidates.Select(c => c.ConnectionId).ToArray());

        Assert.False(tracker.TryAddIceCandidacy(fileRequestId, serverA, "a-3", out var afterDispatchCandidates));
        Assert.Equal("a-3", Assert.Single(afterDispatchCandidates).ConnectionId);
    }

    private static ServerConnectionTracker CreateTracker() =>
        new(NullLogger<ServerConnectionTracker>.Instance);
}

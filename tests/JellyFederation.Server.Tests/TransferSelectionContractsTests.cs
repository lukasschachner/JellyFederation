using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class TransferSelectionContractsTests
{
    [Fact]
    public void TransferSelection_ConstructAndDeconstruct_RoundTrips()
    {
        var selection = new TransferSelection(TransferTransportMode.Quic, TransferSelectionReason.LargeFileQuic);

        selection.Deconstruct(out var mode, out var reason);

        Assert.Equal(TransferTransportMode.Quic, mode);
        Assert.Equal(TransferSelectionReason.LargeFileQuic, reason);
    }

    [Fact]
    public void HolePunchCandidate_Deconstruct_RoundTripsAllFields()
    {
        var candidate = new HolePunchCandidate(
            ServerId: Guid.NewGuid(),
            ConnectionId: "conn-1",
            UdpPort: 12345,
            SupportsQuic: true,
            LargeFileThresholdBytes: 1024,
            SupportsIce: true);

        candidate.Deconstruct(out var serverId, out var connectionId, out var udpPort, out var supportsQuic,
            out var threshold, out var supportsIce);

        Assert.Equal(candidate.ServerId, serverId);
        Assert.Equal("conn-1", connectionId);
        Assert.Equal(12345, udpPort);
        Assert.True(supportsQuic);
        Assert.Equal(1024, threshold);
        Assert.True(supportsIce);
    }
}

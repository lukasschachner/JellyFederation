using System.Reflection;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class WebRtcComponentTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dev.sh")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }

    [Fact]
    public void DataChannelFrameHelpers_RoundTripTypedPayload()
    {
        var createFrame = typeof(FileTransferService).GetMethod(
            "CreateDataChannelFrame",
            BindingFlags.NonPublic | BindingFlags.Static);
        var tryGetPayload = typeof(FileTransferService).GetMethod(
            "TryGetDataChannelPayload",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(createFrame);
        Assert.NotNull(tryGetPayload);

        byte[] payload = [1, 2, 3, 4];
        var frame = Assert.IsType<byte[]>(createFrame.Invoke(null, [DataFrameType, new ReadOnlyMemory<byte>(payload)]));

        Assert.Equal(DataFrameType, frame[0]);
        Assert.Equal(payload, frame[1..]);

        object?[] args = [frame, DataFrameType, null];
        var result = Assert.IsType<bool>(tryGetPayload.Invoke(null, args));
        Assert.True(result);

        var extracted = Assert.IsType<ReadOnlyMemory<byte>>(args[2]);
        Assert.Equal(payload, extracted.ToArray());
    }

    [Fact]
    public void DataChannelFrameHelpers_RejectEmptyOrWrongTypeFrames()
    {
        var tryGetPayload = typeof(FileTransferService).GetMethod(
            "TryGetDataChannelPayload",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(tryGetPayload);

        object?[] emptyArgs = [Array.Empty<byte>(), DataFrameType, null];
        Assert.False(Assert.IsType<bool>(tryGetPayload.Invoke(null, emptyArgs)));

        object?[] wrongTypeArgs = [new byte[] { HeaderFrameType, 9, 9 }, DataFrameType, null];
        Assert.False(Assert.IsType<bool>(tryGetPayload.Invoke(null, wrongTypeArgs)));
    }

    [Fact]
    public void WebRtcTransferImplementation_UsesBoundedQueuesAndBackpressure()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Services",
            "FileTransferService.cs"));

        Assert.Contains("DataChannelMaxBufferedBytes", source);
        Assert.Contains("channel.bufferedAmount", source);
        Assert.Contains("bufferedAmountLowThreshold", source);
        Assert.Contains("Channel.CreateBounded<byte[]>", source);
        Assert.Contains("Channel.CreateBounded<RelayChunk>", source);
        Assert.DoesNotContain("Channel.CreateUnbounded<byte[]>", source);
        Assert.DoesNotContain("Channel.CreateUnbounded<RelayChunk>", source);
    }

    [Fact]
    public void WebRtcSignalingImplementation_BuffersEarlySignalsAndCandidates()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Services",
            "WebRtcTransportService.cs"));

        Assert.Contains("_pendingSignals", source);
        Assert.Contains("DrainPendingSignals", source);
        Assert.Contains("PendingCandidates", source);
        Assert.Contains("RemoteDescriptionApplied", source);
        Assert.Contains("FlushPendingCandidates", source);
    }

    [Fact]
    public void WebRtcConfiguration_IncludesOptionalTurnSettings()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(string.Empty, configuration.TurnServer);
        Assert.Equal(string.Empty, configuration.TurnUsername);
        Assert.Equal(string.Empty, configuration.TurnCredential);

        var html = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Web",
            "configurationpage.html"));
        var javascript = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Web",
            "configurationpage.js"));

        foreach (var fieldId in new[] { "turnServer", "turnUsername", "turnCredential" })
        {
            Assert.Contains($"id=\"{fieldId}\"", html);
            Assert.Contains($"#{fieldId}", javascript);
        }
    }

    [Fact]
    public void StreamingRangeDesign_DocumentsSeekablePlaybackProtocol()
    {
        var design = File.ReadAllText(Path.Combine(RepoRoot, "docs", "webrtc-streaming-range-design.md"));

        Assert.Contains("206 Partial Content", design);
        Assert.Contains("RangeRequest", design);
        Assert.Contains("RangeHeader", design);
        Assert.Contains("RangeData", design);
        Assert.Contains("RangeEnd", design);
        Assert.Contains("bounded response channels", design);
    }

    private const byte HeaderFrameType = 1;
    private const byte DataFrameType = 2;
}

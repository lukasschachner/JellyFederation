using System.Text.Json;
using System.Text.Json.Serialization;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class SignalRContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void HolePunchReady_DeserializeWithoutSupportsIce_DefaultsFalse()
    {
        const string json = """
            {
              "fileRequestId":"8f5d8ad3-0450-44e0-9435-cf0df92b0136",
              "udpPort":9040,
              "overridePublicIp":"203.0.113.4",
              "supportsQuic":true,
              "largeFileThresholdBytes":1048576
            }
            """;

        var dto = JsonSerializer.Deserialize<HolePunchReady>(json, JsonOptions);

        Assert.NotNull(dto);
        Assert.False(dto!.SupportsIce);
        Assert.Equal(9040, dto.UdpPort);
        Assert.True(dto.SupportsQuic);
    }

    [Fact]
    public void HolePunchRequest_RoundTrip_PreservesTransportEnumsAsStrings()
    {
        var dto = new HolePunchRequest(
            Guid.NewGuid(),
            "198.51.100.10:45678",
            5000,
            HolePunchRole.Sender,
            TransferTransportMode.WebRtc,
            TransferSelectionReason.IceNegotiated);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var clone = JsonSerializer.Deserialize<HolePunchRequest>(json, JsonOptions);

        Assert.Contains("\"selectedTransportMode\":\"WebRtc\"", json);
        Assert.Contains("\"transportSelectionReason\":\"IceNegotiated\"", json);
        Assert.NotNull(clone);
        Assert.Equal(dto.FileRequestId, clone!.FileRequestId);
        Assert.Equal(dto.RemoteEndpoint, clone.RemoteEndpoint);
        Assert.Equal(dto.SelectedTransportMode, clone.SelectedTransportMode);
        Assert.Equal(dto.TransportSelectionReason, clone.TransportSelectionReason);
    }

    [Fact]
    public void FileRequestStatusUpdate_RoundTrip_PreservesOptionalFields()
    {
        var dto = new FileRequestStatusUpdate(
            Guid.NewGuid(),
            Status: "Transferring",
            FailureReason: null,
            Failure: new ErrorContract("code", "Validation", "msg", "corr", new Dictionary<string, string?>
            {
                ["key"] = "value"
            }),
            SelectedTransportMode: TransferTransportMode.Quic,
            FailureCategory: TransferFailureCategory.Reliability,
            BytesTransferred: 123,
            TotalBytes: 456);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var clone = JsonSerializer.Deserialize<FileRequestStatusUpdate>(json, JsonOptions);

        Assert.NotNull(clone);
        Assert.Equal(dto.FileRequestId, clone!.FileRequestId);
        Assert.Equal(dto.Status, clone.Status);
        Assert.Equal(TransferTransportMode.Quic, clone.SelectedTransportMode);
        Assert.Equal(TransferFailureCategory.Reliability, clone.FailureCategory);
        Assert.Equal(123, clone.BytesTransferred);
        Assert.Equal(456, clone.TotalBytes);
        Assert.Equal("value", clone.Failure!.Details!["key"]);
    }

    [Fact]
    public void HolePunchResult_RoundTrip_PreservesFailureContract()
    {
        var dto = new HolePunchResult(
            Guid.NewGuid(),
            Success: false,
            Error: "failed",
            Failure: FailureDescriptor.Timeout("hp.timeout", "timed out", "corr"));

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var clone = JsonSerializer.Deserialize<HolePunchResult>(json, JsonOptions);

        Assert.NotNull(clone);
        Assert.False(clone!.Success);
        Assert.Equal("failed", clone.Error);
        Assert.Equal("hp.timeout", clone.Failure!.Code);
        Assert.Equal(nameof(FailureCategory.Timeout), clone.Failure.Category.ToString());
    }

    [Fact]
    public void PositionalSignalRRecords_RoundTrip()
    {
        var signal = new IceSignal(Guid.NewGuid(), IceSignalType.Candidate, "candidate:1 1 udp 1 127.0.0.1 1234 typ host");
        var relay = new RelayChunk(Guid.NewGuid(), 9, false, [1, 2, 3]);

        var signalClone = JsonSerializer.Deserialize<IceSignal>(JsonSerializer.Serialize(signal, JsonOptions), JsonOptions);
        var relayClone = JsonSerializer.Deserialize<RelayChunk>(JsonSerializer.Serialize(relay, JsonOptions), JsonOptions);

        Assert.NotNull(signalClone);
        Assert.Equal(signal.FileRequestId, signalClone!.FileRequestId);
        Assert.Equal(IceSignalType.Candidate, signalClone.Type);
        Assert.Equal(signal.Payload, signalClone.Payload);

        Assert.NotNull(relayClone);
        Assert.Equal(relay.FileRequestId, relayClone!.FileRequestId);
        Assert.Equal(relay.ChunkIndex, relayClone.ChunkIndex);
        Assert.Equal(relay.IsEof, relayClone.IsEof);
        Assert.Equal(relay.Data, relayClone.Data);
    }
}

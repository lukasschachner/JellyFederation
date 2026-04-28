using System.Net;
using System.Net.Sockets;
using System.Reflection;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class HolePunchServiceTests
{
    private static readonly FieldInfo PendingSocketsField = typeof(HolePunchService)
        .GetField("_pendingSockets", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_pendingSockets field not found.");
    private static readonly MethodInfo ParseRemoteEndpointMethod =
        typeof(HolePunchService).GetMethod("ParseRemoteEndpoint", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ParseRemoteEndpoint not found.");

    [Fact]
    public async Task PrepareAndSignalReadyAsync_StagesSocketAndCancelClearsIt_WhenConnectionSendFails()
    {
        var service = CreateService(new PluginConfiguration
        {
            HolePunchPort = 0,
            OverridePublicIp = "203.0.113.10",
            PreferQuicForLargeFiles = false,
            LargeFileQuicThresholdBytes = 1234
        });
        var requestId = Guid.NewGuid();

        await Assert.ThrowsAsync<NullReferenceException>(() => service.PrepareAndSignalReadyAsync(
            new FileRequestNotification(requestId, "item-1", Guid.NewGuid()),
            connection: null!));

        Assert.Equal("item-1", service.GetPendingJellyfinItemId(requestId));
        service.Cancel(requestId);
        Assert.Null(service.GetPendingJellyfinItemId(requestId));
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidRemoteEndpoint_ReportsFailureBeforePunching()
    {
        var service = CreateService(new PluginConfiguration());

        await Assert.ThrowsAsync<NullReferenceException>(() => service.ExecuteAsync(
            new HolePunchRequest(Guid.NewGuid(), "not-an-endpoint", 0, HolePunchRole.Receiver),
            connection: null!));
    }

    [Fact]
    public void Cancel_DisposesPendingSocket()
    {
        var service = CreateService(new PluginConfiguration());
        var requestId = Guid.NewGuid();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var dictionary = Assert.IsAssignableFrom<System.Collections.IDictionary>(PendingSocketsField.GetValue(service));
        dictionary[requestId] = (socket, "item-1", true);

        service.Cancel(requestId);

        Assert.False(dictionary.Contains(requestId));
        Assert.Throws<ObjectDisposedException>(() => socket.Bind(new IPEndPoint(IPAddress.Loopback, 0)));
    }

    [Fact]
    public void ParseRemoteEndpoint_WithValidInput_ReturnsEndpoint()
    {
        var outcome = InvokeParse("203.0.113.10:54321");

        Assert.True(outcome.IsSuccess);
        var endpoint = outcome.RequireValue();
        Assert.Equal(IPAddress.Parse("203.0.113.10"), endpoint.Address);
        Assert.Equal(54321, endpoint.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("not-an-ip:1234")]
    [InlineData("127.0.0.1:not-a-port")]
    public void ParseRemoteEndpoint_WithInvalidInput_ReturnsValidationFailure(string value)
    {
        var outcome = InvokeParse(value);

        Assert.True(outcome.IsFailure);
        Assert.Equal("holepunch.remote_endpoint_invalid", outcome.Failure!.Code);
        Assert.Equal(FailureCategory.Validation, outcome.Failure.Category);
    }

    [Theory]
    [InlineData("127.0.0.1:-1")]
    [InlineData("127.0.0.1:70000")]
    public void ParseRemoteEndpoint_WithOutOfRangePort_Throws(string value)
    {
        Assert.Throws<TargetInvocationException>(() => InvokeParse(value));
    }

    private sealed class TestConfigurationProvider(PluginConfiguration configuration) : IPluginConfigurationProvider
    {
        public PluginConfiguration GetConfiguration() => configuration;
    }

    private static HolePunchService CreateService(PluginConfiguration configuration) =>
        new(
            NullLogger<HolePunchService>.Instance,
            new FileTransferService(
                libraryManager: null!,
                libraryMonitor: null!,
                http: new HttpClient(),
                configProvider: new TestConfigurationProvider(configuration),
                logger: NullLogger<FileTransferService>.Instance),
            new TestConfigurationProvider(configuration));

    private static OperationOutcome<IPEndPoint> InvokeParse(string remoteEndpoint) =>
        Assert.IsType<OperationOutcome<IPEndPoint>>(ParseRemoteEndpointMethod.Invoke(null, [remoteEndpoint, "corr-id"]));
}

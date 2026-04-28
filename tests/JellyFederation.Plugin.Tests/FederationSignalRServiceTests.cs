using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class FederationSignalRServiceTests
{
    [Fact]
    public async Task StartAsync_ReturnsWithoutConnection_WhenConfigurationIsMissing()
    {
        await using var service = CreateService(new PluginConfiguration());

        await service.StartAsync(new PluginConfiguration(), TestContext.Current.CancellationToken);

        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task StartAsync_ConfiguredConnectionRegistersHandlersBeforeConnectFailure()
    {
        await using var service = CreateService(new PluginConfiguration
        {
            FederationServerUrl = "http://127.0.0.1:9",
            ApiKey = "test-key"
        });

        await Assert.ThrowsAnyAsync<Exception>(() => service.StartAsync(new PluginConfiguration
        {
            FederationServerUrl = "http://127.0.0.1:9",
            ApiKey = "test-key"
        }, TestContext.Current.CancellationToken));

        Assert.False(service.IsConnected);
    }

    private sealed class TestConfigurationProvider(PluginConfiguration configuration) : IPluginConfigurationProvider
    {
        public PluginConfiguration GetConfiguration() => configuration;
    }

    private static FederationSignalRService CreateService(PluginConfiguration configuration)
    {
        var configurationProvider = new TestConfigurationProvider(configuration);
        var fileTransfer = new FileTransferService(
            A.Fake<ILibraryManager>(),
            A.Fake<ILibraryMonitor>(),
            new HttpClient(),
            configurationProvider,
            NullLogger<FileTransferService>.Instance);
        var holePunch = new HolePunchService(
            NullLogger<HolePunchService>.Instance,
            fileTransfer,
            configurationProvider);
        var webRtc = new WebRtcTransportService(
            fileTransfer,
            new LocalStreamEndpoint(NullLogger<LocalStreamEndpoint>.Instance),
            configurationProvider,
            NullLogger<WebRtcTransportService>.Instance);
        var librarySync = new LibrarySyncService(
            A.Fake<ILibraryManager>(),
            new HttpClient(),
            NullLogger<LibrarySyncService>.Instance);

        return new FederationSignalRService(
            NullLogger<FederationSignalRService>.Instance,
            holePunch,
            webRtc,
            librarySync,
            configurationProvider);
    }
}

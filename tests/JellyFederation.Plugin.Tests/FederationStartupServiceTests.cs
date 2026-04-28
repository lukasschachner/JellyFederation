using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class FederationStartupServiceTests
{
    [Fact]
    public async Task StartAndStop_SubscribeAndUnsubscribeLibraryEvents()
    {
        var libraryManager = A.Fake<ILibraryManager>();
        var configurationProvider = new TestConfigurationProvider();
        var librarySync = new LibrarySyncService(
            libraryManager,
            new HttpClient(),
            NullLogger<LibrarySyncService>.Instance);
        await using var signalR = CreateSignalR(configurationProvider, libraryManager, librarySync);
        var service = new FederationStartupService(
            librarySync,
            signalR,
            libraryManager,
            configurationProvider,
            NullLogger<FederationStartupService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        A.CallTo(libraryManager).Where(call => call.Method.Name == "add_ItemAdded").MustHaveHappenedOnceExactly();
        A.CallTo(libraryManager).Where(call => call.Method.Name == "add_ItemRemoved").MustHaveHappenedOnceExactly();
        A.CallTo(libraryManager).Where(call => call.Method.Name == "add_ItemUpdated").MustHaveHappenedOnceExactly();
        A.CallTo(libraryManager).Where(call => call.Method.Name == "remove_ItemAdded").MustHaveHappenedOnceExactly();
        A.CallTo(libraryManager).Where(call => call.Method.Name == "remove_ItemRemoved").MustHaveHappenedOnceExactly();
        A.CallTo(libraryManager).Where(call => call.Method.Name == "remove_ItemUpdated").MustHaveHappenedOnceExactly();
    }

    private sealed class TestConfigurationProvider : IPluginConfigurationProvider
    {
        public PluginConfiguration GetConfiguration() => new();
    }

    private static FederationSignalRService CreateSignalR(
        IPluginConfigurationProvider configurationProvider,
        ILibraryManager libraryManager,
        LibrarySyncService librarySync)
    {
        var fileTransfer = new FileTransferService(
            libraryManager,
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
        return new FederationSignalRService(
            NullLogger<FederationSignalRService>.Instance,
            holePunch,
            webRtc,
            librarySync,
            configurationProvider);
    }
}

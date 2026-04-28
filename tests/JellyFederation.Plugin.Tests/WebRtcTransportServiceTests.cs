using System.Reflection;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using JellyFederation.Shared.SignalR;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using SIPSorcery.Net;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class WebRtcTransportServiceTests
{
    private static readonly FieldInfo PendingSignalsField = typeof(WebRtcTransportService)
        .GetField("_pendingSignals", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_pendingSignals field not found.");

    private static readonly FieldInfo StreamUrlsField = typeof(WebRtcTransportService)
        .GetField("_streamUrls", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_streamUrls field not found.");

    private static readonly FieldInfo SessionsField = typeof(WebRtcTransportService)
        .GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_sessions field not found.");

    [Fact]
    public void HandleIceSignal_QueuesSignal_WhenSessionDoesNotExist()
    {
        var service = CreateService();
        var requestId = Guid.NewGuid();
        var signal = new IceSignal(requestId, IceSignalType.Candidate, "{}");

        service.HandleIceSignal(signal);

        var pendingSignals = Assert.IsAssignableFrom<System.Collections.IDictionary>(PendingSignalsField.GetValue(service));
        Assert.True(pendingSignals.Contains(requestId));
    }

    [Fact]
    public void HandleIceSignal_WithExistingSession_HandlesOfferCandidateAndMalformedPayload()
    {
        var service = CreateService();
        var requestId = Guid.NewGuid();
        var session = CreateSession(requestId, IceRole.Answerer);
        var sessions = Assert.IsAssignableFrom<System.Collections.IDictionary>(SessionsField.GetValue(service));
        sessions[requestId] = session;

        service.HandleIceSignal(new IceSignal(requestId, IceSignalType.Offer, "offer-sdp"));
        service.HandleIceSignal(new IceSignal(requestId, IceSignalType.Candidate, "{}"));
        service.HandleIceSignal(new IceSignal(requestId, IceSignalType.Candidate, "not-json"));

        Assert.True(sessions.Contains(requestId));
    }

    [Fact]
    public void GetStreamUrl_ReturnsRegisteredUrlAndCancelRemovesIt()
    {
        var service = CreateService();
        var requestId = Guid.NewGuid();
        var streamUrls = Assert.IsAssignableFrom<System.Collections.IDictionary>(StreamUrlsField.GetValue(service));
        streamUrls[requestId] = "http://127.0.0.1/stream";

        Assert.Equal("http://127.0.0.1/stream", service.GetStreamUrl(requestId));

        service.Cancel(requestId);

        Assert.Null(service.GetStreamUrl(requestId));
    }

    [Fact]
    public async Task BeginAsOffererAsync_CreatesOffererSessionBeforeSendFailure()
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<Exception>(() => service.BeginAsOffererAsync(
            Guid.NewGuid(),
            "item-1",
            connection: null!,
            new PluginConfiguration()));
    }

    [Fact]
    public async Task BeginAsAnswererAsync_DrainsQueuedOfferBeforeAnswerFailure()
    {
        var service = CreateService();
        var requestId = Guid.NewGuid();
        service.HandleIceSignal(new IceSignal(requestId, IceSignalType.Offer, "not-valid-sdp"));

        await Assert.ThrowsAnyAsync<Exception>(() => service.BeginAsAnswererAsync(
            requestId,
            connection: null!,
            new PluginConfiguration()));
    }

    [Fact]
    public async Task StartStreamingTransferAsync_ReturnsNull_WhenCancellationIsAlreadyRequested()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var url = await service.StartStreamingTransferAsync(Guid.NewGuid(), connection: null!, cts.Token);

        Assert.Null(url);
    }

    [Fact]
    public async Task StartRelayReceiveModeAsync_Returns_WhenNoSessionExists()
    {
        var service = CreateService();

        await service.StartRelayReceiveModeAsync(new RelayTransferStart(Guid.NewGuid(), IceRole.Answerer), connection: null!);
    }

    private static object CreateSession(Guid requestId, IceRole role)
    {
        var type = typeof(WebRtcTransportService).Assembly.GetType("JellyFederation.Plugin.Services.IceNegotiationSession")
            ?? throw new InvalidOperationException("IceNegotiationSession type not found.");
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [requestId, new RTCPeerConnection(), role, new CancellationTokenSource()],
            culture: null)!;
    }

    private sealed class TestConfigurationProvider : IPluginConfigurationProvider
    {
        public PluginConfiguration GetConfiguration() => new()
        {
            FederationServerUrl = "http://127.0.0.1",
            ApiKey = "test-key"
        };
    }

    private static WebRtcTransportService CreateService()
    {
        var configurationProvider = new TestConfigurationProvider();
        var fileTransfer = new FileTransferService(
            A.Fake<ILibraryManager>(),
            A.Fake<ILibraryMonitor>(),
            new HttpClient(),
            configurationProvider,
            NullLogger<FileTransferService>.Instance);
        return new WebRtcTransportService(
            fileTransfer,
            new LocalStreamEndpoint(NullLogger<LocalStreamEndpoint>.Instance),
            configurationProvider,
            NullLogger<WebRtcTransportService>.Instance);
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class SignalRWorkflowTests : IAsyncLifetime
{
    private readonly TestServerFactory _factory = new();
    private HttpClient _http = null!;
    private TestApiClient _api = null!;

    public ValueTask InitializeAsync()
    {
        _http = _factory.CreateClient();
        _api = new TestApiClient(_http);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task BothPeersConnectSuccessfully()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        Assert.Equal(HubConnectionState.Connected, requesterConnection.State);
        Assert.Equal(HubConnectionState.Connected, ownerConnection.State);
    }

    [Fact]
    public async Task BrowserCookieSessionConnectsSuccessfully()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = await _api.RegisterAsync($"web-cookie-{Guid.NewGuid():N}", "owner-web", cancellationToken);
        using var sessionResponse = await _http.PostAsJsonAsync("/api/sessions", new CreateWebSessionRequest(server.ServerId, server.ApiKey),
            cancellationToken);
        sessionResponse.EnsureSuccessStatusCode();
        var cookie = Assert.Single(sessionResponse.Headers.GetValues("Set-Cookie"));

        await using var connection = CreateCookieHubConnection(cookie);

        await connection.StartAsync(cancellationToken);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task BrowserAccessTokenConnectsSuccessfully()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = await _api.RegisterAsync($"web-{Guid.NewGuid():N}", "owner-web", cancellationToken);

        await using var connection = CreateBrowserAccessTokenHubConnection(server.ApiKey);

        await connection.StartAsync(cancellationToken);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task FileRequestNotificationsAreDeliveredAndResentWhenPeerReconnects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterNotifications = new EventProbe<FileRequestNotification>();
        var ownerNotifications = new EventProbe<FileRequestNotification>();
        requesterConnection.On<FileRequestNotification>("FileRequestNotification", requesterNotifications.Add);
        ownerConnection.On<FileRequestNotification>("FileRequestNotification", ownerNotifications.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        var firstRequest = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-reconnect-1",
            cancellationToken);

        var requesterNotification = await ReadUntilAsync(
            requesterNotifications,
            notification => notification.FileRequestId == firstRequest.Id,
            cancellationToken);
        var ownerNotification = await ReadUntilAsync(
            ownerNotifications,
            notification => notification.FileRequestId == firstRequest.Id,
            cancellationToken);
        Assert.False(requesterNotification.IsSender);
        Assert.True(ownerNotification.IsSender);

        await ownerConnection.StopAsync(cancellationToken);

        var missedRequest = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-reconnect-2",
            cancellationToken);
        _ = await ReadUntilAsync(
            requesterNotifications,
            notification => notification.FileRequestId == missedRequest.Id,
            cancellationToken);

        await using var reconnectedOwner = CreateHubConnection(owner.ApiKey);
        var reconnectedOwnerNotifications = new EventProbe<FileRequestNotification>();
        reconnectedOwner.On<FileRequestNotification>("FileRequestNotification", reconnectedOwnerNotifications.Add);

        await reconnectedOwner.StartAsync(cancellationToken);

        var resentNotification = await ReadUntilAsync(
            reconnectedOwnerNotifications,
            notification => notification.FileRequestId == missedRequest.Id,
            cancellationToken);
        Assert.True(resentNotification.IsSender);
        Assert.Equal("item-reconnect-2", resentNotification.JellyfinItemId);
    }

    [Fact]
    public async Task IceNegotiationStartsOnlyAfterBothPeersAdvertiseIceSupport()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-ice", cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterIceStarts = new EventProbe<IceNegotiateStart>();
        var ownerIceStarts = new EventProbe<IceNegotiateStart>();
        requesterConnection.On<IceNegotiateStart>("IceNegotiateStart", requesterIceStarts.Add);
        ownerConnection.On<IceNegotiateStart>("IceNegotiateStart", ownerIceStarts.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        await ownerConnection.InvokeAsync(
            "ReportHolePunchReady",
            new HolePunchReady(request.Id, UdpPort: 41000, OverridePublicIp: "127.0.0.1", SupportsIce: true),
            cancellationToken);

        await ownerIceStarts.AssertNoMessageAsync(cancellationToken);
        await requesterIceStarts.AssertNoMessageAsync(cancellationToken);

        await requesterConnection.InvokeAsync(
            "ReportHolePunchReady",
            new HolePunchReady(request.Id, UdpPort: 41001, OverridePublicIp: "127.0.0.1", SupportsIce: true),
            cancellationToken);

        var ownerStart = await ownerIceStarts.ReadAsync(cancellationToken);
        var requesterStart = await requesterIceStarts.ReadAsync(cancellationToken);

        Assert.Equal(request.Id, ownerStart.FileRequestId);
        Assert.Equal(IceRole.Offerer, ownerStart.Role);
        Assert.Equal(request.Id, requesterStart.FileRequestId);
        Assert.Equal(IceRole.Answerer, requesterStart.Role);
    }

    [Fact]
    public async Task ForwardIceSignal_DropsPayloadsLargerThanGuardLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-ice-oversize", cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterSignals = new EventProbe<IceSignal>();
        requesterConnection.On<IceSignal>("IceSignal", requesterSignals.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        var oversizedPayload = new string('x', 128 * 1024 + 1);
        await Assert.ThrowsAnyAsync<Exception>(() => ownerConnection.InvokeAsync("ForwardIceSignal",
            new IceSignal(request.Id, IceSignalType.Candidate, oversizedPayload), cancellationToken));

        await requesterSignals.AssertNoMessageAsync(cancellationToken);
    }

    [Fact]
    public async Task RelayTransferStartIsForwardedOnlyFromAuthorizedSenderToReceiver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-relay-start",
            cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterRelayStarts = new EventProbe<RelayTransferStart>();
        var ownerRelayStarts = new EventProbe<RelayTransferStart>();
        requesterConnection.On<RelayTransferStart>("RelayTransferStart", requesterRelayStarts.Add);
        ownerConnection.On<RelayTransferStart>("RelayTransferStart", ownerRelayStarts.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        await requesterConnection.InvokeAsync("ForwardRelayTransferStart",
            new RelayTransferStart(request.Id, IceRole.Offerer), cancellationToken);

        await requesterRelayStarts.AssertNoMessageAsync(cancellationToken);
        await ownerRelayStarts.AssertNoMessageAsync(cancellationToken);

        await ownerConnection.InvokeAsync("ForwardRelayTransferStart",
            new RelayTransferStart(request.Id, IceRole.Offerer), cancellationToken);

        var routedStart = await requesterRelayStarts.ReadAsync(cancellationToken);
        Assert.Equal(request.Id, routedStart.FileRequestId);
        Assert.Equal(IceRole.Offerer, routedStart.Role);
        await ownerRelayStarts.AssertNoMessageAsync(cancellationToken);
    }

    [Fact]
    public async Task ForwardRelayTransferStart_RequestNotFound_DoesNotForwardMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requester = await _api.RegisterAsync($"requester-{Guid.NewGuid():N}", "owner-a", cancellationToken);
        var owner = await _api.RegisterAsync($"owner-{Guid.NewGuid():N}", "owner-b", cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterRelayStarts = new EventProbe<RelayTransferStart>();
        requesterConnection.On<RelayTransferStart>("RelayTransferStart", requesterRelayStarts.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        await ownerConnection.InvokeAsync("ForwardRelayTransferStart",
            new RelayTransferStart(Guid.NewGuid(), IceRole.Offerer), cancellationToken);

        await requesterRelayStarts.AssertNoMessageAsync(cancellationToken);
    }

    [Fact]
    public async Task RelayChunksAreDeliveredOnlyFromOwnerToRequestingParticipant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        var unrelated = await _api.RegisterAsync("unrelated", "owner-c", cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-relay-chunk",
            cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        await using var unrelatedConnection = CreateHubConnection(unrelated.ApiKey);
        var requesterChunks = new EventProbe<RelayChunk>();
        var ownerChunks = new EventProbe<RelayChunk>();
        var unrelatedChunks = new EventProbe<RelayChunk>();
        requesterConnection.On<RelayChunk>("RelayReceiveChunk", requesterChunks.Add);
        ownerConnection.On<RelayChunk>("RelayReceiveChunk", ownerChunks.Add);
        unrelatedConnection.On<RelayChunk>("RelayReceiveChunk", unrelatedChunks.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);
        await unrelatedConnection.StartAsync(cancellationToken);

        await requesterConnection.InvokeAsync("RelaySendChunk",
            new RelayChunk(request.Id, ChunkIndex: 0, IsEof: false, Data: [9, 9, 9]), cancellationToken);
        await unrelatedConnection.InvokeAsync("RelaySendChunk",
            new RelayChunk(request.Id, ChunkIndex: 1, IsEof: false, Data: [8, 8, 8]), cancellationToken);

        await requesterChunks.AssertNoMessageAsync(cancellationToken);
        await ownerChunks.AssertNoMessageAsync(cancellationToken);
        await unrelatedChunks.AssertNoMessageAsync(cancellationToken);

        await ownerConnection.InvokeAsync("RelaySendChunk",
            new RelayChunk(request.Id, ChunkIndex: 2, IsEof: true, Data: [1, 2, 3, 4]), cancellationToken);

        var routedChunk = await requesterChunks.ReadAsync(cancellationToken);
        Assert.Equal(request.Id, routedChunk.FileRequestId);
        Assert.Equal(2, routedChunk.ChunkIndex);
        Assert.True(routedChunk.IsEof);
        Assert.Equal([1, 2, 3, 4], routedChunk.Data);
        await ownerChunks.AssertNoMessageAsync(cancellationToken);
        await unrelatedChunks.AssertNoMessageAsync(cancellationToken);
    }

    [Fact]
    public async Task RelaySendChunk_DropsPayloadLargerThanGuardLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-relay-oversize",
            cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterChunks = new EventProbe<RelayChunk>();
        requesterConnection.On<RelayChunk>("RelayReceiveChunk", requesterChunks.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        await ownerConnection.InvokeAsync("RelaySendChunk",
            new RelayChunk(request.Id, ChunkIndex: 0, IsEof: false, Data: new byte[32 * 1024 + 1]), cancellationToken);

        await requesterChunks.AssertNoMessageAsync(cancellationToken);
    }

    [Fact]
    public async Task RelaySendChunk_RequestNotFound_DoesNotForward()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requester = await _api.RegisterAsync($"requester-{Guid.NewGuid():N}", "owner-a", cancellationToken);
        var owner = await _api.RegisterAsync($"owner-{Guid.NewGuid():N}", "owner-b", cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterChunks = new EventProbe<RelayChunk>();
        requesterConnection.On<RelayChunk>("RelayReceiveChunk", requesterChunks.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        await ownerConnection.InvokeAsync("RelaySendChunk",
            new RelayChunk(Guid.NewGuid(), ChunkIndex: 0, IsEof: false, Data: [1, 2, 3]), cancellationToken);

        await requesterChunks.AssertNoMessageAsync(cancellationToken);
    }

    [Fact]
    public async Task ReportTransferProgress_RequestNotFound_DoesNotBroadcastToGroups()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);

        await using var requesterPlugin = CreateHubConnection(requester.ApiKey);
        await using var requesterWeb = CreateHubConnection(requester.ApiKey, webClient: true);
        await using var ownerWeb = CreateHubConnection(owner.ApiKey, webClient: true);
        var requesterProgress = new EventProbe<TransferProgress>();
        var ownerProgress = new EventProbe<TransferProgress>();
        requesterWeb.On<TransferProgress>("TransferProgress", requesterProgress.Add);
        ownerWeb.On<TransferProgress>("TransferProgress", ownerProgress.Add);

        await requesterPlugin.StartAsync(cancellationToken);
        await requesterWeb.StartAsync(cancellationToken);
        await ownerWeb.StartAsync(cancellationToken);

        await requesterPlugin.InvokeAsync("ReportTransferProgress",
            new TransferProgress(Guid.NewGuid(), 10, 100), cancellationToken);

        await requesterProgress.AssertNoMessageAsync(cancellationToken);
        await ownerProgress.AssertNoMessageAsync(cancellationToken);
    }

    [Fact]
    public async Task TransportSelection_PrefersQuicForLargeFiles_WhenBothPeersSupportQuic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        const string jellyfinItemId = "item-quic-threshold";
        await SyncOwnerItemAsync(owner.ApiKey, jellyfinItemId, fileSizeBytes: 5_000, cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, jellyfinItemId, cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterPunchRequests = new EventProbe<HolePunchRequest>();
        var ownerPunchRequests = new EventProbe<HolePunchRequest>();
        requesterConnection.On<HolePunchRequest>("HolePunchRequest", requesterPunchRequests.Add);
        ownerConnection.On<HolePunchRequest>("HolePunchRequest", ownerPunchRequests.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        await ownerConnection.InvokeAsync(
            "ReportHolePunchReady",
            new HolePunchReady(request.Id, UdpPort: 41000, OverridePublicIp: "127.0.0.1", SupportsQuic: true,
                LargeFileThresholdBytes: 4_000),
            cancellationToken);
        await requesterConnection.InvokeAsync(
            "ReportHolePunchReady",
            new HolePunchReady(request.Id, UdpPort: 41001, OverridePublicIp: "127.0.0.1", SupportsQuic: true,
                LargeFileThresholdBytes: 4_000),
            cancellationToken);

        var ownerDispatch = await ownerPunchRequests.ReadAsync(cancellationToken);
        var requesterDispatch = await requesterPunchRequests.ReadAsync(cancellationToken);

        Assert.Equal(TransferTransportMode.Quic, ownerDispatch.SelectedTransportMode);
        Assert.Equal(TransferSelectionReason.LargeFileQuic, ownerDispatch.TransportSelectionReason);
        Assert.Equal(TransferTransportMode.Quic, requesterDispatch.SelectedTransportMode);
        Assert.Equal(TransferSelectionReason.LargeFileQuic, requesterDispatch.TransportSelectionReason);
    }

    [Fact]
    public async Task TransportSelection_FallsBackToArq_WhenAnyPeerDoesNotSupportQuic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        const string jellyfinItemId = "item-arq-fallback";
        await SyncOwnerItemAsync(owner.ApiKey, jellyfinItemId, fileSizeBytes: 9_000, cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, jellyfinItemId, cancellationToken);

        await using var requesterConnection = CreateHubConnection(requester.ApiKey);
        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        var requesterPunchRequests = new EventProbe<HolePunchRequest>();
        requesterConnection.On<HolePunchRequest>("HolePunchRequest", requesterPunchRequests.Add);

        await requesterConnection.StartAsync(cancellationToken);
        await ownerConnection.StartAsync(cancellationToken);

        await ownerConnection.InvokeAsync(
            "ReportHolePunchReady",
            new HolePunchReady(request.Id, UdpPort: 42000, OverridePublicIp: "127.0.0.1", SupportsQuic: false,
                LargeFileThresholdBytes: 1_000),
            cancellationToken);
        await requesterConnection.InvokeAsync(
            "ReportHolePunchReady",
            new HolePunchReady(request.Id, UdpPort: 42001, OverridePublicIp: "127.0.0.1", SupportsQuic: true,
                LargeFileThresholdBytes: 1_000),
            cancellationToken);

        var dispatch = await requesterPunchRequests.ReadAsync(cancellationToken);
        Assert.Equal(TransferTransportMode.ArqUdp, dispatch.SelectedTransportMode);
        Assert.Equal(TransferSelectionReason.QuicUnsupportedPeer, dispatch.TransportSelectionReason);
    }

    [Fact]
    public async Task ReportHolePunchResult_FailureMarksRequestAsFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-hp-fail", cancellationToken);

        await using var ownerConnection = CreateHubConnection(owner.ApiKey);
        await ownerConnection.StartAsync(cancellationToken);

        await ownerConnection.InvokeAsync(
            "ReportHolePunchResult",
            new HolePunchResult(
                request.Id,
                Success: false,
                Error: "timeout",
                Failure: FailureDescriptor.Timeout("holepunch.timeout", "Timed out")),
            cancellationToken);

        var updated = await GetRequestAsync(requester.ApiKey, request.Id, cancellationToken);
        Assert.Equal(FileRequestStatus.Failed, updated.Status);
        Assert.Equal(TransferFailureCategory.Connectivity, updated.FailureCategory);
        Assert.Contains("Timed out", updated.FailureReason);
    }

    [Fact]
    public async Task ReportHolePunchResult_FromNonParticipant_DoesNotMutateRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        var unrelated = await _api.RegisterAsync($"unrelated-{Guid.NewGuid():N}", "owner-z", cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-hp-unauthorized", cancellationToken);

        await using var unrelatedConnection = CreateHubConnection(unrelated.ApiKey);
        await unrelatedConnection.StartAsync(cancellationToken);

        await unrelatedConnection.InvokeAsync(
            "ReportHolePunchResult",
            new HolePunchResult(request.Id, Success: false, Error: "malicious"),
            cancellationToken);

        var unchanged = await GetRequestAsync(requester.ApiKey, request.Id, cancellationToken);
        Assert.Equal(FileRequestStatus.Pending, unchanged.Status);
    }

    [Fact]
    public async Task TransferProgressIsVisibleToBothServerGroups()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (requester, owner) = await RegisterPairedServersAsync(cancellationToken);
        var request = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-progress", cancellationToken);

        await using var requesterPlugin = CreateHubConnection(requester.ApiKey);
        await using var requesterWeb = CreateHubConnection(requester.ApiKey, webClient: true);
        await using var ownerWeb = CreateHubConnection(owner.ApiKey, webClient: true);
        var requesterProgress = new EventProbe<TransferProgress>();
        var ownerProgress = new EventProbe<TransferProgress>();
        requesterWeb.On<TransferProgress>("TransferProgress", requesterProgress.Add);
        ownerWeb.On<TransferProgress>("TransferProgress", ownerProgress.Add);

        await requesterPlugin.StartAsync(cancellationToken);
        await requesterWeb.StartAsync(cancellationToken);
        await ownerWeb.StartAsync(cancellationToken);

        var expectedProgress = new TransferProgress(request.Id, 512, 1024);
        var requesterUpdateTask = ReadUntilAsync(requesterProgress, progress => progress == expectedProgress, cancellationToken);
        var ownerUpdateTask = ReadUntilAsync(ownerProgress, progress => progress == expectedProgress, cancellationToken);

        // Long-polling clients can complete StartAsync just before their receive poll is established on
        // slower CI runners. Repeat the transient progress notification until both server groups observe it.
        while (!requesterUpdateTask.IsCompleted || !ownerUpdateTask.IsCompleted)
        {
            await requesterPlugin.InvokeAsync("ReportTransferProgress", expectedProgress, cancellationToken);

            var bothUpdates = Task.WhenAll(requesterUpdateTask, ownerUpdateTask);
            var bothClientsObservedUpdate = true;
            try
            {
                await bothUpdates.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
            catch (TimeoutException)
            {
                bothClientsObservedUpdate = false;
            }

            if (bothClientsObservedUpdate)
                break;
        }

        var requesterUpdate = await requesterUpdateTask;
        var ownerUpdate = await ownerUpdateTask;
        Assert.Equal(requesterUpdate, ownerUpdate);
    }

    private async Task<(RegisterServerResponse Requester, RegisterServerResponse Owner)> RegisterPairedServersAsync(
        CancellationToken cancellationToken)
    {
        var requester = await _api.RegisterAsync($"requester-{Guid.NewGuid():N}", "owner-a", cancellationToken);
        var owner = await _api.RegisterAsync($"owner-{Guid.NewGuid():N}", "owner-b", cancellationToken);

        var invitation = await _api.SendInvitationAsync(requester.ApiKey, owner.ServerId, cancellationToken);
        var acceptResponse = await _api.RespondInvitationAsync(owner.ApiKey, invitation.Id, true, cancellationToken);
        acceptResponse.EnsureSuccessStatusCode();

        return (requester, owner);
    }

    private HubConnection CreateHubConnection(string apiKey, bool webClient = false)
    {
        var clientQuery = webClient ? "?client=web" : string.Empty;
        var hubUrl = new Uri(_http.BaseAddress!, $"/hubs/federation{clientQuery}");

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers.Add("X-Api-Key", apiKey);
            })
            .Build();
    }

    private HubConnection CreateCookieHubConnection(string setCookieHeader)
    {
        var cookie = setCookieHeader.Split(';', 2)[0];
        var hubUrl = new Uri(_http.BaseAddress!, "/hubs/federation?client=web");

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers.Add("Cookie", cookie);
            })
            .Build();
    }

    private HubConnection CreateBrowserAccessTokenHubConnection(string apiKey)
    {
        var hubUrl = new Uri(_http.BaseAddress!,
            $"/hubs/federation?client=web&access_token={Uri.EscapeDataString(apiKey)}");

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    private async Task SyncOwnerItemAsync(string apiKey, string jellyfinItemId, long fileSizeBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/library/sync")
        {
            Content = JsonContent.Create(new SyncMediaRequest([
                new MediaItemSyncEntry(jellyfinItemId, jellyfinItemId, MediaType.Movie, 2024, null, null, fileSizeBytes)
            ]))
        };
        request.Headers.Add("X-Api-Key", apiKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<RequestSnapshot> GetRequestAsync(string apiKey, Guid requestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/filerequests");
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var element = doc.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == requestId);

        var status = Enum.Parse<FileRequestStatus>(element.GetProperty("status").GetString()!, ignoreCase: true);
        var failureCategory = element.TryGetProperty("failureCategory", out var categoryElement) &&
                              categoryElement.ValueKind == JsonValueKind.String
            ? Enum.Parse<TransferFailureCategory>(categoryElement.GetString()!, ignoreCase: true)
            : (TransferFailureCategory?)null;
        var failureReason = element.TryGetProperty("failureReason", out var reasonElement) &&
                            reasonElement.ValueKind == JsonValueKind.String
            ? reasonElement.GetString()
            : null;

        return new RequestSnapshot(status, failureCategory, failureReason);
    }

    private static async Task<T> ReadUntilAsync<T>(EventProbe<T> probe, Func<T, bool> predicate,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        while (true)
        {
            var item = await probe.ReadAsync(timeout.Token);
            if (predicate(item))
                return item;
        }
    }

    private sealed record RequestSnapshot(
        FileRequestStatus Status,
        TransferFailureCategory? FailureCategory,
        string? FailureReason);

    private sealed class EventProbe<T>
    {
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan NoMessageTimeout = TimeSpan.FromMilliseconds(300);
        private readonly Channel<T> _channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        public void Add(T message)
        {
            Assert.True(_channel.Writer.TryWrite(message));
        }

        public async Task<T> ReadAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReceiveTimeout);
            return await _channel.Reader.ReadAsync(timeout.Token);
        }

        public async Task AssertNoMessageAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(NoMessageTimeout);

            try
            {
                var message = await _channel.Reader.ReadAsync(timeout.Token);
                Assert.Fail($"Expected no {typeof(T).Name} message, but received {message}.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Expected: no message arrived before the short assertion window elapsed.
                return;
            }
        }
    }
}

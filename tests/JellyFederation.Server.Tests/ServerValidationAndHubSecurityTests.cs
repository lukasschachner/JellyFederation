using System.Net;
using System.Net.Http.Json;
using System.Threading;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class ServerValidationAndHubSecurityTests : IAsyncLifetime
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
    public async Task Register_InvalidPayload_Returns_ValidationEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _http.PostAsJsonAsync("/api/servers/register",
            new RegisterServerRequest(string.Empty, "owner-a"), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("request.validation_failed", payload!.Error.Code);
        Assert.Equal(nameof(FailureCategory.Validation), payload.Error.Category);
    }

    [Fact]
    public async Task Mine_InvalidPagination_Returns_ValidationEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var registered = await _api.RegisterAsync("server-a", "owner-a", cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/library/mine?page=0&pageSize=1000");
        request.Headers.Add("X-Api-Key", registered.ApiKey);

        var response = await _http.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("library.pagination.invalid", payload!.Error.Code);
        Assert.Equal(nameof(FailureCategory.Validation), payload.Error.Category);
    }

    [Fact]
    public async Task ForwardIceSignal_From_NonParticipant_IsDropped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var a = await _api.RegisterAsync("server-a", "owner-a", cancellationToken);
        var b = await _api.RegisterAsync("server-b", "owner-b", cancellationToken);
        var c = await _api.RegisterAsync("server-c", "owner-c", cancellationToken);

        var invitation = await _api.SendInvitationAsync(a.ApiKey, b.ServerId, cancellationToken);

        var acceptResponse = await _api.RespondInvitationAsync(b.ApiKey, invitation.Id, true, cancellationToken);
        acceptResponse.EnsureSuccessStatusCode();

        var fileRequest = await _api.CreateFileRequestAsync(a.ApiKey, b.ServerId, "jelly-item-1", cancellationToken);

        await using var connA = CreateHubConnection(a.ApiKey);
        await using var connC = CreateHubConnection(c.ApiKey);

        var iceReceivedByA = new TaskCompletionSource<IceSignal>(TaskCreationOptions.RunContinuationsAsynchronously);
        connA.On<IceSignal>("IceSignal", signal => iceReceivedByA.TrySetResult(signal));

        await connA.StartAsync(cancellationToken);
        await connC.StartAsync(cancellationToken);

        var maliciousSignal = new IceSignal(fileRequest.Id, IceSignalType.Candidate, "fake-candidate");
        await connC.InvokeAsync("ForwardIceSignal", maliciousSignal, cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => iceReceivedByA.Task.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task ReportTransferProgress_From_NonParticipant_IsDropped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var a = await _api.RegisterAsync("server-a", "owner-a", cancellationToken);
        var b = await _api.RegisterAsync("server-b", "owner-b", cancellationToken);
        var c = await _api.RegisterAsync("server-c", "owner-c", cancellationToken);

        var invitation = await _api.SendInvitationAsync(a.ApiKey, b.ServerId, cancellationToken);
        var acceptResponse = await _api.RespondInvitationAsync(b.ApiKey, invitation.Id, true, cancellationToken);
        acceptResponse.EnsureSuccessStatusCode();

        var fileRequest = await _api.CreateFileRequestAsync(a.ApiKey, b.ServerId, "jelly-item-2", cancellationToken);

        await using var connA = CreateHubConnection(a.ApiKey);
        await using var connC = CreateHubConnection(c.ApiKey);

        var progressReceivedByA = new TaskCompletionSource<TransferProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        connA.On<TransferProgress>("TransferProgress", progress => progressReceivedByA.TrySetResult(progress));

        await connA.StartAsync(cancellationToken);
        await connC.StartAsync(cancellationToken);

        await connC.InvokeAsync("ReportTransferProgress", new TransferProgress(fileRequest.Id, 123, 1000),
            cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => progressReceivedByA.Task.WaitAsync(timeout.Token));
    }

    private HubConnection CreateHubConnection(string apiKey)
    {
        var hubUrl = new Uri(_http.BaseAddress!, $"/hubs/federation?apiKey={Uri.EscapeDataString(apiKey)}");

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }
}

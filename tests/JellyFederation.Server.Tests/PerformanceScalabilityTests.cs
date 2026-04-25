using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JellyFederation.Data;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class PerformanceScalabilityTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
    public async Task FileRequests_List_IsPagedWithTotalHeaders()
    {
        var ct = TestContext.Current.CancellationToken;
        var requester = CreateServer("page-requester");
        var owner = CreateServer("page-owner");
        await SeedAsync(db =>
        {
            db.Servers.AddRange(requester, owner);
            db.Invitations.Add(new Invitation
            {
                FromServerId = requester.Id,
                ToServerId = owner.Id,
                Status = InvitationStatus.Accepted
            });
            for (var i = 0; i < 55; i++)
                db.FileRequests.Add(new FileRequest
                {
                    RequestingServerId = requester.Id,
                    OwningServerId = owner.Id,
                    JellyfinItemId = $"item-{i:D3}",
                    CreatedAt = DateTime.UtcNow.AddSeconds(-i)
                });
        }, ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/filerequests?page=1&pageSize=50");
        request.Headers.Add("X-Api-Key", requester.ApiKey);
        var response = await _http.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<FileRequestDto>>(JsonOptions, ct);
        Assert.NotNull(items);
        Assert.Equal(50, items!.Count);
        Assert.Equal("55", response.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", response.Headers.GetValues("X-Page").Single());
        Assert.Equal("50", response.Headers.GetValues("X-Page-Size").Single());
        Assert.Equal("2", response.Headers.GetValues("X-Total-Pages").Single());
    }

    [Fact]
    public async Task Invitations_List_IsPagedWithTotalHeadersAndRejectsInvalidPageSize()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = CreateServer("invite-source");
        await SeedAsync(db =>
        {
            db.Servers.Add(source);
            for (var i = 0; i < 55; i++)
            {
                var target = CreateServer($"invite-target-{i:D3}");
                db.Servers.Add(target);
                db.Invitations.Add(new Invitation
                {
                    FromServerId = source.Id,
                    ToServerId = target.Id,
                    CreatedAt = DateTime.UtcNow.AddSeconds(-i)
                });
            }
        }, ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invitations?page=1&pageSize=50");
        request.Headers.Add("X-Api-Key", source.ApiKey);
        var response = await _http.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<InvitationDto>>(JsonOptions, ct);
        Assert.NotNull(items);
        Assert.Equal(50, items!.Count);
        Assert.Equal("55", response.Headers.GetValues("X-Total-Count").Single());

        using var invalid = new HttpRequestMessage(HttpMethod.Get, "/api/invitations?page=1&pageSize=10000");
        invalid.Headers.Add("X-Api-Key", source.ApiKey);
        var invalidResponse = await _http.SendAsync(invalid, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        var failure = await invalidResponse.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions, ct);
        Assert.Equal("invitation.pagination.invalid", failure!.Error.Code);
    }

    [Fact]
    public async Task Servers_List_IsPagedWithTotalHeaders()
    {
        var ct = TestContext.Current.CancellationToken;
        var servers = Enumerable.Range(0, 55)
            .Select(i => CreateServer($"server-page-{i:D3}", DateTime.UtcNow.AddSeconds(-i)))
            .ToList();
        await SeedAsync(db => db.Servers.AddRange(servers), ct);
        var apiKey = servers[0].ApiKey;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/servers?page=1&pageSize=50");
        request.Headers.Add("X-Api-Key", apiKey);
        var response = await _http.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<ServerInfoDto>>(JsonOptions, ct);
        Assert.NotNull(items);
        Assert.Equal(50, items!.Count);
        Assert.True(int.Parse(response.Headers.GetValues("X-Total-Count").Single()) >= 55);
    }

    [Fact]
    public async Task LibrarySync_RejectsDuplicatesAndReplaceAllDeletesStaleRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var server = await _api.RegisterAsync("sync-server", "sync-owner", ct);

        using var duplicateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/library/sync")
        {
            Content = JsonContent.Create(new SyncMediaRequest([
                new MediaItemSyncEntry("dup", "Duplicate A", MediaType.Movie, 2024, null, null, 1),
                new MediaItemSyncEntry("dup", "Duplicate B", MediaType.Movie, 2024, null, null, 1)
            ]))
        };
        duplicateRequest.Headers.Add("X-Api-Key", server.ApiKey);
        var duplicateResponse = await _http.SendAsync(duplicateRequest, ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        var duplicateFailure = await duplicateResponse.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions, ct);
        Assert.Equal("library.sync.duplicate_item", duplicateFailure!.Error.Code);

        await SyncAsync(server.ApiKey, true, [
            new MediaItemSyncEntry("keep", "Keep", MediaType.Movie, 2024, null, null, 1),
            new MediaItemSyncEntry("stale", "Stale", MediaType.Movie, 2024, null, null, 1)
        ], ct);

        await SyncAsync(server.ApiKey, true, [
            new MediaItemSyncEntry("keep", "Keep Updated", MediaType.Series, 2025, null, null, 2)
        ], ct);

        using var mineRequest = new HttpRequestMessage(HttpMethod.Get, "/api/library/mine?page=1&pageSize=10");
        mineRequest.Headers.Add("X-Api-Key", server.ApiKey);
        var mineResponse = await _http.SendAsync(mineRequest, ct);
        mineResponse.EnsureSuccessStatusCode();
        var mine = await mineResponse.Content.ReadFromJsonAsync<List<MediaItemDto>>(JsonOptions, ct);
        Assert.NotNull(mine);
        var item = Assert.Single(mine!);
        Assert.Equal("keep", item.JellyfinItemId);
        Assert.Equal("Keep Updated", item.Title);
    }

    private async Task SyncAsync(string apiKey, bool replaceAll, List<MediaItemSyncEntry> items, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/library/sync")
        {
            Content = JsonContent.Create(new SyncMediaRequest(items, replaceAll))
        };
        request.Headers.Add("X-Api-Key", apiKey);
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task SeedAsync(Action<FederationDbContext> seed, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
        seed(db);
        await db.SaveChangesAsync(ct);
    }

    private static RegisteredServer CreateServer(string name, DateTime? registeredAt = null) => new()
    {
        Name = name,
        OwnerUserId = $"owner-{name}",
        ApiKey = $"api-key-{Guid.NewGuid():N}",
        RegisteredAt = registeredAt ?? DateTime.UtcNow,
        LastSeenAt = registeredAt ?? DateTime.UtcNow
    };
}

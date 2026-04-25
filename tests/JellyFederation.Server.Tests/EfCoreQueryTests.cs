using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JellyFederation.Data;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class EfCoreQueryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task FileRequestList_Resolves_ItemTitle_ByOwningServerAndJellyfinItemId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestServerFactory();
        using var http = factory.CreateClient();
        var api = new TestApiClient(http);

        var requestingServer = await api.RegisterAsync("requesting", "owner-a", cancellationToken);
        var owningServer = await api.RegisterAsync("owning", "owner-b", cancellationToken);
        var unrelatedServer = await api.RegisterAsync("unrelated", "owner-c", cancellationToken);

        await SyncSingleItemAsync(http, requestingServer.ApiKey, "shared-jellyfin-id", "Wrong local title", cancellationToken);
        await SyncSingleItemAsync(http, owningServer.ApiKey, "shared-jellyfin-id", "Correct owning title", cancellationToken);
        await SyncSingleItemAsync(http, unrelatedServer.ApiKey, "shared-jellyfin-id", "Wrong unrelated title", cancellationToken);

        var invitation = await api.SendInvitationAsync(requestingServer.ApiKey, owningServer.ServerId, cancellationToken);
        var acceptResponse = await api.RespondInvitationAsync(owningServer.ApiKey, invitation.Id, accept: true, cancellationToken);
        acceptResponse.EnsureSuccessStatusCode();

        var created = await api.CreateFileRequestAsync(
            requestingServer.ApiKey,
            owningServer.ServerId,
            "shared-jellyfin-id",
            cancellationToken);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/filerequests");
        listRequest.Headers.Add("X-Api-Key", requestingServer.ApiKey);
        var listResponse = await http.SendAsync(listRequest, cancellationToken);
        listResponse.EnsureSuccessStatusCode();

        var requests = await listResponse.Content.ReadFromJsonAsync<List<FileRequestDto>>(JsonOptions, cancellationToken);

        var dto = Assert.Single(requests!, r => r.Id == created.Id);
        Assert.Equal(owningServer.ServerId, dto.OwningServerId);
        Assert.Equal("shared-jellyfin-id", dto.JellyfinItemId);
        Assert.Equal("Correct owning title", dto.ItemTitle);
    }

    [Fact]
    public async Task Browse_Returns_OnlyRequestableMedia_FromAcceptedInvitations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestServerFactory();
        using var http = factory.CreateClient();
        var api = new TestApiClient(http);

        var browsingServer = await api.RegisterAsync("browser", "owner-a", cancellationToken);
        var acceptedPeer = await api.RegisterAsync("accepted-peer", "owner-b", cancellationToken);
        var pendingPeer = await api.RegisterAsync("pending-peer", "owner-c", cancellationToken);
        var declinedPeer = await api.RegisterAsync("declined-peer", "owner-d", cancellationToken);
        var unrelatedPeer = await api.RegisterAsync("unrelated-peer", "owner-e", cancellationToken);

        await SyncSingleItemAsync(http, acceptedPeer.ApiKey, "accepted", "Accepted title", cancellationToken);
        await SyncSingleItemAsync(http, pendingPeer.ApiKey, "pending", "Pending title", cancellationToken);
        await SyncSingleItemAsync(http, declinedPeer.ApiKey, "declined", "Declined title", cancellationToken);
        await SyncSingleItemAsync(http, unrelatedPeer.ApiKey, "unrelated", "Unrelated title", cancellationToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
            db.Invitations.AddRange(
                new Invitation
                {
                    FromServerId = browsingServer.ServerId,
                    ToServerId = acceptedPeer.ServerId,
                    Status = InvitationStatus.Accepted,
                    RespondedAt = DateTime.UtcNow
                },
                new Invitation
                {
                    FromServerId = browsingServer.ServerId,
                    ToServerId = pendingPeer.ServerId,
                    Status = InvitationStatus.Pending
                },
                new Invitation
                {
                    FromServerId = browsingServer.ServerId,
                    ToServerId = declinedPeer.ServerId,
                    Status = InvitationStatus.Declined,
                    RespondedAt = DateTime.UtcNow
                });

            var acceptedHiddenItem = new MediaItem
            {
                ServerId = acceptedPeer.ServerId,
                JellyfinItemId = "accepted-hidden",
                Title = "Accepted hidden title",
                Type = MediaType.Movie,
                IsRequestable = false
            };
            db.MediaItems.Add(acceptedHiddenItem);
            await db.SaveChangesAsync(cancellationToken);
        }

        using var browseRequest = new HttpRequestMessage(HttpMethod.Get, "/api/library");
        browseRequest.Headers.Add("X-Api-Key", browsingServer.ApiKey);
        var browseResponse = await http.SendAsync(browseRequest, cancellationToken);
        browseResponse.EnsureSuccessStatusCode();

        var items = await browseResponse.Content.ReadFromJsonAsync<List<MediaItemDto>>(JsonOptions, cancellationToken);

        var item = Assert.Single(items!);
        Assert.Equal(acceptedPeer.ServerId, item.ServerId);
        Assert.Equal("accepted", item.JellyfinItemId);
        Assert.Equal("Accepted title", item.Title);
        Assert.True(item.IsRequestable);
        Assert.Equal("1", browseResponse.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task StaleCleanupEligibility_IncludesOnlyOldInFlightRequests_AndTrackedUpdatesPersist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        await using var db = database.CreateContext();

        var requester = CreateServer("requester");
        var owner = CreateServer("owner");
        db.Servers.AddRange(requester, owner);

        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
        var eligiblePending = CreateRequest(requester.Id, owner.Id, FileRequestStatus.Pending, cutoff.AddMinutes(-1));
        var eligibleHolePunching = CreateRequest(requester.Id, owner.Id, FileRequestStatus.HolePunching, cutoff.AddMinutes(-2));
        var eligibleTransferring = CreateRequest(requester.Id, owner.Id, FileRequestStatus.Transferring, cutoff.AddMinutes(-3));
        var tooNewPending = CreateRequest(requester.Id, owner.Id, FileRequestStatus.Pending, cutoff.AddTicks(1));
        var oldCompleted = CreateRequest(requester.Id, owner.Id, FileRequestStatus.Completed, cutoff.AddMinutes(-4));
        var oldFailed = CreateRequest(requester.Id, owner.Id, FileRequestStatus.Failed, cutoff.AddMinutes(-5));
        var oldCancelled = CreateRequest(requester.Id, owner.Id, FileRequestStatus.Cancelled, cutoff.AddMinutes(-6));
        db.FileRequests.AddRange(
            eligiblePending,
            eligibleHolePunching,
            eligibleTransferring,
            tooNewPending,
            oldCompleted,
            oldFailed,
            oldCancelled);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var stale = await db.FileRequests
            .AsTracking()
            .Where(r =>
                (r.Status == FileRequestStatus.Pending ||
                 r.Status == FileRequestStatus.HolePunching ||
                 r.Status == FileRequestStatus.Transferring) &&
                r.CreatedAt < cutoff)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        Assert.Equal(
            [eligibleTransferring.Id, eligibleHolePunching.Id, eligiblePending.Id],
            stale.Select(r => r.Id).ToArray());

        foreach (var request in stale)
        {
            request.Status = FileRequestStatus.Failed;
            request.FailureReason = $"Timed out from {request.Status}.";
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var statuses = await db.FileRequests
            .AsNoTracking()
            .OrderBy(r => r.JellyfinItemId)
            .Select(r => new { r.Id, r.Status, r.FailureReason })
            .ToListAsync(cancellationToken);

        Assert.All(statuses.Where(r => stale.Select(s => s.Id).Contains(r.Id)), r =>
        {
            Assert.Equal(FileRequestStatus.Failed, r.Status);
            Assert.NotNull(r.FailureReason);
        });
        Assert.Equal(FileRequestStatus.Pending, statuses.Single(r => r.Id == tooNewPending.Id).Status);
        Assert.Equal(FileRequestStatus.Completed, statuses.Single(r => r.Id == oldCompleted.Id).Status);
        Assert.Equal(FileRequestStatus.Failed, statuses.Single(r => r.Id == oldFailed.Id).Status);
        Assert.Equal(FileRequestStatus.Cancelled, statuses.Single(r => r.Id == oldCancelled.Id).Status);
    }

    [Fact]
    public async Task MediaItems_EnforceUniqueJellyfinItemIdPerServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        await using var db = database.CreateContext();
        var server = CreateServer("unique-media-server");
        db.Servers.Add(server);
        db.MediaItems.AddRange(
            new MediaItem
            {
                ServerId = server.Id,
                JellyfinItemId = "same-item",
                Title = "First",
                Type = MediaType.Movie
            },
            new MediaItem
            {
                ServerId = server.Id,
                JellyfinItemId = "same-item",
                Title = "Second",
                Type = MediaType.Movie
            });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task ModelMetadata_ContainsImportantQueryIndexes()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var db = database.CreateContext();

        AssertIndex<RegisteredServer>(db, isUnique: true, nameof(RegisteredServer.ApiKey));
        AssertIndex<RegisteredServer>(db, isUnique: false, nameof(RegisteredServer.RegisteredAt), nameof(RegisteredServer.Id));
        AssertIndex<MediaItem>(db, isUnique: true, nameof(MediaItem.ServerId), nameof(MediaItem.JellyfinItemId));
        AssertIndex<MediaItem>(db, isUnique: false, nameof(MediaItem.ServerId), nameof(MediaItem.Type));
        AssertIndex<MediaItem>(db, isUnique: false, nameof(MediaItem.ServerId), nameof(MediaItem.Title));
        AssertIndex<MediaItem>(db, isUnique: false, nameof(MediaItem.ServerId), nameof(MediaItem.IndexedAt));
        AssertIndex<Invitation>(db, isUnique: false, nameof(Invitation.FromServerId), nameof(Invitation.Status));
        AssertIndex<Invitation>(db, isUnique: false, nameof(Invitation.ToServerId), nameof(Invitation.Status));
        AssertIndex<Invitation>(db, isUnique: false, nameof(Invitation.FromServerId), nameof(Invitation.CreatedAt), nameof(Invitation.Id));
        AssertIndex<Invitation>(db, isUnique: false, nameof(Invitation.ToServerId), nameof(Invitation.CreatedAt), nameof(Invitation.Id));
        AssertIndex<FileRequest>(db, isUnique: false, nameof(FileRequest.RequestingServerId), nameof(FileRequest.Status));
        AssertIndex<FileRequest>(db, isUnique: false, nameof(FileRequest.OwningServerId), nameof(FileRequest.Status));
        AssertIndex<FileRequest>(db, isUnique: false, nameof(FileRequest.Status), nameof(FileRequest.CreatedAt));
        AssertIndex<FileRequest>(db, isUnique: false, nameof(FileRequest.RequestingServerId), nameof(FileRequest.CreatedAt), nameof(FileRequest.Id));
        AssertIndex<FileRequest>(db, isUnique: false, nameof(FileRequest.OwningServerId), nameof(FileRequest.CreatedAt), nameof(FileRequest.Id));
    }

    private static async Task SyncSingleItemAsync(
        HttpClient http,
        string apiKey,
        string jellyfinItemId,
        string title,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/library/sync")
        {
            Content = JsonContent.Create(new SyncMediaRequest([
                new MediaItemSyncEntry(jellyfinItemId, title, MediaType.Movie, 2024, null, null, 1024)
            ]))
        };
        request.Headers.Add("X-Api-Key", apiKey);

        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static RegisteredServer CreateServer(string name) => new()
    {
        Name = name,
        OwnerUserId = $"owner-{name}",
        ApiKey = $"api-key-{Guid.NewGuid():N}"
    };

    private static FileRequest CreateRequest(
        Guid requestingServerId,
        Guid owningServerId,
        FileRequestStatus status,
        DateTime createdAt) => new()
    {
        RequestingServerId = requestingServerId,
        OwningServerId = owningServerId,
        JellyfinItemId = $"item-{Guid.NewGuid():N}",
        Status = status,
        CreatedAt = createdAt
    };

    private static void AssertIndex<TEntity>(FederationDbContext db, bool isUnique, params string[] propertyNames)
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);

        var index = entityType!.GetIndexes().SingleOrDefault(i => HasProperties(i, propertyNames));
        Assert.NotNull(index);
        Assert.Equal(isUnique, index!.IsUnique);
    }

    private static bool HasProperties(IIndex index, string[] propertyNames) =>
        index.Properties.Select(p => p.Name).SequenceEqual(propertyNames);

    private sealed class SqliteTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<FederationDbContext> _options;

        private SqliteTestDatabase(SqliteConnection connection, DbContextOptions<FederationDbContext> options)
        {
            _connection = connection;
            _options = options;
        }

        public static async Task<SqliteTestDatabase> CreateAsync(CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<FederationDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var context = new FederationDbContext(options);
            await context.Database.EnsureCreatedAsync(cancellationToken);

            return new SqliteTestDatabase(connection, options);
        }

        public FederationDbContext CreateContext() => new(_options);

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }
}

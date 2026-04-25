using JellyFederation.Data;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class InvitationServiceTests
{
    [Fact]
    public async Task SendAsync_CreatesPendingInvitation_WhenNoRelationshipExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        await using var db = database.CreateContext();
        var service = new InvitationService(db);
        var fromServer = CreateServer("from");
        var toServer = CreateServer("to");
        db.Servers.AddRange(fromServer, toServer);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = await service.SendAsync(
            fromServer,
            new SendInvitationRequest(toServer.Id),
            "corr-send",
            cancellationToken);

        Assert.True(outcome.IsSuccess);
        var invitation = outcome.RequireValue();
        Assert.Equal(fromServer.Id, invitation.FromServerId);
        Assert.Equal(toServer.Id, invitation.ToServerId);
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
        Assert.Equal("from", invitation.FromServerName);
        Assert.Equal("to", invitation.ToServerName);
    }

    [Fact]
    public async Task SendAsync_ReturnsConflict_WhenPendingOrAcceptedRelationshipExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        await using var db = database.CreateContext();
        var service = new InvitationService(db);
        var fromServer = CreateServer("from");
        var toServer = CreateServer("to");
        db.Servers.AddRange(fromServer, toServer);
        db.Invitations.Add(new Invitation
        {
            FromServerId = toServer.Id,
            ToServerId = fromServer.Id,
            Status = InvitationStatus.Accepted,
            RespondedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        var outcome = await service.SendAsync(
            fromServer,
            new SendInvitationRequest(toServer.Id),
            "corr-conflict",
            cancellationToken);

        Assert.True(outcome.IsFailure);
        Assert.Equal("invitation.relationship_exists", outcome.Failure!.Code);
        Assert.Equal(FailureCategory.Conflict, outcome.Failure.Category);
        Assert.Equal("corr-conflict", outcome.Failure.CorrelationId);
    }

    [Fact]
    public async Task RespondAsync_AcceptsOnlyPendingInvitationAddressedToServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        await using var db = database.CreateContext();
        var service = new InvitationService(db);
        var fromServer = CreateServer("from");
        var toServer = CreateServer("to");
        db.Servers.AddRange(fromServer, toServer);
        var invitation = new Invitation
        {
            FromServerId = fromServer.Id,
            ToServerId = toServer.Id,
            Status = InvitationStatus.Pending
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = await service.RespondAsync(
            toServer,
            invitation.Id,
            new RespondToInvitationRequest(true),
            "corr-respond",
            cancellationToken);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(InvitationStatus.Accepted, outcome.RequireValue().Status);
        var persisted = await db.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitation.Id, cancellationToken);
        Assert.NotNull(persisted.RespondedAt);
    }

    [Fact]
    public async Task RevokeAsync_ReturnsNotFound_WhenInvitationIsNotOwnedByServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        await using var db = database.CreateContext();
        var service = new InvitationService(db);
        var fromServer = CreateServer("from");
        var toServer = CreateServer("to");
        var unrelatedServer = CreateServer("unrelated");
        db.Servers.AddRange(fromServer, toServer, unrelatedServer);
        var invitation = new Invitation
        {
            FromServerId = fromServer.Id,
            ToServerId = toServer.Id,
            Status = InvitationStatus.Pending
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = await service.RevokeAsync(unrelatedServer, invitation.Id, "corr-revoke", cancellationToken);

        Assert.True(outcome.IsFailure);
        Assert.Equal("invitation.not_found", outcome.Failure!.Code);
        Assert.Equal(FailureCategory.NotFound, outcome.Failure.Category);
    }

    private static RegisteredServer CreateServer(string name) => new()
    {
        Name = name,
        OwnerUserId = $"owner-{name}",
        ApiKey = $"api-key-{Guid.NewGuid():N}"
    };

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

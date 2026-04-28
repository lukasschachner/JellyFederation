using System.Reflection;
using JellyFederation.Data;
using JellyFederation.Server.Hubs;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class StaleRequestCleanupServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<FederationDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IHubContext<FederationHub>, NoOpHubContext>();
        services.AddSingleton(new SignalRErrorMapper());
        services.AddSingleton(new ServerConnectionTracker(NullLogger<ServerConnectionTracker>.Instance));
        services.AddSingleton<FileRequestNotifier>();

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CleanupStaleRequestsAsync_MarksOnlyOldInFlightRequestsAsFailed()
    {
        var requester = new RegisteredServer { Name = "requester", OwnerUserId = "owner-a", ApiKey = "key-a" };
        var owner = new RegisteredServer { Name = "owner", OwnerUserId = "owner-b", ApiKey = "key-b" };
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
            db.Servers.AddRange(requester, owner);
            db.FileRequests.AddRange(
                new FileRequest
                {
                    RequestingServerId = requester.Id,
                    OwningServerId = owner.Id,
                    JellyfinItemId = "old-pending",
                    Status = FileRequestStatus.Pending,
                    CreatedAt = cutoff.AddMinutes(-5)
                },
                new FileRequest
                {
                    RequestingServerId = requester.Id,
                    OwningServerId = owner.Id,
                    JellyfinItemId = "old-transferring",
                    Status = FileRequestStatus.Transferring,
                    CreatedAt = cutoff.AddMinutes(-5)
                },
                new FileRequest
                {
                    RequestingServerId = requester.Id,
                    OwningServerId = owner.Id,
                    JellyfinItemId = "new-pending",
                    Status = FileRequestStatus.Pending,
                    CreatedAt = cutoff.AddMinutes(+5)
                },
                new FileRequest
                {
                    RequestingServerId = requester.Id,
                    OwningServerId = owner.Id,
                    JellyfinItemId = "old-completed",
                    Status = FileRequestStatus.Completed,
                    CreatedAt = cutoff.AddMinutes(-5)
                });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = new StaleRequestCleanupService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleRequestCleanupService>.Instance);

        await InvokeCleanupAsync(service);

        await using var verifyScope = _provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FederationDbContext>();
        var rows = await verifyDb.FileRequests
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(FileRequestStatus.Failed, rows.Single(x => x.JellyfinItemId == "old-pending").Status);
        Assert.Equal(FileRequestStatus.Failed, rows.Single(x => x.JellyfinItemId == "old-transferring").Status);
        Assert.Contains("timed out", rows.Single(x => x.JellyfinItemId == "old-pending").FailureReason!,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(FileRequestStatus.Pending, rows.Single(x => x.JellyfinItemId == "new-pending").Status);
        Assert.Equal(FileRequestStatus.Completed, rows.Single(x => x.JellyfinItemId == "old-completed").Status);
    }

    [Fact]
    public async Task CleanupStaleRequestsAsync_WhenNoStaleRequests_DoesNothing()
    {
        var requester = new RegisteredServer { Name = "requester-2", OwnerUserId = "owner-a", ApiKey = "key-c" };
        var owner = new RegisteredServer { Name = "owner-2", OwnerUserId = "owner-b", ApiKey = "key-d" };

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
            db.Servers.AddRange(requester, owner);
            db.FileRequests.Add(new FileRequest
            {
                RequestingServerId = requester.Id,
                OwningServerId = owner.Id,
                JellyfinItemId = "fresh",
                Status = FileRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = new StaleRequestCleanupService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleRequestCleanupService>.Instance);

        await InvokeCleanupAsync(service);

        await using var verifyScope = _provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FederationDbContext>();
        var row = await verifyDb.FileRequests.AsNoTracking().SingleAsync(x => x.JellyfinItemId == "fresh",
            TestContext.Current.CancellationToken);
        Assert.Equal(FileRequestStatus.Pending, row.Status);
        Assert.Null(row.FailureReason);
    }

    private static async Task InvokeCleanupAsync(StaleRequestCleanupService service)
    {
        var method = typeof(StaleRequestCleanupService).GetMethod("CleanupStaleRequestsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(service, [TestContext.Current.CancellationToken]));
        await task;
    }

    private sealed class NoOpHubContext : IHubContext<FederationHub>
    {
        public IHubClients Clients { get; } = new NoOpHubClients();
        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class NoOpHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NoOpClientProxy();

        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NoOpClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

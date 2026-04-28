using System.Net;
using System.Reflection;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class LibrarySyncServiceTests
{
    private static readonly MethodInfo BuildImageUrlMethod =
        typeof(LibrarySyncService).GetMethod("BuildImageUrl", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildImageUrl not found.");

    private static readonly MethodInfo ChunkPreviewEntriesMethod =
        typeof(LibrarySyncService).GetMethod("ChunkPreviewEntries", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ChunkPreviewEntries not found.");

    private static readonly MethodInfo BuildPosterCandidatesMethod =
        typeof(LibrarySyncService).GetMethod("BuildPosterCandidates", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPosterCandidates not found.");

    [Fact]
    public async Task SyncAsync_ReturnsValidationFailure_WhenConfigurationIsMissing()
    {
        var service = new LibrarySyncService(
            A.Fake<ILibraryManager>(),
            new HttpClient(new CapturingHandler()),
            NullLogger<LibrarySyncService>.Instance);

        var outcome = await service.SyncAsync(new PluginConfiguration(), TestContext.Current.CancellationToken);

        Assert.True(outcome.IsFailure);
        Assert.Equal("library.sync.missing_configuration", outcome.Failure!.Code);
    }

    [Fact]
    public async Task SyncAsync_EmbedsPosterPreview_WhenLocalPosterFitsBudget()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfederation-library-preview-tests");
        try
        {
            var mediaPath = Path.Combine(directory.FullName, "movie.mkv");
            await File.WriteAllBytesAsync(mediaPath, [1], TestContext.Current.CancellationToken);
            using (var image = new Image<Rgba32>(32, 32, Color.Red))
                await image.SaveAsJpegAsync(Path.Combine(directory.FullName, "poster.jpg"), TestContext.Current.CancellationToken);
            var libraryManager = A.Fake<ILibraryManager>();
            A.CallTo(() => libraryManager.GetItemList(A<InternalItemsQuery>._))
                .Returns([new Movie { Id = Guid.NewGuid(), Name = "Movie", Path = mediaPath }]);
            var handler = new CapturingHandler();
            var service = new LibrarySyncService(
                libraryManager,
                new HttpClient(handler),
                NullLogger<LibrarySyncService>.Instance);

            var outcome = await service.SyncAsync(new PluginConfiguration
            {
                FederationServerUrl = "https://federation.example",
                ApiKey = "test-key"
            }, TestContext.Current.CancellationToken);

            Assert.True(outcome.IsSuccess);
            Assert.Equal(2, handler.Bodies.Count);
            Assert.Contains("data:image/jpeg;base64,", handler.Bodies[1], StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SyncAsync_PostsBaseAndPreviewPayloads_WhenConfigured()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfederation-library-sync-tests");
        try
        {
            var mediaPath = Path.Combine(directory.FullName, "movie.mkv");
            await File.WriteAllBytesAsync(mediaPath, [1, 2, 3], TestContext.Current.CancellationToken);
            var itemId = Guid.NewGuid();
            var item = new Movie
            {
                Id = itemId,
                Name = "Movie",
                Path = mediaPath,
                Overview = "Overview",
                ProductionYear = 2026
            };
            var libraryManager = A.Fake<ILibraryManager>();
            A.CallTo(() => libraryManager.GetItemList(A<InternalItemsQuery>._)).Returns([item]);
            var handler = new CapturingHandler();
            var service = new LibrarySyncService(
                libraryManager,
                new HttpClient(handler),
                NullLogger<LibrarySyncService>.Instance);

            var outcome = await service.SyncAsync(new PluginConfiguration
            {
                FederationServerUrl = "https://federation.example/",
                ApiKey = "test-key",
                PublicJellyfinUrl = "https://jellyfin.example"
            }, TestContext.Current.CancellationToken);

            Assert.True(outcome.IsSuccess);
            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, request => Assert.Equal("test-key", request.Headers.GetValues("X-Api-Key").Single()));
            Assert.Contains("\"replaceAll\":true", handler.Bodies[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"replaceAll\":false", handler.Bodies[1], StringComparison.OrdinalIgnoreCase);
            Assert.Contains(itemId.ToString(), handler.Bodies[0], StringComparison.Ordinal);
            Assert.Contains("https://jellyfin.example/Items/", handler.Bodies[1], StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BuildImageUrl_HandlesTrimAndEscaping()
    {
        var nullUrl = InvokeBuildImageUrl("", "item");
        var built = InvokeBuildImageUrl(" https://jf.example/ ", "movie/1");

        Assert.Null(nullUrl);
        Assert.Equal("https://jf.example/Items/movie%2F1/Images/Primary?fillWidth=480&quality=90", built);
    }

    [Fact]
    public void BuildPosterCandidates_ReturnsSupportedNamesInPriorityOrder()
    {
        var candidates = Assert.IsAssignableFrom<IEnumerable<string>>(
                BuildPosterCandidatesMethod.Invoke(null, ["/library/movie"]))
            .ToList();

        Assert.Equal("/library/movie/poster.jpg", candidates[0]);
        Assert.Contains("/library/movie/folder.png", candidates);
        Assert.Equal(12, candidates.Count);
    }

    [Fact]
    public void ChunkPreviewEntries_SplitsLargePayloadsAndPreservesOrder()
    {
        var giant = new string('x', 5_000_000);
        var entries = new List<MediaItemSyncEntry>
        {
            new("a", "A", MediaType.Movie, null, null, giant, 1),
            new("b", "B", MediaType.Movie, null, null, "small", 1),
            new("c", "C", MediaType.Movie, null, null, giant, 1)
        };

        var chunks = InvokeChunkPreviewEntries(entries);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(["a", "b"], chunks[0].Select(x => x.JellyfinItemId));
        Assert.Equal(["c"], chunks[1].Select(x => x.JellyfinItemId));
    }

    [Fact]
    public void ChunkPreviewEntries_HandlesEmptyAndExactThresholdBoundaries()
    {
        var emptyChunks = InvokeChunkPreviewEntries([]);
        Assert.Empty(emptyChunks);

        const int threshold = 8 * 1024 * 1024;
        var entries = new List<MediaItemSyncEntry>
        {
            new("a", "A", MediaType.Movie, null, null, new string('x', threshold), 1),
            new("b", "B", MediaType.Movie, null, null, "y", 1)
        };

        var chunks = InvokeChunkPreviewEntries(entries);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(["a"], chunks[0].Select(x => x.JellyfinItemId));
        Assert.Equal(["b"], chunks[1].Select(x => x.JellyfinItemId));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static string? InvokeBuildImageUrl(string jellyfinBaseUrl, string itemId) =>
        BuildImageUrlMethod.Invoke(null, [jellyfinBaseUrl, itemId]) as string;

    private static List<List<MediaItemSyncEntry>> InvokeChunkPreviewEntries(IReadOnlyList<MediaItemSyncEntry> entries) =>
        Assert.IsType<List<List<MediaItemSyncEntry>>>(ChunkPreviewEntriesMethod.Invoke(null, [entries]));
}

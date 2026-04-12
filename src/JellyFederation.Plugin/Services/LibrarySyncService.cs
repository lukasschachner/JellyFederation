using Jellyfin.Data.Enums;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Telemetry;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Reflection;
using MediaType = JellyFederation.Shared.Models.MediaType;
using System.Diagnostics;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Pushes the local Jellyfin library metadata to the federation server.
/// Called on startup and whenever a library change is detected.
/// </summary>
public partial class LibrarySyncService(
    ILibraryManager libraryManager,
    HttpClient http,
    ILogger<LibrarySyncService> logger)
{
    private const int MaxEmbeddedPreviewBytesPerImage = 450 * 1024;
    private const int MaxEmbeddedPreviewBytesPerSync = 80 * 1024 * 1024;
    private const int MaxPreviewUpdateChunkChars = 8 * 1024 * 1024;
    private static readonly int[] PreviewWidths = [360, 300, 240, 180];
    private static readonly int[] PreviewJpegQualities = [72, 60, 48, 40];

    // Cache PropertyInfo lookups per type to avoid repeated reflection on large libraries
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private int _pendingSync;
    private bool _missingConfigurationWarningLogged;

    public async Task SyncAsync(PluginConfiguration config, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(config.FederationServerUrl) ||
            string.IsNullOrEmpty(config.ApiKey))
        {
            if (!_missingConfigurationWarningLogged)
            {
                LogSyncSkipped(logger);
                _missingConfigurationWarningLogged = true;
            }
            return;
        }

        _missingConfigurationWarningLogged = false;
        Interlocked.Exchange(ref _pendingSync, 1);

        if (!await _syncGate.WaitAsync(0, ct))
        {
            LogSyncAlreadyInProgress(logger);
            return;
        }

        try
        {
            while (Interlocked.Exchange(ref _pendingSync, 0) == 1)
            {
                try
                {
                    await RunSyncOnceAsync(config, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    FederationMetrics.RecordOperation(
                        "library.sync",
                        "plugin",
                        FederationTelemetry.OutcomeError,
                        TimeSpan.Zero,
                        FederationPlugin.ReleaseVersion);
                    throw new InvalidOperationException(
                        $"Library sync failed: {TelemetryRedaction.SanitizeErrorMessage(ex.Message)}", ex);
                }
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task RunSyncOnceAsync(PluginConfiguration config, CancellationToken ct)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            FederationTelemetry.SpanPluginHttpClient,
            ActivityKind.Client);
        FederationTelemetry.SetCommonTags(
            activity,
            "library.sync",
            "plugin",
            correlationId,
            releaseVersion: FederationPlugin.ReleaseVersion);
        using var inFlight = FederationMetrics.BeginInflight("library.sync", "plugin", FederationPlugin.ReleaseVersion);

        LogStartingSync(logger);

        var items = libraryManager.GetItemList(new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode, BaseItemKind.Audio]
        }).ToList();

        var baseEntries = BuildBaseEntries(items);
        await PostSyncRequest(baseEntries, replaceAll: true, config, ct, correlationId);
        LogSyncedItems(logger, baseEntries.Count);

        var previewPayload = BuildSyncPayload(items, config, MaxEmbeddedPreviewBytesPerSync);
        var previewEntries = previewPayload.Entries
            .Where(x => !string.IsNullOrWhiteSpace(x.ImageUrl))
            .ToList();

        if (previewEntries.Count > 0)
        {
            var chunks = ChunkPreviewEntries(previewEntries);
            foreach (var chunk in chunks)
                await PostSyncRequest(chunk, replaceAll: false, config, ct, correlationId);

            LogPreviewSyncStats(logger,
                previewPayload.EmbeddedCount, previewPayload.FallbackUrlCount, previewPayload.MissingImageCount,
                previewPayload.BudgetUsed / 1024, previewPayload.BudgetRemaining / 1024, chunks.Count);
        }
        else
        {
            LogPreviewSyncStatsEmpty(logger, previewPayload.MissingImageCount, MaxEmbeddedPreviewBytesPerSync / 1024);
        }

        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("library.sync", "plugin", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
    }

    private static bool IsAudioItem(BaseItem item) =>
        item.GetType().Name == "Audio";

    private static MediaType MapType(BaseItem item) => item switch
    {
        Movie => MediaType.Movie,
        Series => MediaType.Series,
        Episode => MediaType.Episode,
        _ when IsAudioItem(item) => MediaType.Music,
        _ => MediaType.Other
    };

    private long GetFileSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;

        // Series/collection items often point to directories, not media files.
        if (Directory.Exists(path))
            return 0;

        if (!File.Exists(path))
            return 0;

        try
        {
            return new FileInfo(path).Length;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        catch (IOException ex)
        {
            LogFileSizeFailed(logger, ex, path);
            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogFileSizeAccessDenied(logger, ex, path);
            return 0;
        }
    }

    private static string? BuildImageUrl(string jellyfinBaseUrl, string jellyfinItemId)
    {
        if (string.IsNullOrWhiteSpace(jellyfinBaseUrl))
            return null;

        var baseUrl = jellyfinBaseUrl.Trim().TrimEnd('/');
        return $"{baseUrl}/Items/{Uri.EscapeDataString(jellyfinItemId)}/Images/Primary?fillWidth=480&quality=90";
    }

    private string? BuildEmbeddedPreviewDataUrl(BaseItem item, ref int remainingBudget)
    {
        if (remainingBudget <= 0)
            return null;

        var imagePath = ResolvePrimaryImagePath(item);
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        try
        {
            var maxBytes = Math.Min(MaxEmbeddedPreviewBytesPerImage, remainingBudget);
            var compressedBytes = TryCreateCompressedPreview(imagePath, maxBytes);
            if (compressedBytes is null)
                return null;

            remainingBudget -= compressedBytes.Length;
            return $"data:image/jpeg;base64,{Convert.ToBase64String(compressedBytes)}";
        }
        catch (Exception ex)
        {
            LogEmbedPreviewFailed(logger, ex, item.Id);
            return null;
        }
    }

    private SyncPayload BuildSyncPayload(IReadOnlyList<BaseItem> items, PluginConfiguration config, int previewBudget)
    {
        var remainingPreviewBudget = previewBudget;
        var embeddedCount = 0;
        var fallbackUrlCount = 0;
        var missingImageCount = 0;

        var entries = items
            .OrderBy(GetPreviewPriority)
            .Select(i =>
            {
                var embeddedPreview = BuildEmbeddedPreviewDataUrl(i, ref remainingPreviewBudget);
                var imageUrl = embeddedPreview ?? BuildImageUrl(config.PublicJellyfinUrl, i.Id.ToString());
                if (embeddedPreview is not null) embeddedCount++;
                else if (imageUrl is not null) fallbackUrlCount++;
                else missingImageCount++;
                return new MediaItemSyncEntry(
                    JellyfinItemId: i.Id.ToString(),
                    Title: i.Name,
                    Type: MapType(i),
                    Year: i.ProductionYear,
                    Overview: i.Overview,
                    ImageUrl: imageUrl,
                    FileSizeBytes: i.Path is not null ? GetFileSize(i.Path) : 0);
            })
            .ToList();

        return new SyncPayload(
            entries,
            embeddedCount,
            fallbackUrlCount,
            missingImageCount,
            BudgetRemaining: remainingPreviewBudget,
            BudgetUsed: previewBudget - remainingPreviewBudget);
    }

    private List<MediaItemSyncEntry> BuildBaseEntries(IReadOnlyList<BaseItem> items)
    {
        return items
            .Select(i => new MediaItemSyncEntry(
                JellyfinItemId: i.Id.ToString(),
                Title: i.Name,
                Type: MapType(i),
                Year: i.ProductionYear,
                Overview: i.Overview,
                ImageUrl: null,
                FileSizeBytes: i.Path is not null ? GetFileSize(i.Path) : 0))
            .ToList();
    }

    private static List<List<MediaItemSyncEntry>> ChunkPreviewEntries(IReadOnlyList<MediaItemSyncEntry> entries)
    {
        var chunks = new List<List<MediaItemSyncEntry>>();
        var current = new List<MediaItemSyncEntry>();
        var currentChars = 0;

        foreach (var entry in entries)
        {
            var imageChars = entry.ImageUrl?.Length ?? 0;
            if (current.Count > 0 && currentChars + imageChars > MaxPreviewUpdateChunkChars)
            {
                chunks.Add(current);
                current = new List<MediaItemSyncEntry>();
                currentChars = 0;
            }

            current.Add(entry);
            currentChars += imageChars;
        }

        if (current.Count > 0)
            chunks.Add(current);

        return chunks;
    }

    private async Task PostSyncRequest(List<MediaItemSyncEntry> entries, bool replaceAll, PluginConfiguration config, CancellationToken ct, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{config.FederationServerUrl.TrimEnd('/')}/api/library/sync")
        {
            Content = JsonContent.Create(new SyncMediaRequest(entries, ReplaceAll: replaceAll))
        };
        request.Headers.Add("X-Api-Key", config.ApiKey);
        TraceContextPropagation.InjectToHttpRequest(request);
        TraceContextPropagation.InjectCorrelationId(request.Headers, correlationId);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static byte[]? TryCreateCompressedPreview(string imagePath, int maxBytes)
    {
        using var sourceImage = Image.Load(imagePath);

        foreach (var width in PreviewWidths)
        {
            using var resized = sourceImage.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(width, width)
            }));

            foreach (var quality in PreviewJpegQualities)
            {
                using var ms = new MemoryStream();
                resized.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });
                if (ms.Length > 0 && ms.Length <= maxBytes)
                    return ms.ToArray();
            }
        }

        return null;
    }

    private static int GetPreviewPriority(BaseItem item) => item switch
    {
        Movie => 0,
        Series => 1,
        Episode => 2,
        _ when IsAudioItem(item) => 3,
        _ => 4
    };

    private string? ResolvePrimaryImagePath(BaseItem item)
    {
        var visited = new HashSet<Guid>();
        var jellyfinPath = ResolvePrimaryImagePathFromItemWithFallback(item, visited);
        if (!string.IsNullOrWhiteSpace(jellyfinPath)) return jellyfinPath;

        foreach (var parent in item.GetParents())
        {
            var parentPath = ResolvePrimaryImagePathFromItemWithFallback(parent, visited);
            if (!string.IsNullOrWhiteSpace(parentPath)) return parentPath;
        }

        var owner = item.GetOwner();
        if (owner is not null && !visited.Contains(owner.Id))
        {
            var ownerPath = ResolvePrimaryImagePathFromItemWithFallback(owner, visited);
            if (!string.IsNullOrWhiteSpace(ownerPath)) return ownerPath;
        }

        return null;
    }

    private string? ResolvePrimaryImagePathFromItemWithFallback(BaseItem item, HashSet<Guid> visited)
    {
        if (!visited.Add(item.Id))
            return null;

        var jellyfinPath = ResolveImagePathFromJellyfinMetadata(item);
        if (!string.IsNullOrWhiteSpace(jellyfinPath))
            return jellyfinPath;

        var runtimeType = item.GetType();
        foreach (var propertyName in new[] { "PrimaryImagePath", "ImagePath" })
        {
            var property = PropertyCache.GetOrAdd(
                (runtimeType, propertyName),
                key => key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance));

            if (property?.PropertyType == typeof(string) &&
                property.GetValue(item) is string directPath &&
                !string.IsNullOrWhiteSpace(directPath))
            {
                return directPath;
            }
        }

        if (string.IsNullOrWhiteSpace(item.Path))
            return null;

        IEnumerable<string> candidates;
        if (File.Exists(item.Path))
        {
            var directory = Path.GetDirectoryName(item.Path);
            if (string.IsNullOrWhiteSpace(directory))
                return null;
            candidates = BuildPosterCandidates(directory);
        }
        else if (Directory.Exists(item.Path))
        {
            candidates = BuildPosterCandidates(item.Path);
        }
        else
        {
            return null;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private string? ResolveImagePathFromJellyfinMetadata(BaseItem item)
    {
        // Prefer Jellyfin's own image metadata over filesystem conventions.
        // This covers posters stored in Jellyfin's metadata cache as well.
        var preferredTypes = new[]
        {
            ImageType.Primary,
            ImageType.Thumb,
            ImageType.Backdrop,
            ImageType.Logo,
            ImageType.Art
        };

        foreach (var imageType in preferredTypes)
        {
            try
            {
                var directPath = item.GetImagePath(imageType, 0);
                if (!string.IsNullOrWhiteSpace(directPath) && File.Exists(directPath))
                    return directPath;

                foreach (var imageInfo in item.GetImages(imageType))
                {
                    if (!string.IsNullOrWhiteSpace(imageInfo.Path) && File.Exists(imageInfo.Path))
                        return imageInfo.Path;
                }
            }
            catch (Exception ex)
            {
                // Some item types may not expose all image slots; log unexpected failures at debug level.
                LogReadImageSlotFailed(logger, ex, imageType, item.Id);
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildPosterCandidates(string directory)
    {
        foreach (var name in new[] { "poster", "folder", "cover" })
        {
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
            {
                yield return Path.Combine(directory, name + ext);
            }
        }
    }

    private sealed record SyncPayload(
        List<MediaItemSyncEntry> Entries,
        int EmbeddedCount,
        int FallbackUrlCount,
        int MissingImageCount,
        int BudgetRemaining,
        int BudgetUsed);

}

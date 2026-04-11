using Jellyfin.Data.Enums;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Dtos;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using MediaType = JellyFederation.Shared.Models.MediaType;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Pushes the local Jellyfin library metadata to the federation server.
/// Called on startup and whenever a library change is detected.
/// </summary>
public class LibrarySyncService(
    ILibraryManager libraryManager,
    HttpClient http,
    ILogger<LibrarySyncService> logger)
{
    public async Task SyncAsync(PluginConfiguration config, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(config.FederationServerUrl) ||
            string.IsNullOrEmpty(config.ApiKey))
            return;

        logger.LogInformation("Starting library sync to federation server");

        var items = libraryManager.GetItemList(new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode, BaseItemKind.Audio]
        });

        var syncEntries = items
            .Select(i => new MediaItemSyncEntry(
                JellyfinItemId: i.Id.ToString(),
                Title: i.Name,
                Type: MapType(i),
                Year: i.ProductionYear,
                Overview: i.Overview,
                ImageUrl: null, // image serving handled by Jellyfin directly
                FileSizeBytes: i.Path is not null ? GetFileSize(i.Path) : 0))
            .ToList();

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{config.FederationServerUrl.TrimEnd('/')}/api/library/sync")
        {
            Content = JsonContent.Create(new SyncMediaRequest(syncEntries))
        };
        request.Headers.Add("X-Api-Key", config.ApiKey);

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Synced {Count} items to federation server", syncEntries.Count);
    }

    private static MediaType MapType(BaseItem item) => item switch
    {
        Movie => MediaType.Movie,
        Series => MediaType.Series,
        Episode => MediaType.Episode,
        _ when item.GetType().Name == "Audio" => MediaType.Music,
        _ => MediaType.Other
    };

    private long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not get file size for {Path}", path);
            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied getting file size for {Path}", path);
            return 0;
        }
    }
}

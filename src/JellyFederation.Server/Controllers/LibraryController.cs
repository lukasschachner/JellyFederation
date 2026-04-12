using JellyFederation.Server.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public partial class LibraryController(
    FederationDbContext db,
    ILogger<LibraryController> logger) : AuthenticatedController
{
    /// <summary>
    /// Replaces the entire media index for the authenticated server.
    /// Plugin calls this on startup and after library changes.
    /// Preserves existing IsRequestable flags -- sync only updates metadata.
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(SyncMediaRequest request)
    {
        var startedAt = Stopwatch.StartNew();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "library.sync", "server", CorrelationId);
        using var inFlight = FederationMetrics.BeginInflight("library.sync", "server");

        var server = CurrentServer;
        LogSyncStarted(logger, server.Id, request.Items.Count, request.ReplaceAll);

        // Differential sync: update changed items, add new ones, remove stale ones
        var existing = await db.MediaItems
            .Where(m => m.ServerId == server.Id)
            .ToDictionaryAsync(m => m.JellyfinItemId);

        var incomingIds = new HashSet<string>(request.Items.Count);
        var updatedCount = 0;
        var addedCount = 0;

        foreach (var item in request.Items)
        {
            incomingIds.Add(item.JellyfinItemId);

            if (existing.TryGetValue(item.JellyfinItemId, out var dbItem))
            {
                // Update changed metadata fields, preserve IsRequestable
                dbItem.Title = item.Title;
                dbItem.Type = item.Type;
                dbItem.Year = item.Year;
                dbItem.Overview = item.Overview;
                dbItem.ImageUrl = item.ImageUrl;
                dbItem.FileSizeBytes = item.FileSizeBytes;
                updatedCount++;
            }
            else
            {
                // New item
                db.MediaItems.Add(new MediaItem
                {
                    ServerId = server.Id,
                    JellyfinItemId = item.JellyfinItemId,
                    Title = item.Title,
                    Type = item.Type,
                    Year = item.Year,
                    Overview = item.Overview,
                    ImageUrl = item.ImageUrl,
                    FileSizeBytes = item.FileSizeBytes,
                    IsRequestable = true
                });
                addedCount++;
            }
        }

        var removedCount = 0;
        if (request.ReplaceAll)
        {
            // Remove items that no longer exist in Jellyfin
            var staleItems = existing.Values.Where(m => !incomingIds.Contains(m.JellyfinItemId)).ToList();
            removedCount = staleItems.Count;
            db.MediaItems.RemoveRange(staleItems);
        }

        await db.SaveChangesAsync();
        LogSyncCompleted(logger, server.Id, request.Items.Count, addedCount, updatedCount, removedCount);
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("library.sync", "server", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed);

        return Ok();
    }

    /// <summary>
    /// Returns all media items belonging to the authenticated server.
    /// Supports optional type filter. Returns X-Total-Count header for pagination.
    /// </summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<MediaItemDto>>> Mine(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        pageSize = Math.Min(pageSize, 500);
        var server = CurrentServer;

        var query = db.MediaItems
            .Include(m => m.Server)
            .Where(m => m.ServerId == server.Id);

        query = ApplyTitleSearch(query, search);

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<MediaType>(type, ignoreCase: true, out var parsedType))
            query = query.Where(m => m.Type == parsedType);
        else if (!string.IsNullOrWhiteSpace(type))
            LogInvalidMediaTypeFilter(logger, type, "mine");

        var total = await query.CountAsync();
        Response.Headers["X-Total-Count"] = total.ToString();

        var items = await query
            .OrderBy(m => m.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        LogMineReturned(logger, server.Id, total, page, pageSize, search, type);

        return Ok(items.Select(ToDto));
    }

    /// <summary>
    /// Returns item counts grouped by media type for the authenticated server.
    /// </summary>
    [HttpGet("mine/counts")]
    public async Task<ActionResult<Dictionary<string, int>>> MineCounts(
        [FromQuery] string? search)
    {
        var server = CurrentServer;

        var query = ApplyTitleSearch(db.MediaItems.Where(m => m.ServerId == server.Id), search);

        var counts = await query
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var result = counts.ToDictionary(x => x.Type, x => x.Count);
        result["All"] = counts.Sum(x => x.Count);
        LogMineCountsReturned(logger, server.Id, result["All"], search);
        return Ok(result);
    }

    /// <summary>
    /// Sets the IsRequestable flag on a media item owned by the authenticated server.
    /// </summary>
    [HttpPut("{itemId:guid}/requestable")]
    public async Task<IActionResult> SetRequestable(
        Guid itemId,
        [FromBody] SetRequestableRequest body)
    {
        var server = CurrentServer;

        var item = await db.MediaItems.FirstOrDefaultAsync(
            m => m.Id == itemId && m.ServerId == server.Id);
        if (item is null)
        {
            LogSetRequestableNotFound(logger, itemId, server.Id);
            return NotFound();
        }

        item.IsRequestable = body.IsRequestable;
        await db.SaveChangesAsync();
        LogSetRequestableUpdated(logger, item.Id, server.Id, body.IsRequestable);
        return NoContent();
    }

    /// <summary>
    /// Browse all media visible to the requesting server.
    /// Supports optional type filter. Returns X-Total-Count header for pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<MediaItemDto>>> Browse(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        pageSize = Math.Min(pageSize, 500);
        var server = CurrentServer;

        var query = GetBrowsableItems(server, search, type)
            .Include(m => m.Server);

        var total = await query.CountAsync();
        Response.Headers["X-Total-Count"] = total.ToString();

        var items = await query
            .OrderBy(m => m.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        LogBrowseReturned(logger, server.Id, total, page, pageSize, search, type);

        return Ok(items.Select(ToDto));
    }

    /// <summary>
    /// Returns item counts grouped by media type for the federated library.
    /// </summary>
    [HttpGet("counts")]
    public async Task<ActionResult<Dictionary<string, int>>> BrowseCounts(
        [FromQuery] string? search)
    {
        var server = CurrentServer;

        var query = GetBrowsableItems(server, search, type: null);

        var counts = await query
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var result = counts.ToDictionary(x => x.Type, x => x.Count);
        result["All"] = counts.Sum(x => x.Count);
        LogBrowseCountsReturned(logger, server.Id, result["All"], search);
        return Ok(result);
    }

    /// <summary>
    /// Builds the base query for browsable media items from federated servers.
    /// Filters to accepted-invitation peers, requestable items, and optional search/type.
    /// </summary>
    private IQueryable<MediaItem> GetBrowsableItems(RegisteredServer server, string? search, string? type)
    {
        var allowedServerIds = db.Invitations
            .Where(i => i.Status == InvitationStatus.Accepted &&
                        (i.FromServerId == server.Id || i.ToServerId == server.Id))
            .Select(i => i.FromServerId == server.Id ? i.ToServerId : i.FromServerId);

        var query = db.MediaItems
            .Where(m => allowedServerIds.Contains(m.ServerId) && m.IsRequestable);

        query = ApplyTitleSearch(query, search);

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<MediaType>(type, ignoreCase: true, out var parsedType))
            query = query.Where(m => m.Type == parsedType);
        else if (!string.IsNullOrWhiteSpace(type))
            LogInvalidMediaTypeFilter(logger, type, "browse");

        return query;
    }

    private static IQueryable<MediaItem> ApplyTitleSearch(IQueryable<MediaItem> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        return query.Where(m => EF.Functions.Like(m.Title, $"%{EscapeLikePattern(search)}%", @"\"));
    }

    private static string EscapeLikePattern(string input) =>
        input.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");

    private static MediaItemDto ToDto(MediaItem m) => new(
        m.Id, m.ServerId, m.Server.Name,
        m.JellyfinItemId, m.Title, m.Type,
        m.Year, m.Overview, m.ImageUrl, m.FileSizeBytes,
        m.IsRequestable);
}

public record SetRequestableRequest(bool IsRequestable);

using JellyFederation.Server.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class LibraryController(FederationDbContext db) : ControllerBase
{
    /// <summary>
    /// Replaces the entire media index for the authenticated server.
    /// Plugin calls this on startup and after library changes.
    /// Preserves existing IsRequestable flags -- sync only updates metadata.
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(SyncMediaRequest request)
    {
        var server = GetServer();

        // Keep existing requestable flags indexed by JellyfinItemId
        var existing = await db.MediaItems
            .Where(m => m.ServerId == server.Id)
            .ToDictionaryAsync(m => m.JellyfinItemId);

        db.MediaItems.RemoveRange(existing.Values);

        var newItems = request.Items.Select(item => new MediaItem
        {
            ServerId = server.Id,
            JellyfinItemId = item.JellyfinItemId,
            Title = item.Title,
            Type = item.Type,
            Year = item.Year,
            Overview = item.Overview,
            ImageUrl = item.ImageUrl,
            FileSizeBytes = item.FileSizeBytes,
            // Preserve requestable flag if item was already known, default true for new items
            IsRequestable = existing.TryGetValue(item.JellyfinItemId, out var prev)
                ? prev.IsRequestable
                : true
        });

        db.MediaItems.AddRange(newItems);
        await db.SaveChangesAsync();

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
        var server = GetServer();

        var query = db.MediaItems
            .Include(m => m.Server)
            .Where(m => m.ServerId == server.Id);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => EF.Functions.Like(m.Title, $"%{search}%"));

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<MediaType>(type, ignoreCase: true, out var parsedType))
            query = query.Where(m => m.Type == parsedType);

        var total = await query.CountAsync();
        Response.Headers["X-Total-Count"] = total.ToString();

        var items = await query
            .OrderBy(m => m.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(items.Select(ToDto));
    }

    /// <summary>
    /// Returns item counts grouped by media type for the authenticated server.
    /// </summary>
    [HttpGet("mine/counts")]
    public async Task<ActionResult<Dictionary<string, int>>> MineCounts(
        [FromQuery] string? search)
    {
        var server = GetServer();

        var query = db.MediaItems.Where(m => m.ServerId == server.Id);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => EF.Functions.Like(m.Title, $"%{search}%"));

        var counts = await query
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var result = counts.ToDictionary(x => x.Type, x => x.Count);
        result["All"] = counts.Sum(x => x.Count);
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
        var server = GetServer();

        var item = await db.MediaItems.FirstOrDefaultAsync(
            m => m.Id == itemId && m.ServerId == server.Id);
        if (item is null) return NotFound();

        item.IsRequestable = body.IsRequestable;
        await db.SaveChangesAsync();
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
        var server = GetServer();

        var query = GetBrowsableItems(server, search, type)
            .Include(m => m.Server);

        var total = await query.CountAsync();
        Response.Headers["X-Total-Count"] = total.ToString();

        var items = await query
            .OrderBy(m => m.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(items.Select(ToDto));
    }

    /// <summary>
    /// Returns item counts grouped by media type for the federated library.
    /// </summary>
    [HttpGet("counts")]
    public async Task<ActionResult<Dictionary<string, int>>> BrowseCounts(
        [FromQuery] string? search)
    {
        var server = GetServer();

        var query = GetBrowsableItems(server, search, type: null);

        var counts = await query
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var result = counts.ToDictionary(x => x.Type, x => x.Count);
        result["All"] = counts.Sum(x => x.Count);
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

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => EF.Functions.Like(m.Title, $"%{search}%"));

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<MediaType>(type, ignoreCase: true, out var parsedType))
            query = query.Where(m => m.Type == parsedType);

        return query;
    }

    private RegisteredServer GetServer() =>
        (RegisteredServer)HttpContext.Items["Server"]!;

    private static MediaItemDto ToDto(MediaItem m) => new(
        m.Id, m.ServerId, m.Server.Name,
        m.JellyfinItemId, m.Title, m.Type,
        m.Year, m.Overview, m.ImageUrl, m.FileSizeBytes,
        m.IsRequestable);
}

public record SetRequestableRequest(bool IsRequestable);

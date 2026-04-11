using JellyFederation.Server.Data;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LibraryController(FederationDbContext db) : ControllerBase
{
    /// <summary>
    /// Replaces the entire media index for the authenticated server.
    /// Plugin calls this on startup and after library changes.
    /// Preserves existing IsRequestable flags — sync only updates metadata.
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(SyncMediaRequest request,
        [FromHeader(Name = "X-Api-Key")] string apiKey)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

        // Differential sync: update changed items, add new ones, remove stale ones
        var existing = await db.MediaItems
            .Where(m => m.ServerId == server.Id)
            .ToDictionaryAsync(m => m.JellyfinItemId);

        var incomingIds = new HashSet<string>(request.Items.Count);

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
            }
        }

        // Remove items that no longer exist in Jellyfin
        var staleItems = existing.Values.Where(m => !incomingIds.Contains(m.JellyfinItemId));
        db.MediaItems.RemoveRange(staleItems);

        await db.SaveChangesAsync();

        return Ok();
    }

    /// <summary>
    /// Returns all media items belonging to the authenticated server.
    /// Supports optional type filter. Returns X-Total-Count header for pagination.
    /// </summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<MediaItemDto>>> Mine(
        [FromHeader(Name = "X-Api-Key")] string apiKey,
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

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
        [FromHeader(Name = "X-Api-Key")] string apiKey,
        [FromQuery] string? search)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

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
        [FromBody] SetRequestableRequest body,
        [FromHeader(Name = "X-Api-Key")] string apiKey)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

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
        [FromHeader(Name = "X-Api-Key")] string apiKey,
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

        var allowedServerIds = await db.Invitations
            .Where(i => i.Status == InvitationStatus.Accepted &&
                        (i.FromServerId == server.Id || i.ToServerId == server.Id))
            .Select(i => i.FromServerId == server.Id ? i.ToServerId : i.FromServerId)
            .ToListAsync();

        var query = db.MediaItems
            .Include(m => m.Server)
            .Where(m => allowedServerIds.Contains(m.ServerId) && m.IsRequestable);

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
    /// Returns item counts grouped by media type for the federated library.
    /// </summary>
    [HttpGet("counts")]
    public async Task<ActionResult<Dictionary<string, int>>> BrowseCounts(
        [FromHeader(Name = "X-Api-Key")] string apiKey,
        [FromQuery] string? search)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

        var allowedServerIds = await db.Invitations
            .Where(i => i.Status == InvitationStatus.Accepted &&
                        (i.FromServerId == server.Id || i.ToServerId == server.Id))
            .Select(i => i.FromServerId == server.Id ? i.ToServerId : i.FromServerId)
            .ToListAsync();

        var query = db.MediaItems
            .Where(m => allowedServerIds.Contains(m.ServerId) && m.IsRequestable);

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

    private static MediaItemDto ToDto(MediaItem m) => new(
        m.Id, m.ServerId, m.Server.Name,
        m.JellyfinItemId, m.Title, m.Type,
        m.Year, m.Overview, m.ImageUrl, m.FileSizeBytes,
        m.IsRequestable);
}

public record SetRequestableRequest(bool IsRequestable);

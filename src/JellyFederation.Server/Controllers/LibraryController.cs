using System.Diagnostics;
using JellyFederation.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Pagination;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public partial class LibraryController : AuthenticatedController
{
    private const int SyncBatchSize = 500;
    private const int LibrarySyncRequestSizeLimitBytes = 100 * 1024 * 1024;

    private readonly FederationDbContext _db;
    private readonly ErrorContractMapper _errorMapper;
    private readonly ILogger<LibraryController> _logger;

    public LibraryController(FederationDbContext db,
        ErrorContractMapper errorMapper,
        ILogger<LibraryController> logger)
    {
        _db = db;
        _errorMapper = errorMapper;
        _logger = logger;
    }

    /// <summary>
    ///     Replaces the entire media index for the authenticated server.
    ///     Plugin calls this on startup and after library changes.
    ///     Preserves existing IsRequestable flags -- sync only updates metadata.
    /// </summary>
    [HttpPost("sync")]
    [RequestSizeLimit(LibrarySyncRequestSizeLimitBytes)]
    public async Task<IActionResult> Sync(SyncMediaRequest request, CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.StartNew();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "library.sync", "server", CorrelationId);
        using var inFlight = FederationMetrics.BeginInflight("library.sync", "server");

        var server = CurrentServer;
        LogSyncStarted(_logger, server.Id, request.Items.Count, request.ReplaceAll);

        var incomingIds = new HashSet<string>(request.Items.Count, StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!incomingIds.Add(item.JellyfinItemId))
            {
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
                FederationMetrics.RecordOperation("library.sync", "server", FederationTelemetry.OutcomeError,
                    startedAt.Elapsed, failureCategory: FailureCategory.Validation.ToString(),
                    failureCode: "library.sync.duplicate_item");
                return ErrorContractMapper.ToActionResult(FailureDescriptor.Validation(
                    "library.sync.duplicate_item",
                    "Sync payload contains duplicate media item identifiers.",
                    CorrelationId));
            }
        }

        var syncStartedAt = DateTime.UtcNow;
        var updatedCount = 0;
        var addedCount = 0;

        foreach (var batch in request.Items.Chunk(SyncBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchIds = batch.Select(i => i.JellyfinItemId).ToArray();

            // Bound tracking to the current batch rather than all media rows for the server.
            var existing = await _db.MediaItems
                .AsTracking()
                .Where(m => m.ServerId == server.Id && batchIds.Contains(m.JellyfinItemId))
                .ToDictionaryAsync(m => m.JellyfinItemId, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in batch)
            {
                if (existing.TryGetValue(item.JellyfinItemId, out var dbItem))
                {
                    // Update changed metadata fields, preserve IsRequestable.
                    dbItem.Title = item.Title;
                    dbItem.Type = item.Type;
                    dbItem.Year = item.Year;
                    dbItem.Overview = item.Overview;
                    dbItem.ImageUrl = item.ImageUrl;
                    dbItem.FileSizeBytes = item.FileSizeBytes;
                    dbItem.IndexedAt = syncStartedAt;
                    updatedCount++;
                }
                else
                {
                    _db.MediaItems.Add(new MediaItem
                    {
                        ServerId = server.Id,
                        JellyfinItemId = item.JellyfinItemId,
                        Title = item.Title,
                        Type = item.Type,
                        Year = item.Year,
                        Overview = item.Overview,
                        ImageUrl = item.ImageUrl,
                        FileSizeBytes = item.FileSizeBytes,
                        IsRequestable = true,
                        IndexedAt = syncStartedAt
                    });
                    addedCount++;
                }
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
        }

        var removedCount = 0;
        if (request.ReplaceAll)
            removedCount = await _db.MediaItems
                .Where(m => m.ServerId == server.Id && m.IndexedAt < syncStartedAt)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

        LogSyncCompleted(_logger, server.Id, request.Items.Count, addedCount, updatedCount, removedCount);
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("library.sync", "server", FederationTelemetry.OutcomeSuccess,
            startedAt.Elapsed);

        return Ok();
    }

    /// <summary>
    ///     Returns all media items belonging to the authenticated server.
    ///     Supports optional type filter. Returns X-Total-Count header for pagination.
    /// </summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<MediaItemDto>>> Mine(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (TryValidatePagination(page, pageSize) is { } validationFailure)
            return validationFailure;

        var server = CurrentServer;

        var query = _db.MediaItems
            .AsNoTracking()
            .Where(m => m.ServerId == server.Id);

        query = ApplyTitleSearch(query, search);

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<MediaType>(type, true, out var parsedType))
            query = query.Where(m => m.Type == parsedType);
        else if (!string.IsNullOrWhiteSpace(type))
            LogInvalidMediaTypeFilter(_logger, type, "mine");

        var total = await query.CountAsync().ConfigureAwait(false);
        Response.Headers["X-Total-Count"] = total.ToString();

        // All items belong to CurrentServer — capture the name once as a constant rather than joining.
        var serverName = server.Name;
        var items = await query
            .OrderBy(m => m.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MediaItemDto(
                m.Id, m.ServerId, serverName,
                m.JellyfinItemId, m.Title, m.Type,
                m.Year, m.Overview, m.ImageUrl, m.FileSizeBytes, m.IsRequestable))
            .ToListAsync().ConfigureAwait(false);
        LogMineReturned(_logger, server.Id, total, page, pageSize, search, type);

        return Ok(items);
    }

    /// <summary>
    ///     Returns item counts grouped by media type for the authenticated server.
    /// </summary>
    [HttpGet("mine/counts")]
    public async Task<ActionResult<Dictionary<string, int>>> MineCounts(
        [FromQuery] string? search)
    {
        var server = CurrentServer;

        var query = ApplyTitleSearch(
            _db.MediaItems
                .AsNoTracking()
                .Where(m => m.ServerId == server.Id),
            search);

        var counts = await query
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync().ConfigureAwait(false);

        var result = counts.ToDictionary(x => x.Type, x => x.Count);
        result["All"] = counts.Sum(x => x.Count);
        LogMineCountsReturned(_logger, server.Id, result["All"], search);
        return Ok(result);
    }

    /// <summary>
    ///     Sets the IsRequestable flag on a media item owned by the authenticated server.
    /// </summary>
    [HttpPut("{itemId:guid}/requestable")]
    public async Task<IActionResult> SetRequestable(
        Guid itemId,
        [FromBody] SetRequestableRequest body)
    {
        var server = CurrentServer;

        var item = await _db.MediaItems
            .AsTracking()
            .FirstOrDefaultAsync(m => m.Id == itemId && m.ServerId == server.Id)
            .ConfigureAwait(false);
        if (item is null)
        {
            LogSetRequestableNotFound(_logger, itemId, server.Id);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.NotFound(
                "library.item_not_found",
                "Media item not found for authenticated server.",
                CorrelationId));
        }

        item.IsRequestable = body.IsRequestable;
        await _db.SaveChangesAsync().ConfigureAwait(false);
        LogSetRequestableUpdated(_logger, item.Id, server.Id, body.IsRequestable);
        return NoContent();
    }

    /// <summary>
    ///     Browse all media visible to the requesting server.
    ///     Supports optional type filter. Returns X-Total-Count header for pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<MediaItemDto>>> Browse(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (TryValidatePagination(page, pageSize) is { } validationFailure)
            return validationFailure;

        var server = CurrentServer;

        var baseQuery = GetBrowsableItems(server, search, type);

        var total = await baseQuery.CountAsync().ConfigureAwait(false);
        Response.Headers["X-Total-Count"] = total.ToString();

        // Project to DTO in-query so EF Core only selects the columns we need.
        // m.Server.Name is translated to a JOIN on the fly — no Include required.
        var items = await baseQuery
            .OrderBy(m => m.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MediaItemDto(
                m.Id, m.ServerId, m.Server.Name,
                m.JellyfinItemId, m.Title, m.Type,
                m.Year, m.Overview, m.ImageUrl, m.FileSizeBytes, m.IsRequestable))
            .ToListAsync().ConfigureAwait(false);
        LogBrowseReturned(_logger, server.Id, total, page, pageSize, search, type);

        return Ok(items);
    }

    /// <summary>
    ///     Returns item counts grouped by media type for the federated library.
    /// </summary>
    [HttpGet("counts")]
    public async Task<ActionResult<Dictionary<string, int>>> BrowseCounts(
        [FromQuery] string? search)
    {
        var server = CurrentServer;

        var query = GetBrowsableItems(server, search, null);

        var counts = await query
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync().ConfigureAwait(false);

        var result = counts.ToDictionary(x => x.Type, x => x.Count);
        result["All"] = counts.Sum(x => x.Count);
        LogBrowseCountsReturned(_logger, server.Id, result["All"], search);
        return Ok(result);
    }

    /// <summary>
    ///     Builds the base query for browsable media items from federated servers.
    ///     Filters to accepted-invitation peers, requestable items, and optional search/type.
    /// </summary>
    private IQueryable<MediaItem> GetBrowsableItems(RegisteredServer server, string? search, string? type)
    {
        var allowedServerIds = _db.Invitations
            .Where(i => i.Status == InvitationStatus.Accepted &&
                        (i.FromServerId == server.Id || i.ToServerId == server.Id))
            .Select(i => i.FromServerId == server.Id ? i.ToServerId : i.FromServerId);

        var query = _db.MediaItems
            .AsNoTracking()
            .Where(m => allowedServerIds.Contains(m.ServerId) && m.IsRequestable);

        query = ApplyTitleSearch(query, search);

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<MediaType>(type, true, out var parsedType))
            query = query.Where(m => m.Type == parsedType);
        else if (!string.IsNullOrWhiteSpace(type))
            LogInvalidMediaTypeFilter(_logger, type, "browse");

        return query;
    }

    private ObjectResult? TryValidatePagination(int page, int pageSize) =>
        PaginationHeaders.Validate(page, pageSize, "library.pagination.invalid", CorrelationId);

    private static IQueryable<MediaItem> ApplyTitleSearch(IQueryable<MediaItem> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        return query.Where(m => EF.Functions.Like(m.Title, $"%{EscapeLikePattern(search)}%", @"\"));
    }

    private static string EscapeLikePattern(string input)
    {
        return input.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");
    }

    // ToDto kept for completeness but no longer called by read endpoints (they use projections).
    private static MediaItemDto ToDto(MediaItem m, string serverName) =>
        new(m.Id, m.ServerId, serverName,
            m.JellyfinItemId, m.Title, m.Type,
            m.Year, m.Overview, m.ImageUrl, m.FileSizeBytes, m.IsRequestable);
}

public record SetRequestableRequest
{
    public SetRequestableRequest(bool IsRequestable)
    {
        this.IsRequestable = IsRequestable;
    }

    public bool IsRequestable { get; init; }

    public void Deconstruct(out bool IsRequestable)
    {
        IsRequestable = this.IsRequestable;
    }
}

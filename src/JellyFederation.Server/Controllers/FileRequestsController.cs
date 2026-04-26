using System.Diagnostics;
using JellyFederation.Data;
using JellyFederation.Server.Auth;
using JellyFederation.Server.Hubs;
using JellyFederation.Server.Pagination;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize(AuthenticationSchemes = FederationAuthSchemes.ApiKeyOrSession)]
public partial class FileRequestsController : AuthenticatedController
{
    private readonly FederationDbContext _db;
    private readonly ErrorContractMapper _errorMapper;
    private readonly IHubContext<FederationHub> _hub;
    private readonly ILogger<FileRequestsController> _logger;
    private readonly FileRequestNotifier _notifier;
    private readonly ServerConnectionTracker _tracker;

    public FileRequestsController(FederationDbContext db,
        ServerConnectionTracker tracker,
        IHubContext<FederationHub> hub,
        FileRequestNotifier notifier,
        ErrorContractMapper errorMapper,
        ILogger<FileRequestsController> logger)
    {
        _db = db;
        _tracker = tracker;
        _hub = hub;
        _notifier = notifier;
        _errorMapper = errorMapper;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<FileRequestDto>> Create(
        CreateFileRequestDto request)
    {
        var startedAt = Stopwatch.StartNew();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "file_request.create", "server", CorrelationId,
            request.OwningServerId.ToString());
        using var inFlight = FederationMetrics.BeginInflight("file_request.create", "server");

        var requestingServer = CurrentServer;
        LogCreateRequested(_logger, requestingServer.Id, request.OwningServerId, request.JellyfinItemId);

        var owningServer = await _db.Servers.FindAsync(request.OwningServerId).ConfigureAwait(false);
        if (owningServer is null)
        {
            LogCreateOwningServerNotFound(_logger, request.OwningServerId, requestingServer.Id);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.create", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.NotFound(
                "file_request.owning_server_not_found",
                "Owning server not found.",
                CorrelationId));
        }

        // Verify an accepted invitation exists between these two servers
        var invited = await _db.Invitations.AnyAsync(i =>
            i.Status == InvitationStatus.Accepted &&
            ((i.FromServerId == requestingServer.Id && i.ToServerId == owningServer.Id) ||
             (i.FromServerId == owningServer.Id && i.ToServerId == requestingServer.Id))).ConfigureAwait(false);

        if (!invited)
        {
            LogCreateForbiddenNoInvitation(_logger, requestingServer.Id, owningServer.Id);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.create", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.Authorization(
                "file_request.invitation_required",
                "An accepted invitation is required between both servers.",
                CorrelationId));
        }

        var fileRequest = new FileRequest
        {
            RequestingServerId = requestingServer.Id,
            OwningServerId = owningServer.Id,
            JellyfinItemId = request.JellyfinItemId
        };

        _db.FileRequests.Add(fileRequest);
        await _db.SaveChangesAsync().ConfigureAwait(false);
        LogCreated(_logger, fileRequest.Id, fileRequest.RequestingServerId, fileRequest.OwningServerId);

        // Notify both plugins so each can bind a UDP socket and signal ready for hole punching
        var ownerConn = _tracker.GetConnectionId(owningServer.Id);
        if (ownerConn is not null)
            await _hub.Clients.Client(ownerConn).SendAsync(
                    "FileRequestNotification",
                    new FileRequestNotification(fileRequest.Id, request.JellyfinItemId, requestingServer.Id, true))
                .ConfigureAwait(false);
        else
            LogCreateOwnerPluginOffline(_logger, fileRequest.Id, owningServer.Id);

        var requesterConn = _tracker.GetConnectionId(requestingServer.Id);
        if (requesterConn is not null)
            await _hub.Clients.Client(requesterConn).SendAsync(
                    "FileRequestNotification",
                    new FileRequestNotification(fileRequest.Id, request.JellyfinItemId, requestingServer.Id, false))
                .ConfigureAwait(false);
        else
            LogCreateRequesterPluginOffline(_logger, fileRequest.Id, requestingServer.Id);

        // Both server objects are already in memory — no extra DB round-trips needed
        fileRequest.RequestingServer = requestingServer;
        fileRequest.OwningServer = owningServer;
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("file_request.create", "server", FederationTelemetry.OutcomeSuccess,
            startedAt.Elapsed);
        return Ok(ToDto(fileRequest));
    }

    [HttpGet]
    public async Task<ActionResult<List<FileRequestDto>>> List(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (PaginationHeaders.Validate(page, pageSize, "file_request.pagination.invalid", CorrelationId) is { } validationFailure)
            return validationFailure;

        var pageRequest = new PageRequest(page, pageSize);
        var server = CurrentServer;

        var query = _db.FileRequests
            .AsNoTracking()
            .Where(r => r.RequestingServerId == server.Id || r.OwningServerId == server.Id);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        PaginationHeaders.Add(Response, pageRequest, total);

        // Project to an anonymous type to avoid loading full tracked server entities.
        // r.RequestingServer.Name and r.OwningServer.Name are translated to JOINs by EF Core.
        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.PageSize)
            .Select(r => new
            {
                r.Id,
                r.RequestingServerId,
                RequestingServerName = r.RequestingServer.Name,
                r.OwningServerId,
                OwningServerName = r.OwningServer.Name,
                r.JellyfinItemId,
                r.Status,
                r.SelectedTransportMode,
                r.FailureCategory,
                r.BytesTransferred,
                r.TotalBytes,
                r.FailureReason,
                r.CreatedAt
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var itemIds = requests.Select(r => r.JellyfinItemId).Distinct().ToList();
        var ownerIds = requests.Select(r => r.OwningServerId).Distinct().ToList();
        var titles = itemIds.Count == 0
            ? new Dictionary<(Guid ServerId, string JellyfinItemId), string>()
            : await _db.MediaItems
                .AsNoTracking()
                .Where(m => ownerIds.Contains(m.ServerId) && itemIds.Contains(m.JellyfinItemId))
                .Select(m => new { m.ServerId, m.JellyfinItemId, m.Title })
                .ToDictionaryAsync(m => (m.ServerId, m.JellyfinItemId), m => m.Title, cancellationToken)
                .ConfigureAwait(false);
        LogListReturned(_logger, server.Id, requests.Count);

        return Ok(requests.Select(r => new FileRequestDto(
            r.Id,
            r.RequestingServerId, r.RequestingServerName,
            r.OwningServerId, r.OwningServerName,
            r.JellyfinItemId, titles.GetValueOrDefault((r.OwningServerId, r.JellyfinItemId)),
            r.Status, r.SelectedTransportMode, r.FailureCategory,
            r.BytesTransferred, r.TotalBytes, r.FailureReason,
            r.FailureReason is null
                ? null
                : ErrorContractMapper.ToContract(new FailureDescriptor(
                    $"request.{(r.FailureCategory ?? TransferFailureCategory.Unknown).ToString().ToLowerInvariant()}",
                    r.FailureCategory switch
                    {
                        TransferFailureCategory.Timeout => FailureCategory.Timeout,
                        TransferFailureCategory.Connectivity => FailureCategory.Connectivity,
                        TransferFailureCategory.Reliability => FailureCategory.Reliability,
                        TransferFailureCategory.Cancelled => FailureCategory.Cancelled,
                        _ => FailureCategory.Unexpected
                    },
                    r.FailureReason)),
            r.CreatedAt)));
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var startedAt = Stopwatch.StartNew();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "file_request.cancel", "server", CorrelationId,
            releaseVersion: "server");

        var server = CurrentServer;

        var request = await _db.FileRequests
            .AsTracking()
            .FirstOrDefaultAsync(r => r.Id == id)
            .ConfigureAwait(false);
        if (request is null)
        {
            LogCancelNotFound(_logger, id, server.Id);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.cancel", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.NotFound(
                "file_request.not_found",
                "File request not found.",
                CorrelationId));
        }

        // Either party can cancel
        if (request.RequestingServerId != server.Id && request.OwningServerId != server.Id)
        {
            LogCancelForbidden(_logger, id, server.Id, request.RequestingServerId, request.OwningServerId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.cancel", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.Authorization(
                "file_request.cancel_forbidden",
                "Only participating servers can cancel this request.",
                CorrelationId));
        }

        // Can only cancel non-terminal requests
        if (request.Status is FileRequestStatus.Completed or FileRequestStatus.Cancelled)
        {
            LogCancelRejectedTerminal(_logger, id, request.Status);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.cancel", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.Conflict(
                "file_request.already_terminal",
                "Request is already in a terminal state.",
                CorrelationId));
        }

        request.Status = FileRequestStatus.Cancelled;
        request.FailureCategory = TransferFailureCategory.Cancelled;
        request.FailureReason = null;
        await _db.SaveChangesAsync().ConfigureAwait(false);

        await _notifier.SendCancelAsync(request).ConfigureAwait(false);
        LogCancelled(_logger, id, server.Id);
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("file_request.cancel", "server", FederationTelemetry.OutcomeSuccess,
            startedAt.Elapsed);

        return NoContent();
    }

    [HttpPut("{id}/complete")]
    public async Task<IActionResult> MarkCompleted(Guid id)
    {
        var startedAt = Stopwatch.StartNew();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "file_request.complete", "server", CorrelationId,
            releaseVersion: "server");

        var server = CurrentServer;

        var request = await _db.FileRequests
            .AsTracking()
            .FirstOrDefaultAsync(r => r.Id == id)
            .ConfigureAwait(false);
        if (request is null)
        {
            LogMarkCompleteNotFound(_logger, id, server.Id);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.complete", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.NotFound(
                "file_request.not_found",
                "File request not found.",
                CorrelationId));
        }

        // Only the receiver (requesting server) may mark a transfer as completed
        if (request.RequestingServerId != server.Id)
        {
            LogMarkCompleteForbidden(_logger, id, server.Id, request.RequestingServerId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.complete", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.Authorization(
                "file_request.complete_forbidden",
                "Only the requesting server can mark transfer completion.",
                CorrelationId));
        }

        if (request.Status != FileRequestStatus.Transferring)
        {
            LogMarkCompleteConflict(_logger, id, request.Status);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.complete", "server", FederationTelemetry.OutcomeError,
                startedAt.Elapsed);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.Conflict(
                "file_request.invalid_state",
                "Request is not in progress.",
                CorrelationId));
        }

        request.Status = FileRequestStatus.Completed;
        request.CompletedAt = DateTime.UtcNow;
        request.FailureCategory = null;
        request.FailureReason = null;
        await _db.SaveChangesAsync().ConfigureAwait(false);

        await _notifier.NotifyStatusAsync(request).ConfigureAwait(false);
        LogMarkedComplete(_logger, id, server.Id);
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("file_request.complete", "server", FederationTelemetry.OutcomeSuccess,
            startedAt.Elapsed);

        return NoContent();
    }

    private static FileRequestDto ToDto(FileRequest r, string? itemTitle = null)
    {
        return new FileRequestDto(r.Id,
            r.RequestingServerId, r.RequestingServer.Name,
            r.OwningServerId, r.OwningServer.Name,
            r.JellyfinItemId, itemTitle,
            r.Status,
            r.SelectedTransportMode,
            r.FailureCategory,
            r.BytesTransferred,
            r.TotalBytes,
            r.FailureReason,
            r.FailureReason is null
                ? null
                : ErrorContractMapper.ToContract(new FailureDescriptor(
                    $"request.{(r.FailureCategory ?? TransferFailureCategory.Unknown).ToString().ToLowerInvariant()}",
                    r.FailureCategory switch
                    {
                        TransferFailureCategory.Timeout => FailureCategory.Timeout,
                        TransferFailureCategory.Connectivity => FailureCategory.Connectivity,
                        TransferFailureCategory.Reliability => FailureCategory.Reliability,
                        TransferFailureCategory.Cancelled => FailureCategory.Cancelled,
                        _ => FailureCategory.Unexpected
                    },
                    r.FailureReason)),
            r.CreatedAt);
    }
}

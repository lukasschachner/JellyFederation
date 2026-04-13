using JellyFederation.Server.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Hubs;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public partial class FileRequestsController(
    FederationDbContext db,
    ServerConnectionTracker tracker,
    IHubContext<FederationHub> hub,
    FileRequestNotifier notifier,
    ILogger<FileRequestsController> logger) : AuthenticatedController
{
    [HttpPost]
    public async Task<ActionResult<FileRequestDto>> Create(
        CreateFileRequestDto request)
    {
        var startedAt = Stopwatch.StartNew();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "file_request.create", "server", CorrelationId, request.OwningServerId.ToString());
        using var inFlight = FederationMetrics.BeginInflight("file_request.create", "server");

        var requestingServer = CurrentServer;
        LogCreateRequested(logger, requestingServer.Id, request.OwningServerId, request.JellyfinItemId);

        var owningServer = await db.Servers.FindAsync(request.OwningServerId);
        if (owningServer is null)
        {
            LogCreateOwningServerNotFound(logger, request.OwningServerId, requestingServer.Id);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.create", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return NotFound("Owning server not found.");
        }

        // Verify an accepted invitation exists between these two servers
        var invited = await db.Invitations.AnyAsync(i =>
            i.Status == InvitationStatus.Accepted &&
            ((i.FromServerId == requestingServer.Id && i.ToServerId == owningServer.Id) ||
             (i.FromServerId == owningServer.Id && i.ToServerId == requestingServer.Id)));

        if (!invited)
        {
            LogCreateForbiddenNoInvitation(logger, requestingServer.Id, owningServer.Id);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.create", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return Forbid();
        }

        var fileRequest = new FileRequest
        {
            RequestingServerId = requestingServer.Id,
            OwningServerId = owningServer.Id,
            JellyfinItemId = request.JellyfinItemId
        };

        db.FileRequests.Add(fileRequest);
        await db.SaveChangesAsync();
        LogCreated(logger, fileRequest.Id, fileRequest.RequestingServerId, fileRequest.OwningServerId);

        // Notify both plugins so each can bind a UDP socket and signal ready for hole punching
        var ownerConn = tracker.GetConnectionId(owningServer.Id);
        if (ownerConn is not null)
        {
            await hub.Clients.Client(ownerConn).SendAsync(
                "FileRequestNotification",
                new FileRequestNotification(fileRequest.Id, request.JellyfinItemId, requestingServer.Id, IsSender: true));
        }
        else
        {
            LogCreateOwnerPluginOffline(logger, fileRequest.Id, owningServer.Id);
        }

        var requesterConn = tracker.GetConnectionId(requestingServer.Id);
        if (requesterConn is not null)
        {
            await hub.Clients.Client(requesterConn).SendAsync(
                "FileRequestNotification",
                new FileRequestNotification(fileRequest.Id, request.JellyfinItemId, requestingServer.Id, IsSender: false));
        }
        else
        {
            LogCreateRequesterPluginOffline(logger, fileRequest.Id, requestingServer.Id);
        }

        // Both server objects are already in memory — no extra DB round-trips needed
        fileRequest.RequestingServer = requestingServer;
        fileRequest.OwningServer = owningServer;
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("file_request.create", "server", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed);
        return Ok(ToDto(fileRequest));
    }

    [HttpGet]
    public async Task<ActionResult<List<FileRequestDto>>> List()
    {
        var server = CurrentServer;

        var requests = await db.FileRequests
            .Include(r => r.RequestingServer)
            .Include(r => r.OwningServer)
            .Where(r => r.RequestingServerId == server.Id || r.OwningServerId == server.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        // Batch-load item titles from the media items table
        var itemIds = requests.Select(r => r.JellyfinItemId).Distinct().ToList();
        var titles = await db.MediaItems
            .Where(m => itemIds.Contains(m.JellyfinItemId))
            .Select(m => new { m.JellyfinItemId, m.Title })
            .ToDictionaryAsync(m => m.JellyfinItemId, m => m.Title);
        LogListReturned(logger, server.Id, requests.Count);

        return Ok(requests.Select(r => ToDto(r, titles.GetValueOrDefault(r.JellyfinItemId))));
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var startedAt = Stopwatch.StartNew();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "file_request.cancel", "server", CorrelationId, releaseVersion: "server");

        var server = CurrentServer;

        var request = await db.FileRequests.FindAsync(id);
        if (request is null)
        {
            LogCancelNotFound(logger, id, server.Id);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.cancel", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return NotFound();
        }

        // Either party can cancel
        if (request.RequestingServerId != server.Id && request.OwningServerId != server.Id)
        {
            LogCancelForbidden(logger, id, server.Id, request.RequestingServerId, request.OwningServerId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.cancel", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return Forbid();
        }

        // Can only cancel non-terminal requests
        if (request.Status is FileRequestStatus.Completed or FileRequestStatus.Cancelled)
        {
            LogCancelRejectedTerminal(logger, id, request.Status);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.cancel", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return BadRequest("Request is already in a terminal state.");
        }

        request.Status = FileRequestStatus.Cancelled;
        request.FailureCategory = TransferFailureCategory.Cancelled;
        request.FailureReason = null;
        await db.SaveChangesAsync();

        await notifier.SendCancelAsync(request);
        LogCancelled(logger, id, server.Id);
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("file_request.cancel", "server", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed);

        return NoContent();
    }

    [HttpPut("{id}/complete")]
    public async Task<IActionResult> MarkCompleted(Guid id)
    {
        var startedAt = Stopwatch.StartNew();
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Server);
        FederationTelemetry.SetCommonTags(activity, "file_request.complete", "server", CorrelationId, releaseVersion: "server");

        var server = CurrentServer;

        var request = await db.FileRequests.FindAsync(id);
        if (request is null)
        {
            LogMarkCompleteNotFound(logger, id, server.Id);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.complete", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return NotFound();
        }
        // Only the receiver (requesting server) may mark a transfer as completed
        if (request.RequestingServerId != server.Id)
        {
            LogMarkCompleteForbidden(logger, id, server.Id, request.RequestingServerId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.complete", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return Forbid();
        }

        if (request.Status != FileRequestStatus.Transferring)
        {
            LogMarkCompleteConflict(logger, id, request.Status);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file_request.complete", "server", FederationTelemetry.OutcomeError, startedAt.Elapsed);
            return Conflict("Request is not in progress");
        }

        request.Status = FileRequestStatus.Completed;
        request.CompletedAt = DateTime.UtcNow;
        request.FailureCategory = null;
        request.FailureReason = null;
        await db.SaveChangesAsync();

        await notifier.NotifyStatusAsync(request);
        LogMarkedComplete(logger, id, server.Id);
        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
        FederationMetrics.RecordOperation("file_request.complete", "server", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed);

        return NoContent();
    }

    private static FileRequestDto ToDto(FileRequest r, string? itemTitle = null) =>
        new(r.Id,
            r.RequestingServerId, r.RequestingServer.Name,
            r.OwningServerId, r.OwningServer.Name,
            r.JellyfinItemId, itemTitle,
            r.Status,
            r.SelectedTransportMode,
            r.FailureCategory,
            r.BytesTransferred,
            r.TotalBytes,
            r.FailureReason,
            r.CreatedAt);
}

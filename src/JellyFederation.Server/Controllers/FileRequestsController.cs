using JellyFederation.Server.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Hubs;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class FileRequestsController(
    FederationDbContext db,
    ServerConnectionTracker tracker,
    IHubContext<FederationHub> hub) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<FileRequestDto>> Create(
        CreateFileRequestDto request)
    {
        var requestingServer = GetServer();

        var owningServer = await db.Servers.FindAsync(request.OwningServerId);
        if (owningServer is null) return NotFound("Owning server not found.");

        // Verify an accepted invitation exists between these two servers
        var invited = await db.Invitations.AnyAsync(i =>
            i.Status == InvitationStatus.Accepted &&
            ((i.FromServerId == requestingServer.Id && i.ToServerId == owningServer.Id) ||
             (i.FromServerId == owningServer.Id && i.ToServerId == requestingServer.Id)));

        if (!invited) return Forbid();

        var fileRequest = new FileRequest
        {
            RequestingServerId = requestingServer.Id,
            OwningServerId = owningServer.Id,
            JellyfinItemId = request.JellyfinItemId
        };

        db.FileRequests.Add(fileRequest);
        await db.SaveChangesAsync();

        // Notify both plugins so each can bind a UDP socket and signal ready for hole punching
        var ownerConn = tracker.GetConnectionId(owningServer.Id);
        if (ownerConn is not null)
        {
            await hub.Clients.Client(ownerConn).SendAsync(
                "FileRequestNotification",
                new FileRequestNotification(fileRequest.Id, request.JellyfinItemId, requestingServer.Id, IsSender: true));
        }

        var requesterConn = tracker.GetConnectionId(requestingServer.Id);
        if (requesterConn is not null)
        {
            await hub.Clients.Client(requesterConn).SendAsync(
                "FileRequestNotification",
                new FileRequestNotification(fileRequest.Id, request.JellyfinItemId, requestingServer.Id, IsSender: false));
        }

        await db.Entry(fileRequest).Reference(r => r.RequestingServer).LoadAsync();
        await db.Entry(fileRequest).Reference(r => r.OwningServer).LoadAsync();
        return Ok(ToDto(fileRequest));
    }

    [HttpGet]
    public async Task<ActionResult<List<FileRequestDto>>> List()
    {
        var server = GetServer();

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

        return Ok(requests.Select(r => ToDto(r, titles.GetValueOrDefault(r.JellyfinItemId))));
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var server = GetServer();

        var request = await db.FileRequests.FindAsync(id);
        if (request is null) return NotFound();

        // Either party can cancel
        if (request.RequestingServerId != server.Id && request.OwningServerId != server.Id)
            return Forbid();

        // Can only cancel non-terminal requests
        if (request.Status is FileRequestStatus.Completed or FileRequestStatus.Cancelled)
            return BadRequest("Request is already in a terminal state.");

        request.Status = FileRequestStatus.Cancelled;
        await db.SaveChangesAsync();

        var cancelMsg = new CancelTransfer(id);
        var cancelUpdate = new FileRequestStatusUpdate(id, "Cancelled", null);

        // Tell both plugins to stop
        var ownerConn = tracker.GetConnectionId(request.OwningServerId);
        if (ownerConn is not null)
            await hub.Clients.Client(ownerConn).SendAsync("CancelTransfer", cancelMsg);

        var requesterConn = tracker.GetConnectionId(request.RequestingServerId);
        if (requesterConn is not null)
            await hub.Clients.Client(requesterConn).SendAsync("CancelTransfer", cancelMsg);

        // Update browser clients
        await hub.Clients.Group($"server:{request.OwningServerId}").SendAsync("FileRequestStatusUpdate", cancelUpdate);
        await hub.Clients.Group($"server:{request.RequestingServerId}").SendAsync("FileRequestStatusUpdate", cancelUpdate);

        return NoContent();
    }

    [HttpPut("{id}/complete")]
    public async Task<IActionResult> MarkCompleted(Guid id)
    {
        var server = GetServer();

        var request = await db.FileRequests.FindAsync(id);
        if (request is null) return NotFound();
        // The receiver (requesting server) calls this when the download finishes
        if (request.RequestingServerId != server.Id && request.OwningServerId != server.Id)
            return Forbid();

        request.Status = FileRequestStatus.Completed;
        request.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var completedUpdate = new FileRequestStatusUpdate(id, "Completed", null);

        // Notify plugin connections
        var requesterConn = tracker.GetConnectionId(request.RequestingServerId);
        if (requesterConn is not null)
            await hub.Clients.Client(requesterConn).SendAsync("FileRequestStatusUpdate", completedUpdate);

        var ownerConn2 = tracker.GetConnectionId(request.OwningServerId);
        if (ownerConn2 is not null)
            await hub.Clients.Client(ownerConn2).SendAsync("FileRequestStatusUpdate", completedUpdate);

        // Notify browser clients
        await hub.Clients.Group($"server:{request.RequestingServerId}").SendAsync("FileRequestStatusUpdate", completedUpdate);
        await hub.Clients.Group($"server:{request.OwningServerId}").SendAsync("FileRequestStatusUpdate", completedUpdate);

        return NoContent();
    }

    private RegisteredServer GetServer() =>
        (RegisteredServer)HttpContext.Items["Server"]!;

    private static FileRequestDto ToDto(FileRequest r, string? itemTitle = null) =>
        new(r.Id,
            r.RequestingServerId, r.RequestingServer?.Name ?? "",
            r.OwningServerId, r.OwningServer?.Name ?? "",
            r.JellyfinItemId, itemTitle,
            r.Status, r.FailureReason, r.CreatedAt);
}

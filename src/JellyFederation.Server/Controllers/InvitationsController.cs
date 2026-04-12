using JellyFederation.Server.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public partial class InvitationsController(
    FederationDbContext db,
    ILogger<InvitationsController> logger) : AuthenticatedController
{
    [HttpPost]
    public async Task<ActionResult<InvitationDto>> Send(
        SendInvitationRequest request)
    {
        var fromServer = CurrentServer;
        LogInvitationSendRequested(logger, fromServer.Id, request.ToServerId);

        var toServer = await db.Servers.FindAsync(request.ToServerId);
        if (toServer is null)
        {
            LogInvitationTargetNotFound(logger, fromServer.Id, request.ToServerId);
            return NotFound("Target server not found.");
        }

        var existingRelationship = await db.Invitations.AnyAsync(i =>
            (i.FromServerId == fromServer.Id && i.ToServerId == toServer.Id ||
             i.FromServerId == toServer.Id && i.ToServerId == fromServer.Id) &&
            (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Accepted));

        if (existingRelationship)
        {
            LogInvitationRelationshipExists(logger, fromServer.Id, toServer.Id);
            return Conflict("A relationship already exists between these servers");
        }

        var invitation = new Invitation
        {
            FromServerId = fromServer.Id,
            ToServerId = toServer.Id
        };

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();
        LogInvitationCreated(logger, invitation.Id, invitation.FromServerId, invitation.ToServerId);

        return Ok(ToDto(invitation, fromServer.Name, toServer.Name));
    }

    [HttpGet]
    public async Task<ActionResult<List<InvitationDto>>> List()
    {
        var server = CurrentServer;

        var invitations = await db.Invitations
            .Include(i => i.FromServer)
            .Include(i => i.ToServer)
            .Where(i => i.FromServerId == server.Id || i.ToServerId == server.Id)
            .ToListAsync();
        LogInvitationListReturned(logger, server.Id, invitations.Count);

        return Ok(invitations.Select(i =>
            ToDto(i, i.FromServer.Name, i.ToServer.Name)));
    }

    [HttpPut("{id}/respond")]
    public async Task<ActionResult<InvitationDto>> Respond(
        Guid id,
        RespondToInvitationRequest request)
    {
        var server = CurrentServer;

        var invitation = await db.Invitations
            .Include(i => i.FromServer)
            .Include(i => i.ToServer)
            .FirstOrDefaultAsync(i => i.Id == id && i.ToServerId == server.Id);

        if (invitation is null)
        {
            LogInvitationRespondNotFound(logger, id, server.Id);
            return NotFound();
        }
        if (invitation.Status != InvitationStatus.Pending)
        {
            LogInvitationRespondNotPending(logger, id, invitation.Status);
            return Conflict("Invitation is no longer pending.");
        }

        invitation.Status = request.Accept ? InvitationStatus.Accepted : InvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        LogInvitationResponded(logger, invitation.Id, server.Id, request.Accept);

        return Ok(ToDto(invitation, invitation.FromServer.Name, invitation.ToServer.Name));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var server = CurrentServer;

        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Id == id && i.FromServerId == server.Id);

        if (invitation is null)
        {
            LogInvitationRevokeNotFound(logger, id, server.Id);
            return NotFound();
        }

        invitation.Status = InvitationStatus.Revoked;
        await db.SaveChangesAsync();
        LogInvitationRevoked(logger, invitation.Id, server.Id);

        return NoContent();
    }

    private static InvitationDto ToDto(Invitation i, string fromName, string toName) =>
        new(i.Id, i.FromServerId, fromName, i.ToServerId, toName, i.Status, i.CreatedAt);
}

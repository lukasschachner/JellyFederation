using JellyFederation.Server.Data;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvitationsController(FederationDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InvitationDto>> Send(
        SendInvitationRequest request,
        [FromHeader(Name = "X-Api-Key")] string apiKey)
    {
        var fromServer = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (fromServer is null) return Unauthorized();

        var toServer = await db.Servers.FindAsync(request.ToServerId);
        if (toServer is null) return NotFound("Target server not found.");

        var existingRelationship = await db.Invitations.AnyAsync(i =>
            (i.FromServerId == fromServer.Id && i.ToServerId == toServer.Id ||
             i.FromServerId == toServer.Id && i.ToServerId == fromServer.Id) &&
            (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Accepted));

        if (existingRelationship) return Conflict("A relationship already exists between these servers");

        var invitation = new Invitation
        {
            FromServerId = fromServer.Id,
            ToServerId = toServer.Id
        };

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        return Ok(ToDto(invitation, fromServer.Name, toServer.Name));
    }

    [HttpGet]
    public async Task<ActionResult<List<InvitationDto>>> List(
        [FromHeader(Name = "X-Api-Key")] string apiKey)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

        var invitations = await db.Invitations
            .Include(i => i.FromServer)
            .Include(i => i.ToServer)
            .Where(i => i.FromServerId == server.Id || i.ToServerId == server.Id)
            .ToListAsync();

        return Ok(invitations.Select(i =>
            ToDto(i, i.FromServer.Name, i.ToServer.Name)));
    }

    [HttpPut("{id}/respond")]
    public async Task<ActionResult<InvitationDto>> Respond(
        Guid id,
        RespondToInvitationRequest request,
        [FromHeader(Name = "X-Api-Key")] string apiKey)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

        var invitation = await db.Invitations
            .Include(i => i.FromServer)
            .Include(i => i.ToServer)
            .FirstOrDefaultAsync(i => i.Id == id && i.ToServerId == server.Id);

        if (invitation is null) return NotFound();
        if (invitation.Status != InvitationStatus.Pending)
            return Conflict("Invitation is no longer pending.");

        invitation.Status = request.Accept ? InvitationStatus.Accepted : InvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(ToDto(invitation, invitation.FromServer.Name, invitation.ToServer.Name));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(
        Guid id,
        [FromHeader(Name = "X-Api-Key")] string apiKey)
    {
        var server = await db.Servers.FirstOrDefaultAsync(s => s.ApiKey == apiKey);
        if (server is null) return Unauthorized();

        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Id == id && i.FromServerId == server.Id);

        if (invitation is null) return NotFound();

        invitation.Status = InvitationStatus.Revoked;
        await db.SaveChangesAsync();

        return NoContent();
    }

    private static InvitationDto ToDto(Invitation i, string fromName, string toName) =>
        new(i.Id, i.FromServerId, fromName, i.ToServerId, toName, i.Status, i.CreatedAt);
}

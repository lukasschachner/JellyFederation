using JellyFederation.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public partial class InvitationsController : AuthenticatedController
{
    private readonly FederationDbContext _db;
    private readonly ErrorContractMapper _errorMapper;
    private readonly ILogger<InvitationsController> _logger;

    public InvitationsController(FederationDbContext db,
        ErrorContractMapper errorMapper,
        ILogger<InvitationsController> logger)
    {
        _db = db;
        _errorMapper = errorMapper;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<InvitationDto>> Send(
        SendInvitationRequest request)
    {
        var fromServer = CurrentServer;
        LogInvitationSendRequested(_logger, fromServer.Id, request.ToServerId);

        var toServer = await _db.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.ToServerId)
            .ConfigureAwait(false);
        if (toServer is null)
        {
            LogInvitationTargetNotFound(_logger, fromServer.Id, request.ToServerId);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.NotFound(
                "invitation.target_not_found",
                "Target server not found.",
                CorrelationId));
        }

        var existingRelationship = await _db.Invitations.AnyAsync(i =>
            ((i.FromServerId == fromServer.Id && i.ToServerId == toServer.Id) ||
             (i.FromServerId == toServer.Id && i.ToServerId == fromServer.Id)) &&
            (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Accepted)).ConfigureAwait(false);

        if (existingRelationship)
        {
            LogInvitationRelationshipExists(_logger, fromServer.Id, toServer.Id);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.Conflict(
                "invitation.relationship_exists",
                "A relationship already exists between these servers.",
                CorrelationId));
        }

        var invitation = new Invitation
        {
            FromServerId = fromServer.Id,
            ToServerId = toServer.Id
        };

        _db.Invitations.Add(invitation);
        await _db.SaveChangesAsync().ConfigureAwait(false);
        LogInvitationCreated(_logger, invitation.Id, invitation.FromServerId, invitation.ToServerId);

        return Ok(ToDto(invitation, fromServer.Name, toServer.Name));
    }

    [HttpGet]
    public async Task<ActionResult<List<InvitationDto>>> List()
    {
        var server = CurrentServer;

        var invitations = await _db.Invitations
            .AsNoTracking()
            .Where(i => i.FromServerId == server.Id || i.ToServerId == server.Id)
            .Select(i => new InvitationDto(
                i.Id,
                i.FromServerId,
                i.FromServer.Name,
                i.ToServerId,
                i.ToServer.Name,
                i.Status,
                i.CreatedAt))
            .ToListAsync().ConfigureAwait(false);
        LogInvitationListReturned(_logger, server.Id, invitations.Count);

        return Ok(invitations);
    }

    [HttpPut("{id}/respond")]
    public async Task<ActionResult<InvitationDto>> Respond(
        Guid id,
        RespondToInvitationRequest request)
    {
        var server = CurrentServer;

        // AsTracking required — invitation.Status is mutated and saved below.
        var invitation = await _db.Invitations
            .AsTracking()
            .Include(i => i.FromServer)
            .Include(i => i.ToServer)
            .FirstOrDefaultAsync(i => i.Id == id && i.ToServerId == server.Id).ConfigureAwait(false);

        if (invitation is null)
        {
            LogInvitationRespondNotFound(_logger, id, server.Id);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.NotFound(
                "invitation.not_found",
                "Invitation not found.",
                CorrelationId));
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            LogInvitationRespondNotPending(_logger, id, invitation.Status);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.Conflict(
                "invitation.not_pending",
                "Invitation is no longer pending.",
                CorrelationId));
        }

        invitation.Status = request.Accept ? InvitationStatus.Accepted : InvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync().ConfigureAwait(false);
        LogInvitationResponded(_logger, invitation.Id, server.Id, request.Accept);

        return Ok(ToDto(invitation, invitation.FromServer.Name, invitation.ToServer.Name));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var server = CurrentServer;

        // AsTracking required — invitation.Status is mutated and saved below.
        var invitation = await _db.Invitations
            .AsTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.FromServerId == server.Id).ConfigureAwait(false);

        if (invitation is null)
        {
            LogInvitationRevokeNotFound(_logger, id, server.Id);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.NotFound(
                "invitation.not_found",
                "Invitation not found.",
                CorrelationId));
        }

        invitation.Status = InvitationStatus.Revoked;
        await _db.SaveChangesAsync().ConfigureAwait(false);
        LogInvitationRevoked(_logger, invitation.Id, server.Id);

        return NoContent();
    }

    private static InvitationDto ToDto(Invitation i, string fromName, string toName)
    {
        return new InvitationDto(i.Id, i.FromServerId, fromName, i.ToServerId, toName, i.Status, i.CreatedAt);
    }
}

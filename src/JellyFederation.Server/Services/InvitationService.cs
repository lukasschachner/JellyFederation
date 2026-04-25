using JellyFederation.Data;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Services;

public sealed class InvitationService
{
    private readonly FederationDbContext _db;

    public InvitationService(FederationDbContext db)
    {
        _db = db;
    }

    public async Task<OperationOutcome<InvitationDto>> SendAsync(
        RegisteredServer fromServer,
        SendInvitationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var toServer = await _db.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.ToServerId, cancellationToken)
            .ConfigureAwait(false);
        if (toServer is null)
        {
            return OperationOutcome<InvitationDto>.Fail(FailureDescriptor.NotFound(
                "invitation.target_not_found",
                "Target server not found.",
                correlationId));
        }

        var existingRelationship = await _db.Invitations.AnyAsync(i =>
            ((i.FromServerId == fromServer.Id && i.ToServerId == toServer.Id) ||
             (i.FromServerId == toServer.Id && i.ToServerId == fromServer.Id)) &&
            (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Accepted), cancellationToken)
            .ConfigureAwait(false);

        if (existingRelationship)
        {
            return OperationOutcome<InvitationDto>.Fail(FailureDescriptor.Conflict(
                "invitation.relationship_exists",
                "A relationship already exists between these servers.",
                correlationId));
        }

        var invitation = new Invitation
        {
            FromServerId = fromServer.Id,
            ToServerId = toServer.Id
        };

        _db.Invitations.Add(invitation);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationOutcome<InvitationDto>.Success(ToDto(invitation, fromServer.Name, toServer.Name));
    }

    public async Task<int> CountAsync(
        RegisteredServer server,
        CancellationToken cancellationToken = default)
    {
        return await _db.Invitations
            .AsNoTracking()
            .Where(i => i.FromServerId == server.Id || i.ToServerId == server.Id)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InvitationDto>> ListAsync(
        RegisteredServer server,
        int skip = 0,
        int take = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return await _db.Invitations
            .AsNoTracking()
            .Where(i => i.FromServerId == server.Id || i.ToServerId == server.Id)
            .OrderByDescending(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Skip(skip)
            .Take(take)
            .Select(i => new InvitationDto(
                i.Id,
                i.FromServerId,
                i.FromServer.Name,
                i.ToServerId,
                i.ToServer.Name,
                i.Status,
                i.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationOutcome<InvitationDto>> RespondAsync(
        RegisteredServer server,
        Guid invitationId,
        RespondToInvitationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await _db.Invitations
            .AsTracking()
            .Include(i => i.FromServer)
            .Include(i => i.ToServer)
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.ToServerId == server.Id, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            return OperationOutcome<InvitationDto>.Fail(FailureDescriptor.NotFound(
                "invitation.not_found",
                "Invitation not found.",
                correlationId));
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return OperationOutcome<InvitationDto>.Fail(FailureDescriptor.Conflict(
                "invitation.not_pending",
                "Invitation is no longer pending.",
                correlationId));
        }

        invitation.Status = request.Accept ? InvitationStatus.Accepted : InvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationOutcome<InvitationDto>.Success(
            ToDto(invitation, invitation.FromServer.Name, invitation.ToServer.Name));
    }

    public async Task<OperationOutcome<bool>> RevokeAsync(
        RegisteredServer server,
        Guid invitationId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await _db.Invitations
            .AsTracking()
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.FromServerId == server.Id, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            return OperationOutcome<bool>.Fail(FailureDescriptor.NotFound(
                "invitation.not_found",
                "Invitation not found.",
                correlationId));
        }

        invitation.Status = InvitationStatus.Revoked;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationOutcome<bool>.Success(true);
    }

    private static InvitationDto ToDto(Invitation invitation, string fromName, string toName)
    {
        return new InvitationDto(
            invitation.Id,
            invitation.FromServerId,
            fromName,
            invitation.ToServerId,
            toName,
            invitation.Status,
            invitation.CreatedAt);
    }
}

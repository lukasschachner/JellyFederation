using JellyFederation.Server.Auth;
using JellyFederation.Server.Pagination;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize(AuthenticationSchemes = FederationAuthSchemes.ApiKeyOrSession)]
public partial class InvitationsController : AuthenticatedController
{
    private readonly InvitationService _invitations;
    private readonly ILogger<InvitationsController> _logger;

    public InvitationsController(
        InvitationService invitations,
        ILogger<InvitationsController> logger)
    {
        _invitations = invitations;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<InvitationDto>> Send(
        SendInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var fromServer = CurrentServer;
        LogInvitationSendRequested(_logger, fromServer.Id, request.ToServerId);

        var outcome = await _invitations.SendAsync(fromServer, request, CorrelationId, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            var failure = outcome.Failure!;
            if (failure.Code == "invitation.target_not_found")
                LogInvitationTargetNotFound(_logger, fromServer.Id, request.ToServerId);
            else if (failure.Code == "invitation.relationship_exists")
                LogInvitationRelationshipExists(_logger, fromServer.Id, request.ToServerId);

            return ErrorContractMapper.ToActionResult(failure);
        }

        var invitation = outcome.RequireValue();
        LogInvitationCreated(_logger, invitation.Id, invitation.FromServerId, invitation.ToServerId);
        return Ok(invitation);
    }

    [HttpGet]
    public async Task<ActionResult<List<InvitationDto>>> List(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (PaginationHeaders.Validate(page, pageSize, "invitation.pagination.invalid", CorrelationId) is { } validationFailure)
            return validationFailure;

        var pageRequest = new PageRequest(page, pageSize);
        var server = CurrentServer;

        var total = await _invitations.CountAsync(server, cancellationToken).ConfigureAwait(false);
        PaginationHeaders.Add(Response, pageRequest, total);

        var invitations = await _invitations
            .ListAsync(server, pageRequest.Skip, pageRequest.PageSize, cancellationToken)
            .ConfigureAwait(false);
        LogInvitationListReturned(_logger, server.Id, invitations.Count);

        return Ok(invitations);
    }

    [HttpPut("{id}/respond")]
    public async Task<ActionResult<InvitationDto>> Respond(
        Guid id,
        RespondToInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var server = CurrentServer;
        var outcome = await _invitations.RespondAsync(server, id, request, CorrelationId, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            var failure = outcome.Failure!;
            if (failure.Code == "invitation.not_found")
                LogInvitationRespondNotFound(_logger, id, server.Id);
            else if (failure.Code == "invitation.not_pending")
                LogInvitationRespondNotPending(_logger, id, InvitationStatus.Revoked);

            return ErrorContractMapper.ToActionResult(failure);
        }

        var invitation = outcome.RequireValue();
        LogInvitationResponded(_logger, invitation.Id, server.Id, request.Accept);
        return Ok(invitation);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var server = CurrentServer;
        var outcome = await _invitations.RevokeAsync(server, id, CorrelationId, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            LogInvitationRevokeNotFound(_logger, id, server.Id);
            return ErrorContractMapper.ToActionResult(outcome.Failure!);
        }

        LogInvitationRevoked(_logger, id, server.Id);
        return NoContent();
    }
}

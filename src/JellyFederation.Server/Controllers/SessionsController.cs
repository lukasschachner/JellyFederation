using JellyFederation.Data;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class SessionsController : ControllerBase
{
    private readonly FederationDbContext _db;
    private readonly WebSessionService _sessions;

    public SessionsController(FederationDbContext db, WebSessionService sessions)
    {
        _db = db;
        _sessions = sessions;
    }

    [HttpPost]
    public async Task<ActionResult<WebSessionResponse>> Create(CreateWebSessionRequest request)
    {
        var server = await _db.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.ServerId && s.ApiKey == request.ApiKey)
            .ConfigureAwait(false);

        if (server is null)
            return ErrorContractMapper.ToActionResult(FailureDescriptor.Authorization(
                "session.invalid_credentials",
                "Invalid server ID or API key."));

        Response.Cookies.Append(
            WebSessionService.CookieName,
            _sessions.CreateSessionCookieValue(server),
            _sessions.CreateCookieOptions(Request));

        return Ok(new WebSessionResponse(server.Id, server.Name));
    }

    [HttpDelete]
    public IActionResult Delete()
    {
        WebSessionService.DeleteSessionCookie(Response);
        return NoContent();
    }
}

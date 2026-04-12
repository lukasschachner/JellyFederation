using System.Threading.RateLimiting;
using JellyFederation.Server.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class ServersController(
    FederationDbContext db,
    IConfiguration configuration,
    ILogger<ServersController> logger) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("register")]
    public async Task<ActionResult<RegisterServerResponse>> Register(RegisterServerRequest request)
    {
        LogRegisterAttempt(logger, request.Name, request.OwnerUserId);
        var adminToken = configuration["AdminToken"];
        if (!string.IsNullOrEmpty(adminToken))
        {
            var providedToken = Request.Headers["X-Admin-Token"].FirstOrDefault();
            if (providedToken != adminToken)
            {
                LogRegisterRejectedAdminToken(logger, request.Name);
                return Unauthorized("Invalid or missing admin token.");
            }
        }

        var server = new RegisteredServer
        {
            Name = request.Name,
            OwnerUserId = request.OwnerUserId,
            ApiKey = ApiKeyService.Generate()
        };

        db.Servers.Add(server);
        await db.SaveChangesAsync();
        LogRegisterSucceeded(logger, server.Id, server.Name);

        return Ok(new RegisterServerResponse(server.Id, server.ApiKey));
    }

    [HttpGet]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    public async Task<ActionResult<List<ServerInfoDto>>> List()
    {
        var servers = await db.Servers
            .Select(s => new ServerInfoDto(
                s.Id, s.Name, s.OwnerUserId, s.IsOnline, s.LastSeenAt, s.MediaItems.Count))
            .ToListAsync();
        LogListedServers(logger, servers.Count);

        return Ok(servers);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ServerInfoDto>> Get(Guid id)
    {
        var server = await db.Servers
            .Where(s => s.Id == id)
            .Select(s => new ServerInfoDto(
                s.Id, s.Name, s.OwnerUserId, s.IsOnline, s.LastSeenAt, s.MediaItems.Count))
            .FirstOrDefaultAsync();

        if (server is null)
        {
            LogServerNotFound(logger, id);
            return NotFound();
        }

        LogServerFetched(logger, id);

        return Ok(server);
    }
}

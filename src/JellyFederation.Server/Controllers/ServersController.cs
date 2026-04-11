using System.Threading.RateLimiting;
using JellyFederation.Server.Data;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServersController(FederationDbContext db, IConfiguration configuration) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("register")]
    public async Task<ActionResult<RegisterServerResponse>> Register(RegisterServerRequest request)
    {
        var adminToken = configuration["AdminToken"];
        if (!string.IsNullOrEmpty(adminToken))
        {
            var providedToken = Request.Headers["X-Admin-Token"].FirstOrDefault();
            if (providedToken != adminToken)
                return Unauthorized("Invalid or missing admin token.");
        }

        var server = new RegisteredServer
        {
            Name = request.Name,
            OwnerUserId = request.OwnerUserId,
            ApiKey = ApiKeyService.Generate()
        };

        db.Servers.Add(server);
        await db.SaveChangesAsync();

        return Ok(new RegisterServerResponse(server.Id, server.ApiKey));
    }

    [HttpGet]
    public async Task<ActionResult<List<ServerInfoDto>>> List()
    {
        var servers = await db.Servers
            .Select(s => new ServerInfoDto(
                s.Id, s.Name, s.OwnerUserId, s.IsOnline, s.LastSeenAt, s.MediaItems.Count))
            .ToListAsync();

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

        if (server is null) return NotFound();

        return Ok(server);
    }
}

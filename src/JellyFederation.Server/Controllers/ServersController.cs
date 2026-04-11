using JellyFederation.Server.Data;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServersController(FederationDbContext db) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<RegisterServerResponse>> Register(RegisterServerRequest request)
    {
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
            .Include(s => s.MediaItems)
            .ToListAsync();

        return Ok(servers.Select(s => new ServerInfoDto(
            s.Id, s.Name, s.OwnerUserId, s.IsOnline, s.LastSeenAt, s.MediaItems.Count)));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ServerInfoDto>> Get(Guid id)
    {
        var server = await db.Servers
            .Include(s => s.MediaItems)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (server is null) return NotFound();

        return Ok(new ServerInfoDto(
            server.Id, server.Name, server.OwnerUserId,
            server.IsOnline, server.LastSeenAt, server.MediaItems.Count));
    }
}

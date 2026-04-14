using JellyFederation.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class ServersController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly FederationDbContext _db;
    private readonly ErrorContractMapper _errorMapper;
    private readonly ILogger<ServersController> _logger;

    public ServersController(FederationDbContext db,
        IConfiguration configuration,
        ErrorContractMapper errorMapper,
        ILogger<ServersController> logger)
    {
        _db = db;
        _configuration = configuration;
        _errorMapper = errorMapper;
        _logger = logger;
    }

    [HttpPost("register")]
    [EnableRateLimiting("register")]
    public async Task<ActionResult<RegisterServerResponse>> Register(RegisterServerRequest request)
    {
        LogRegisterAttempt(_logger, request.Name, request.OwnerUserId);
        var adminToken = _configuration["AdminToken"];
        if (!string.IsNullOrEmpty(adminToken))
        {
            var providedToken = Request.Headers["X-Admin-Token"].FirstOrDefault();
            if (providedToken != adminToken)
            {
                LogRegisterRejectedAdminToken(_logger, request.Name);
                return ErrorContractMapper.ToActionResult(FailureDescriptor.Authorization(
                    "server.register.invalid_admin_token",
                    "Invalid or missing admin token."));
            }
        }

        var server = new RegisteredServer
        {
            Name = request.Name,
            OwnerUserId = request.OwnerUserId,
            ApiKey = ApiKeyService.Generate()
        };

        _db.Servers.Add(server);
        await _db.SaveChangesAsync().ConfigureAwait(false);
        LogRegisterSucceeded(_logger, server.Id, server.Name);

        return Ok(new RegisterServerResponse(server.Id, server.ApiKey));
    }

    [HttpGet]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    public async Task<ActionResult<List<ServerInfoDto>>> List()
    {
        var servers = await _db.Servers
            .Select(s => new ServerInfoDto(
                s.Id, s.Name, s.OwnerUserId, s.IsOnline, s.LastSeenAt, s.MediaItems.Count))
            .ToListAsync().ConfigureAwait(false);
        LogListedServers(_logger, servers.Count);

        return Ok(servers);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ServerInfoDto>> Get(Guid id)
    {
        var server = await _db.Servers
            .Where(s => s.Id == id)
            .Select(s => new ServerInfoDto(
                s.Id, s.Name, s.OwnerUserId, s.IsOnline, s.LastSeenAt, s.MediaItems.Count))
            .FirstOrDefaultAsync().ConfigureAwait(false);

        if (server is null)
        {
            LogServerNotFound(_logger, id);
            return ErrorContractMapper.ToActionResult(FailureDescriptor.NotFound(
                "server.not_found",
                "Server not found."));
        }

        LogServerFetched(_logger, id);

        return Ok(server);
    }
}

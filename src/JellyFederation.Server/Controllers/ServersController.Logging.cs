using Microsoft.Extensions.Logging;

namespace JellyFederation.Server.Controllers;

public partial class ServersController
{
    [LoggerMessage(1, LogLevel.Information, "Register request for server {Name} owner {OwnerUserId}")]
    private static partial void LogRegisterAttempt(ILogger logger, string name, string ownerUserId);

    [LoggerMessage(2, LogLevel.Warning, "Register rejected due to invalid admin token for server {Name}")]
    private static partial void LogRegisterRejectedAdminToken(ILogger logger, string name);

    [LoggerMessage(3, LogLevel.Information, "Registered server {Name} ({ServerId})")]
    private static partial void LogRegisterSucceeded(ILogger logger, Guid serverId, string name);

    [LoggerMessage(4, LogLevel.Debug, "Listed {Count} registered server(s)")]
    private static partial void LogListedServers(ILogger logger, int count);

    [LoggerMessage(5, LogLevel.Warning, "Server {ServerId} not found")]
    private static partial void LogServerNotFound(ILogger logger, Guid serverId);

    [LoggerMessage(6, LogLevel.Debug, "Fetched server {ServerId}")]
    private static partial void LogServerFetched(ILogger logger, Guid serverId);
}

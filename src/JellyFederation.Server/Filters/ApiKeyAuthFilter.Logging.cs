namespace JellyFederation.Server.Filters;

public partial class ApiKeyAuthFilter
{
    [LoggerMessage(1, LogLevel.Warning, "API key missing for {Path}")]
    private static partial void LogMissingApiKey(ILogger logger, string path);

    [LoggerMessage(2, LogLevel.Debug, "API key cache miss for {Path}")]
    private static partial void LogApiKeyCacheMiss(ILogger logger, string path);

    [LoggerMessage(3, LogLevel.Debug, "API key cache hit for {Path} (server {ServerId})")]
    private static partial void LogApiKeyCacheHit(ILogger logger, string path, Guid serverId);

    [LoggerMessage(4, LogLevel.Warning, "API key rejected for {Path}")]
    private static partial void LogApiKeyRejected(ILogger logger, string path);

    [LoggerMessage(5, LogLevel.Debug, "API key authenticated for {Path} (server {ServerId})")]
    private static partial void LogApiKeyAuthenticated(ILogger logger, string path, Guid serverId);
}
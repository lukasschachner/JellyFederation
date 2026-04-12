using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

public partial class FederationStartupService
{
    [LoggerMessage(1, LogLevel.Warning, "Federation plugin is not configured. Set Federation Server URL and API key in plugin settings.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(2, LogLevel.Information, "Federation plugin connected successfully (trace={TraceId}, span={SpanId}, correlation={CorrelationId})")]
    private static partial void LogConnectedSuccessfully(ILogger logger, string? traceId, string? spanId, string correlationId);

    [LoggerMessage(3, LogLevel.Warning, "Failed to connect to federation server (attempt {Attempt}). Retrying in {Delay}s (trace={TraceId}, span={SpanId}, correlation={CorrelationId})")]
    private static partial void LogConnectionFailed(ILogger logger, Exception ex, int attempt, int delay, string? traceId, string? spanId, string correlationId);

    [LoggerMessage(4, LogLevel.Debug, "Skipping library re-sync because SignalR is not connected")]
    private static partial void LogSkippingResync(ILogger logger);

    [LoggerMessage(5, LogLevel.Error, "Library re-sync failed")]
    private static partial void LogResyncFailed(ILogger logger, Exception ex);
}

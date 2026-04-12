using JellyFederation.Shared.SignalR;
using Microsoft.Extensions.Logging;
using System.Net;

namespace JellyFederation.Plugin.Services;

public partial class HolePunchService
{
    [LoggerMessage(1, LogLevel.Information, "Reusing existing UDP socket on port {Port} for file request {Id}")]
    private static partial void LogReusingSocket(ILogger logger, int port, Guid id);

    [LoggerMessage(2, LogLevel.Warning, "Configured port {Port} is already in use for request {Id} — falling back to ephemeral port")]
    private static partial void LogPortInUse(ILogger logger, Exception ex, int port, Guid id);

    [LoggerMessage(3, LogLevel.Information, "Bound UDP socket on port {Port} for file request {Id}")]
    private static partial void LogBoundSocket(ILogger logger, int port, Guid id);

    [LoggerMessage(4, LogLevel.Error, "Failed to bind receiver socket on port {Port} for request {Id} — reporting failure")]
    private static partial void LogBindFailed(ILogger logger, Exception ex, int port, Guid id);

    [LoggerMessage(5, LogLevel.Warning, "Failed to report bind failure for request {Id}")]
    private static partial void LogReportBindFailureFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(6, LogLevel.Error, "Invalid remote endpoint: {Ep}")]
    private static partial void LogInvalidEndpoint(ILogger logger, string ep);

    [LoggerMessage(7, LogLevel.Information, "Starting hole punch to {Ep} (role: {Role})")]
    private static partial void LogStartingHolePunch(ILogger logger, IPEndPoint ep, HolePunchRole role);

    [LoggerMessage(8, LogLevel.Information, "Hole punched successfully to {Ep}")]
    private static partial void LogHolePunchSuccess(ILogger logger, IPEndPoint ep);

    [LoggerMessage(9, LogLevel.Warning, "Hole punch timed out for request {Id}")]
    private static partial void LogHolePunchTimeout(ILogger logger, Guid id);

    [LoggerMessage(10, LogLevel.Warning, "Failed to send HolePunchResult for request {Id} — connection may be closed")]
    private static partial void LogSendResultFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(11, LogLevel.Information, "Received valid probe from {Ep} ({Bytes} bytes)")]
    private static partial void LogProbeReceived(ILogger logger, IPEndPoint ep, int bytes);

    [LoggerMessage(12, LogLevel.Debug, "Received {Bytes} bytes (not a valid probe)")]
    private static partial void LogInvalidProbe(ILogger logger, int bytes);
}

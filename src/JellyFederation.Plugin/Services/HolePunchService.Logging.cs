using System.Net;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

public partial class HolePunchService
{
    [LoggerMessage(1, LogLevel.Information, "Reusing existing UDP socket on port {Port} for file request {Id}")]
    private static partial void LogReusingSocket(ILogger logger, int port, Guid id);

    [LoggerMessage(2, LogLevel.Warning,
        "Configured port {Port} is already in use for request {Id} — falling back to ephemeral port")]
    private static partial void LogPortInUse(ILogger logger, Exception ex, int port, Guid id);

    [LoggerMessage(3, LogLevel.Information, "Bound UDP socket on port {Port} for file request {Id}")]
    private static partial void LogBoundSocket(ILogger logger, int port, Guid id);

    [LoggerMessage(4, LogLevel.Error,
        "Failed to bind receiver socket on port {Port} for request {Id} — reporting failure")]
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

    [LoggerMessage(13, LogLevel.Information, "Request {Id} transport mode selected: {Mode} ({Reason})")]
    private static partial void LogTransportMode(
        ILogger logger,
        Guid id,
        TransferTransportMode mode,
        TransferSelectionReason reason);

    [LoggerMessage(14, LogLevel.Warning, "Unexpected cancellation after hole punch for request {Id}")]
    private static partial void LogUnexpectedCancellation(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(15, LogLevel.Information,
        "Hole punch readiness for request {Id}: preferQuic={PreferQuic}, quicSupported={QuicSupported}, advertisedSupportsQuic={AdvertisedSupportsQuic}, thresholdBytes={ThresholdBytes}, localPort={LocalPort}, overrideIp={OverrideIp}")]
    private static partial void LogHolePunchReadinessCapabilities(
        ILogger logger,
        Guid id,
        bool preferQuic,
        bool quicSupported,
        bool advertisedSupportsQuic,
        long thresholdBytes,
        int localPort,
        string overrideIp);

    [LoggerMessage(16, LogLevel.Error, "Transfer execution failed after hole punch for request {Id}")]
    private static partial void LogTransferExecutionFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(17, LogLevel.Warning,
        "Hole punch failure descriptor for request {Id}: code={Code}, category={Category}, message={Message}")]
    private static partial void LogFailureDescriptor(
        ILogger logger,
        Guid id,
        string code,
        string category,
        string message);
}

using System.Collections.Concurrent;
using System.Diagnostics;
using JellyFederation.Server.Hubs;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.SignalR;

namespace JellyFederation.Server.Services;

/// <summary>
///     Centralises the fan-out notification logic for file request status changes
///     so it is not duplicated between controllers and the hub.
/// </summary>
public partial class FileRequestNotifier
{
    private readonly ConcurrentDictionary<Guid, IDisposable> _activeTransfers = new();
    private readonly IHubContext<FederationHub> _hub;
    private readonly ILogger<FileRequestNotifier> _logger;
    private readonly SignalRErrorMapper _signalrErrorMapper;
    private readonly ServerConnectionTracker _tracker;

    /// <summary>
    ///     Centralises the fan-out notification logic for file request status changes
    ///     so it is not duplicated between controllers and the hub.
    /// </summary>
    public FileRequestNotifier(IHubContext<FederationHub> hub,
        ServerConnectionTracker tracker,
        SignalRErrorMapper signalrErrorMapper,
        ILogger<FileRequestNotifier> logger)
    {
        _hub = hub;
        _tracker = tracker;
        _signalrErrorMapper = signalrErrorMapper;
        _logger = logger;
    }

    /// <summary>
    ///     Sends a FileRequestStatusUpdate to both plugin connections and browser groups.
    /// </summary>
    public async Task NotifyStatusAsync(FileRequest request)
    {
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Producer);
        FederationTelemetry.SetCommonTags(
            activity,
            "file_request.notify_status",
            "server",
            request.Id.ToString("N"),
            request.OwningServerId.ToString());

        LogNotifyStatus(_logger, request.Id, request.Status, request.OwningServerId, request.RequestingServerId);
        var update = new FileRequestStatusUpdate(
            request.Id,
            request.Status.ToString(),
            request.FailureReason,
            SignalRErrorMapper.ToContract(ToFailureDescriptor(request)),
            request.SelectedTransportMode,
            request.FailureCategory,
            request.BytesTransferred,
            request.TotalBytes);

        var senderConn = _tracker.GetConnectionId(request.OwningServerId);
        var receiverConn = _tracker.GetConnectionId(request.RequestingServerId);

        if (senderConn is not null)
        {
            LogNotifyPluginConnection(_logger, request.Id, request.OwningServerId, senderConn);
            await _hub.Clients.Client(senderConn).SendAsync("FileRequestStatusUpdate", update).ConfigureAwait(false);
        }
        else
        {
            LogNotifyPluginConnectionMissing(_logger, request.Id, request.OwningServerId);
        }

        if (receiverConn is not null)
        {
            LogNotifyPluginConnection(_logger, request.Id, request.RequestingServerId, receiverConn);
            await _hub.Clients.Client(receiverConn).SendAsync("FileRequestStatusUpdate", update).ConfigureAwait(false);
        }
        else
        {
            LogNotifyPluginConnectionMissing(_logger, request.Id, request.RequestingServerId);
        }

        await _hub.Clients.Group($"server:{request.OwningServerId}").SendAsync("FileRequestStatusUpdate", update)
            .ConfigureAwait(false);
        await _hub.Clients.Group($"server:{request.RequestingServerId}").SendAsync("FileRequestStatusUpdate", update)
            .ConfigureAwait(false);
        LogNotifyBrowserGroups(_logger, request.Id, request.OwningServerId, request.RequestingServerId);

        if (request.Status == FileRequestStatus.Transferring)
        {
            var scope = FederationMetrics.BeginInflight("file_request.transfer", "server");
            if (!_activeTransfers.TryAdd(request.Id, scope))
                scope.Dispose();
        }
        else if (request.Status is FileRequestStatus.Completed or FileRequestStatus.Cancelled
                 or FileRequestStatus.Failed)
        {
            if (_activeTransfers.TryRemove(request.Id, out var scope))
                scope.Dispose();
        }
    }

    /// <summary>
    ///     Sends CancelTransfer to plugin connections and FileRequestStatusUpdate to browser groups.
    /// </summary>
    public async Task SendCancelAsync(FileRequest request)
    {
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            ActivityKind.Producer);
        FederationTelemetry.SetCommonTags(
            activity,
            "file_request.cancel_notify",
            "server",
            request.Id.ToString("N"),
            request.OwningServerId.ToString());

        LogSendCancel(_logger, request.Id, request.OwningServerId, request.RequestingServerId);
        var cancelMsg = new CancelTransfer(request.Id);
        var statusUpdate = new FileRequestStatusUpdate(
            request.Id,
            "Cancelled",
            null,
            SignalRErrorMapper.ToContract(FailureDescriptor.Cancelled(
                "request.cancelled",
                "Request was cancelled.")),
            request.SelectedTransportMode,
            TransferFailureCategory.Cancelled,
            request.BytesTransferred,
            request.TotalBytes);

        var ownerConn = _tracker.GetConnectionId(request.OwningServerId);
        if (ownerConn is not null)
        {
            LogCancelPluginConnection(_logger, request.Id, request.OwningServerId, ownerConn);
            await _hub.Clients.Client(ownerConn).SendAsync("CancelTransfer", cancelMsg).ConfigureAwait(false);
        }
        else
        {
            LogCancelPluginConnectionMissing(_logger, request.Id, request.OwningServerId);
        }

        var requesterConn = _tracker.GetConnectionId(request.RequestingServerId);
        if (requesterConn is not null)
        {
            LogCancelPluginConnection(_logger, request.Id, request.RequestingServerId, requesterConn);
            await _hub.Clients.Client(requesterConn).SendAsync("CancelTransfer", cancelMsg).ConfigureAwait(false);
        }
        else
        {
            LogCancelPluginConnectionMissing(_logger, request.Id, request.RequestingServerId);
        }

        await _hub.Clients.Group($"server:{request.OwningServerId}").SendAsync("FileRequestStatusUpdate", statusUpdate)
            .ConfigureAwait(false);
        await _hub.Clients.Group($"server:{request.RequestingServerId}")
            .SendAsync("FileRequestStatusUpdate", statusUpdate).ConfigureAwait(false);
        LogCancelBrowserGroups(_logger, request.Id, request.OwningServerId, request.RequestingServerId);
    }

    private static FailureDescriptor? ToFailureDescriptor(FileRequest request)
    {
        if (request.Status is not FileRequestStatus.Failed and not FileRequestStatus.Cancelled)
            return null;

        var category = request.FailureCategory switch
        {
            TransferFailureCategory.Timeout => FailureCategory.Timeout,
            TransferFailureCategory.Connectivity => FailureCategory.Connectivity,
            TransferFailureCategory.Reliability => FailureCategory.Reliability,
            TransferFailureCategory.Cancelled => FailureCategory.Cancelled,
            _ => FailureCategory.Unexpected
        };

        return new FailureDescriptor(
            $"request.{request.Status.ToString().ToLowerInvariant()}",
            category,
            request.FailureReason ?? "Request failed.");
    }
}

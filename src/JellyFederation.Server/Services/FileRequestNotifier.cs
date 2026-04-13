using JellyFederation.Server.Hubs;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Server.Services;

/// <summary>
/// Centralises the fan-out notification logic for file request status changes
/// so it is not duplicated between controllers and the hub.
/// </summary>
public partial class FileRequestNotifier(
    IHubContext<FederationHub> hub,
    ServerConnectionTracker tracker,
    ILogger<FileRequestNotifier> logger)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, IDisposable> _activeTransfers = new();

    /// <summary>
    /// Sends a FileRequestStatusUpdate to both plugin connections and browser groups.
    /// </summary>
    public async Task NotifyStatusAsync(FileRequest request)
    {
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            System.Diagnostics.ActivityKind.Producer);
        FederationTelemetry.SetCommonTags(
            activity,
            "file_request.notify_status",
            "server",
            request.Id.ToString("N"),
            request.OwningServerId.ToString());

        LogNotifyStatus(logger, request.Id, request.Status, request.OwningServerId, request.RequestingServerId);
        var update = new FileRequestStatusUpdate(
            request.Id,
            request.Status.ToString(),
            request.FailureReason,
            request.SelectedTransportMode,
            request.FailureCategory,
            request.BytesTransferred,
            request.TotalBytes);

        var senderConn = tracker.GetConnectionId(request.OwningServerId);
        var receiverConn = tracker.GetConnectionId(request.RequestingServerId);

        if (senderConn is not null)
        {
            LogNotifyPluginConnection(logger, request.Id, request.OwningServerId, senderConn);
            await hub.Clients.Client(senderConn).SendAsync("FileRequestStatusUpdate", update);
        }
        else
        {
            LogNotifyPluginConnectionMissing(logger, request.Id, request.OwningServerId);
        }
        if (receiverConn is not null)
        {
            LogNotifyPluginConnection(logger, request.Id, request.RequestingServerId, receiverConn);
            await hub.Clients.Client(receiverConn).SendAsync("FileRequestStatusUpdate", update);
        }
        else
        {
            LogNotifyPluginConnectionMissing(logger, request.Id, request.RequestingServerId);
        }

        await hub.Clients.Group($"server:{request.OwningServerId}").SendAsync("FileRequestStatusUpdate", update);
        await hub.Clients.Group($"server:{request.RequestingServerId}").SendAsync("FileRequestStatusUpdate", update);
        LogNotifyBrowserGroups(logger, request.Id, request.OwningServerId, request.RequestingServerId);

        if (request.Status == FileRequestStatus.Transferring)
        {
            var scope = FederationMetrics.BeginInflight("file_request.transfer", "server");
            if (!_activeTransfers.TryAdd(request.Id, scope))
                scope.Dispose();
        }
        else if (request.Status is FileRequestStatus.Completed or FileRequestStatus.Cancelled or FileRequestStatus.Failed)
        {
            if (_activeTransfers.TryRemove(request.Id, out var scope))
                scope.Dispose();
        }
    }

    /// <summary>
    /// Sends CancelTransfer to plugin connections and FileRequestStatusUpdate to browser groups.
    /// </summary>
    public async Task SendCancelAsync(FileRequest request)
    {
        using var activity = FederationTelemetry.ServerActivitySource.StartActivity(
            FederationTelemetry.SpanSignalRWorkflow,
            System.Diagnostics.ActivityKind.Producer);
        FederationTelemetry.SetCommonTags(
            activity,
            "file_request.cancel_notify",
            "server",
            request.Id.ToString("N"),
            request.OwningServerId.ToString());

        LogSendCancel(logger, request.Id, request.OwningServerId, request.RequestingServerId);
        var cancelMsg = new CancelTransfer(request.Id);
        var statusUpdate = new FileRequestStatusUpdate(
            request.Id,
            "Cancelled",
            null,
            request.SelectedTransportMode,
            TransferFailureCategory.Cancelled,
            request.BytesTransferred,
            request.TotalBytes);

        var ownerConn = tracker.GetConnectionId(request.OwningServerId);
        if (ownerConn is not null)
        {
            LogCancelPluginConnection(logger, request.Id, request.OwningServerId, ownerConn);
            await hub.Clients.Client(ownerConn).SendAsync("CancelTransfer", cancelMsg);
        }
        else
        {
            LogCancelPluginConnectionMissing(logger, request.Id, request.OwningServerId);
        }

        var requesterConn = tracker.GetConnectionId(request.RequestingServerId);
        if (requesterConn is not null)
        {
            LogCancelPluginConnection(logger, request.Id, request.RequestingServerId, requesterConn);
            await hub.Clients.Client(requesterConn).SendAsync("CancelTransfer", cancelMsg);
        }
        else
        {
            LogCancelPluginConnectionMissing(logger, request.Id, request.RequestingServerId);
        }

        await hub.Clients.Group($"server:{request.OwningServerId}").SendAsync("FileRequestStatusUpdate", statusUpdate);
        await hub.Clients.Group($"server:{request.RequestingServerId}").SendAsync("FileRequestStatusUpdate", statusUpdate);
        LogCancelBrowserGroups(logger, request.Id, request.OwningServerId, request.RequestingServerId);
    }
}

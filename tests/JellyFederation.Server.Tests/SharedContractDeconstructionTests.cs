using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class SharedContractDeconstructionTests
{
    [Fact]
    public void FileRequestDto_Deconstruct_ReturnsAllConstructorValues()
    {
        var id = Guid.NewGuid();
        var requestingServerId = Guid.NewGuid();
        var owningServerId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var failure = new ErrorContract("code", FailureCategory.Connectivity.ToString(), "message");
        var dto = new FileRequestDto(
            id,
            requestingServerId,
            "requesting",
            owningServerId,
            "owning",
            "item-1",
            "Movie",
            FileRequestStatus.Transferring,
            TransferTransportMode.WebRtc,
            TransferFailureCategory.Connectivity,
            42,
            100,
            "reason",
            failure,
            createdAt);

        var (actualId, actualRequestingServerId, actualRequestingServerName,
            actualOwningServerId, actualOwningServerName, actualJellyfinItemId, actualItemTitle,
            actualStatus, actualSelectedTransportMode, actualFailureCategory, actualBytesTransferred,
            actualTotalBytes, actualFailureReason, actualFailure, actualCreatedAt) = dto;

        Assert.Equal(id, actualId);
        Assert.Equal(requestingServerId, actualRequestingServerId);
        Assert.Equal("requesting", actualRequestingServerName);
        Assert.Equal(owningServerId, actualOwningServerId);
        Assert.Equal("owning", actualOwningServerName);
        Assert.Equal("item-1", actualJellyfinItemId);
        Assert.Equal("Movie", actualItemTitle);
        Assert.Equal(FileRequestStatus.Transferring, actualStatus);
        Assert.Equal(TransferTransportMode.WebRtc, actualSelectedTransportMode);
        Assert.Equal(TransferFailureCategory.Connectivity, actualFailureCategory);
        Assert.Equal(42, actualBytesTransferred);
        Assert.Equal(100, actualTotalBytes);
        Assert.Equal("reason", actualFailureReason);
        Assert.Equal(failure, actualFailure);
        Assert.Equal(createdAt, actualCreatedAt);
    }

    [Fact]
    public void DtoDeconstructors_ReturnConstructorValues()
    {
        var createdAt = DateTime.UtcNow;
        var serverId = Guid.NewGuid();
        var invitation = new InvitationDto(Guid.NewGuid(), Guid.NewGuid(), "from", Guid.NewGuid(), "to", InvitationStatus.Pending, createdAt);
        var media = new MediaItemDto(Guid.NewGuid(), serverId, "server", "jf-1", "title", MediaType.Episode, 2026, "overview", "image", 123, true);
        var send = new SendInvitationRequest(serverId);
        var respond = new RespondToInvitationRequest(true);
        var server = new ServerInfoDto(serverId, "server", "owner", true, createdAt, 7);
        var register = new RegisterServerResponse(serverId, "key");

        var (_, _, fromName, _, toName, invitationStatus, invitationCreatedAt) = invitation;
        var (_, actualServerId, actualServerName, actualItemId, actualTitle, actualType, actualYear, actualOverview, actualImageUrl, actualSize, actualRequestable) = media;
        send.Deconstruct(out var sendToServerId);
        respond.Deconstruct(out var accepted);
        var (registeredServerId, registeredName, ownerUserId, isOnline, registeredAt, mediaItemCount) = server;
        var (responseServerId, apiKey) = register;

        Assert.Equal("from", fromName);
        Assert.Equal("to", toName);
        Assert.Equal(InvitationStatus.Pending, invitationStatus);
        Assert.Equal(createdAt, invitationCreatedAt);
        Assert.Equal(serverId, actualServerId);
        Assert.Equal("server", actualServerName);
        Assert.Equal("jf-1", actualItemId);
        Assert.Equal("title", actualTitle);
        Assert.Equal(MediaType.Episode, actualType);
        Assert.Equal(2026, actualYear);
        Assert.Equal("overview", actualOverview);
        Assert.Equal("image", actualImageUrl);
        Assert.Equal(123, actualSize);
        Assert.True(actualRequestable);
        Assert.Equal(serverId, sendToServerId);
        Assert.True(accepted);
        Assert.Equal(serverId, registeredServerId);
        Assert.Equal("server", registeredName);
        Assert.Equal("owner", ownerUserId);
        Assert.True(isOnline);
        Assert.Equal(createdAt, registeredAt);
        Assert.Equal(7, mediaItemCount);
        Assert.Equal(serverId, responseServerId);
        Assert.Equal("key", apiKey);
    }

    [Fact]
    public void SignalRMessageDeconstructors_ReturnConstructorValues()
    {
        var requestId = Guid.NewGuid();
        var failure = new FailureDescriptor("code", FailureCategory.Connectivity, "message");
        var error = new ErrorContract("code", FailureCategory.Connectivity.ToString(), "message");

        var holePunchRequest = new HolePunchRequest(requestId, "127.0.0.1:1234", 5555, HolePunchRole.Sender, TransferTransportMode.Quic, TransferSelectionReason.LargeFileQuic);
        var ready = new HolePunchReady(requestId, 5555, "203.0.113.1", true, 2048, true);
        var result = new HolePunchResult(requestId, false, "failed", failure);
        var notification = new FileRequestNotification(requestId, "item-1", Guid.NewGuid(), false);
        var status = new FileRequestStatusUpdate(requestId, "Failed", "bad", error, TransferTransportMode.ArqUdp, TransferFailureCategory.Connectivity, 10, 20);
        var progress = new TransferProgress(requestId, 10, 20);
        var cancel = new CancelTransfer(requestId);

        var (hpId, endpoint, localPort, role, mode, reason) = holePunchRequest;
        var (readyId, udpPort, overrideIp, supportsQuic, threshold, supportsIce) = ready;
        var (resultId, success, message, actualFailure) = result;
        var (notificationId, jellyfinItemId, requestingServerId, isSender) = notification;
        var (statusId, actualStatus, failureReason, actualError, statusMode, failureCategory, bytesTransferred, totalBytes) = status;
        var (progressId, bytesReceived, progressTotalBytes) = progress;
        cancel.Deconstruct(out var cancelId);

        Assert.Equal(requestId, hpId);
        Assert.Equal("127.0.0.1:1234", endpoint);
        Assert.Equal(5555, localPort);
        Assert.Equal(HolePunchRole.Sender, role);
        Assert.Equal(TransferTransportMode.Quic, mode);
        Assert.Equal(TransferSelectionReason.LargeFileQuic, reason);
        Assert.Equal(requestId, readyId);
        Assert.Equal(5555, udpPort);
        Assert.Equal("203.0.113.1", overrideIp);
        Assert.True(supportsQuic);
        Assert.Equal(2048, threshold);
        Assert.True(supportsIce);
        Assert.Equal(requestId, resultId);
        Assert.False(success);
        Assert.Equal("failed", message);
        Assert.Equal(failure, actualFailure);
        Assert.Equal(requestId, notificationId);
        Assert.Equal("item-1", jellyfinItemId);
        Assert.NotEqual(Guid.Empty, requestingServerId);
        Assert.False(isSender);
        Assert.Equal(requestId, statusId);
        Assert.Equal("Failed", actualStatus);
        Assert.Equal("bad", failureReason);
        Assert.Equal(error, actualError);
        Assert.Equal(TransferTransportMode.ArqUdp, statusMode);
        Assert.Equal(TransferFailureCategory.Connectivity, failureCategory);
        Assert.Equal(10, bytesTransferred);
        Assert.Equal(20, totalBytes);
        Assert.Equal(requestId, progressId);
        Assert.Equal(10, bytesReceived);
        Assert.Equal(20, progressTotalBytes);
        Assert.Equal(requestId, cancelId);
    }
}

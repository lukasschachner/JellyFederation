using JellyFederation.Shared.Models;

namespace JellyFederation.Shared.Dtos;

public record SendInvitationRequest
{
    public SendInvitationRequest(Guid ToServerId)
    {
        this.ToServerId = ToServerId;
    }

    public Guid ToServerId { get; init; }

    public void Deconstruct(out Guid ToServerId)
    {
        ToServerId = this.ToServerId;
    }
}

public record InvitationDto
{
    public InvitationDto(Guid Id,
        Guid FromServerId,
        string FromServerName,
        Guid ToServerId,
        string ToServerName,
        InvitationStatus Status,
        DateTime CreatedAt)
    {
        this.Id = Id;
        this.FromServerId = FromServerId;
        this.FromServerName = FromServerName;
        this.ToServerId = ToServerId;
        this.ToServerName = ToServerName;
        this.Status = Status;
        this.CreatedAt = CreatedAt;
    }

    public Guid Id { get; init; }
    public Guid FromServerId { get; init; }
    public string FromServerName { get; init; }
    public Guid ToServerId { get; init; }
    public string ToServerName { get; init; }
    public InvitationStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }

    public void Deconstruct(out Guid Id, out Guid FromServerId, out string FromServerName, out Guid ToServerId,
        out string ToServerName, out InvitationStatus Status, out DateTime CreatedAt)
    {
        Id = this.Id;
        FromServerId = this.FromServerId;
        FromServerName = this.FromServerName;
        ToServerId = this.ToServerId;
        ToServerName = this.ToServerName;
        Status = this.Status;
        CreatedAt = this.CreatedAt;
    }
}

public record RespondToInvitationRequest
{
    public RespondToInvitationRequest(bool Accept)
    {
        this.Accept = Accept;
    }

    public bool Accept { get; init; }

    public void Deconstruct(out bool Accept)
    {
        Accept = this.Accept;
    }
}
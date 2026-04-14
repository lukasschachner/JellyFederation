namespace JellyFederation.Shared.Dtos;

public record RegisterServerRequest
{
    public RegisterServerRequest(string Name, string OwnerUserId)
    {
        this.Name = Name;
        this.OwnerUserId = OwnerUserId;
    }

    public string Name { get; init; }
    public string OwnerUserId { get; init; }

    public void Deconstruct(out string Name, out string OwnerUserId)
    {
        Name = this.Name;
        OwnerUserId = this.OwnerUserId;
    }
}

public record RegisterServerResponse
{
    public RegisterServerResponse(Guid ServerId, string ApiKey)
    {
        this.ServerId = ServerId;
        this.ApiKey = ApiKey;
    }

    public Guid ServerId { get; init; }
    public string ApiKey { get; init; }

    public void Deconstruct(out Guid ServerId, out string ApiKey)
    {
        ServerId = this.ServerId;
        ApiKey = this.ApiKey;
    }
}

public record ServerInfoDto
{
    public ServerInfoDto(Guid Id,
        string Name,
        string OwnerUserId,
        bool IsOnline,
        DateTime LastSeenAt,
        int MediaItemCount)
    {
        this.Id = Id;
        this.Name = Name;
        this.OwnerUserId = OwnerUserId;
        this.IsOnline = IsOnline;
        this.LastSeenAt = LastSeenAt;
        this.MediaItemCount = MediaItemCount;
    }

    public Guid Id { get; init; }
    public string Name { get; init; }
    public string OwnerUserId { get; init; }
    public bool IsOnline { get; init; }
    public DateTime LastSeenAt { get; init; }
    public int MediaItemCount { get; init; }

    public void Deconstruct(out Guid Id, out string Name, out string OwnerUserId, out bool IsOnline,
        out DateTime LastSeenAt, out int MediaItemCount)
    {
        Id = this.Id;
        Name = this.Name;
        OwnerUserId = this.OwnerUserId;
        IsOnline = this.IsOnline;
        LastSeenAt = this.LastSeenAt;
        MediaItemCount = this.MediaItemCount;
    }
}
using System.ComponentModel.DataAnnotations;

namespace JellyFederation.Shared.Dtos;

public sealed class CreateWebSessionRequest
{
    public CreateWebSessionRequest()
    {
    }

    public CreateWebSessionRequest(Guid ServerId, string ApiKey)
    {
        this.ServerId = ServerId;
        this.ApiKey = ApiKey;
    }

    [Required]
    public Guid ServerId { get; init; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string ApiKey { get; init; } = string.Empty;
}

public sealed record WebSessionResponse(Guid ServerId, string ServerName);

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JellyFederation.Shared.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace JellyFederation.Server.Tests;

internal sealed class TestServerFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"jellyfederation-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_databasePath}",
                ["AdminToken"] = string.Empty,
                ["Telemetry:EnableTracing"] = "false",
                ["Telemetry:EnableMetrics"] = "false",
                ["Telemetry:EnableLogs"] = "false",
                ["Telemetry:OtlpEndpoint"] = string.Empty,
                ["Urls"] = "http://127.0.0.1:0"
            };

            configBuilder.AddInMemoryCollection(settings);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}

internal sealed class TestApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public TestApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<RegisterServerResponse> RegisterAsync(string name, string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("/api/servers/register", new RegisterServerRequest(name, ownerUserId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterServerResponse>(JsonOptions, cancellationToken))!;
    }

    public async Task<InvitationDto> SendInvitationAsync(string apiKey, Guid toServerId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations")
        {
            Content = JsonContent.Create(new SendInvitationRequest(toServerId))
        };
        request.Headers.Add("X-Api-Key", apiKey);
        var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvitationDto>(JsonOptions, cancellationToken))!;
    }

    public async Task<HttpResponseMessage> RespondInvitationAsync(string apiKey, Guid invitationId, bool accept,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/invitations/{invitationId}/respond")
        {
            Content = JsonContent.Create(new RespondToInvitationRequest(accept))
        };
        request.Headers.Add("X-Api-Key", apiKey);
        return await _http.SendAsync(request, cancellationToken);
    }

    public async Task<FileRequestDto> CreateFileRequestAsync(string apiKey, Guid owningServerId, string itemId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/filerequests")
        {
            Content = JsonContent.Create(new CreateFileRequestDto(itemId, owningServerId))
        };
        request.Headers.Add("X-Api-Key", apiKey);
        var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FileRequestDto>(JsonOptions, cancellationToken))!;
    }
}

using System.Net;
using System.Net.Http.Json;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class SessionsControllerTests : IAsyncLifetime
{
    private readonly TestServerFactory _factory = new();
    private HttpClient _http = null!;

    public ValueTask InitializeAsync()
    {
        _http = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Create_InvalidPayloadEnvelope_ReturnsValidationError()
    {
        using var response = await _http.PostAsJsonAsync(
            "/api/sessions",
            new { serverId = Guid.Empty, apiKey = "" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("request.validation_failed", payload!.Error.Code);
    }

    [Fact]
    public async Task Create_InvalidCredentials_ReturnsAuthorizationError()
    {
        using var response = await _http.PostAsJsonAsync(
            "/api/sessions",
            new CreateWebSessionRequest(Guid.NewGuid(), "wrong-key"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("session.invalid_credentials", payload!.Error.Code);
        Assert.Equal(nameof(FailureCategory.Authorization), payload.Error.Category);
    }

    [Fact]
    public async Task Delete_IsIdempotent_AndClearsCookie()
    {
        using var firstDelete = await _http.DeleteAsync("/api/sessions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Contains(firstDelete.Headers, header => header.Key == "Set-Cookie");

        using var secondDelete = await _http.DeleteAsync("/api/sessions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);
        Assert.Contains(secondDelete.Headers, header => header.Key == "Set-Cookie");
    }

    [Fact]
    public async Task Create_AndDelete_SessionCookie_Succeeds()
    {
        var register = await _http.PostAsJsonAsync(
            "/api/servers/register",
            new RegisterServerRequest("session-server", "owner-session"),
            TestContext.Current.CancellationToken);
        register.EnsureSuccessStatusCode();
        var server = (await register.Content.ReadFromJsonAsync<RegisterServerResponse>(TestContext.Current.CancellationToken))!;

        using var createResponse = await _http.PostAsJsonAsync(
            "/api/sessions",
            new CreateWebSessionRequest(server.ServerId, server.ApiKey),
            TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();
        Assert.Contains("Set-Cookie", createResponse.Headers.Select(h => h.Key));

        using var deleteResponse = await _http.DeleteAsync("/api/sessions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Contains(deleteResponse.Headers, header => header.Key == "Set-Cookie");
    }
}

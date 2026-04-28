using System.Net;
using System.Net.Http.Json;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class FederationApiKeyAuthenticationHandlerTests : IAsyncLifetime
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
    public async Task ProtectedEndpoint_WithoutCredentials_ReturnsForbiddenEnvelope()
    {
        var response = await _http.GetAsync("/api/servers", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal("api.auth.invalid_credentials", error!.Error.Code);
        Assert.Equal(nameof(FailureCategory.Authorization), error.Error.Category);
        Assert.Equal("Missing or invalid credentials.", error.Error.Message);
        Assert.False(string.IsNullOrWhiteSpace(error.Error.CorrelationId));
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("Bearer   ")]
    [InlineData("Bearer malformed")]
    public async Task ProtectedEndpoint_WithMalformedOrUnknownBearer_ReturnsForbidden(string authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/servers");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignalRHubNegotiation_AllowsAccessTokenQuery()
    {
        var register = await _http.PostAsJsonAsync(
            "/api/servers/register",
            new RegisterServerRequest("auth-hub-access-token", "owner-auth"),
            TestContext.Current.CancellationToken);
        register.EnsureSuccessStatusCode();
        var server = (await register.Content.ReadFromJsonAsync<RegisterServerResponse>(TestContext.Current.CancellationToken))!;

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/hubs/federation/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(server.ApiKey)}");
        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignalRHubLegacyApiKeyQuery_IsRejectedByDefault_ButAllowedWhenEnabled()
    {
        var register = await _http.PostAsJsonAsync(
            "/api/servers/register",
            new RegisterServerRequest("auth-hub-legacy", "owner-auth"),
            TestContext.Current.CancellationToken);
        register.EnsureSuccessStatusCode();
        var server = (await register.Content.ReadFromJsonAsync<RegisterServerResponse>(TestContext.Current.CancellationToken))!;

        using (var request = new HttpRequestMessage(HttpMethod.Post,
                   $"/hubs/federation/negotiate?negotiateVersion=1&apiKey={Uri.EscapeDataString(server.ApiKey)}"))
        using (var response = await _http.SendAsync(request, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        await using var legacyFactory = new LegacySignalRQueryFactory();
        using var legacyHttp = legacyFactory.CreateClient();
        var legacyRegister = await legacyHttp.PostAsJsonAsync(
            "/api/servers/register",
            new RegisterServerRequest("auth-hub-legacy-enabled", "owner-auth"),
            TestContext.Current.CancellationToken);
        legacyRegister.EnsureSuccessStatusCode();
        var legacyServer = (await legacyRegister.Content.ReadFromJsonAsync<RegisterServerResponse>(TestContext.Current.CancellationToken))!;

        using var legacyRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/hubs/federation/negotiate?negotiateVersion=1&apiKey={Uri.EscapeDataString(legacyServer.ApiKey)}");
        using var legacyResponse = await legacyHttp.SendAsync(legacyRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidApiKey_Succeeds()
    {
        var register = await _http.PostAsJsonAsync(
            "/api/servers/register",
            new RegisterServerRequest("auth-server", "owner-auth"),
            TestContext.Current.CancellationToken);
        register.EnsureSuccessStatusCode();
        var server = (await register.Content.ReadFromJsonAsync<RegisterServerResponse>(TestContext.Current.CancellationToken))!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/servers");
        request.Headers.Add("X-Api-Key", server.ApiKey);

        var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class LegacySignalRQueryFactory : WebApplicationFactory<Program>
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
                    ["Security:AllowLegacySignalRApiKeyQuery"] = "true",
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
}

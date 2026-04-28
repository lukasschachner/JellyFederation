using System.Net;
using System.Net.Http.Json;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class ServersControllerTests : IAsyncLifetime
{
    private readonly AdminTokenFactory _factory = new();
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
    public async Task Register_RequiresAdminToken_WhenConfigured()
    {
        using var missingToken = new HttpRequestMessage(HttpMethod.Post, "/api/servers/register")
        {
            Content = JsonContent.Create(new RegisterServerRequest("server-a", "owner-a"))
        };

        using var missingTokenResponse = await _http.SendAsync(missingToken, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, missingTokenResponse.StatusCode);
        var missingError = await missingTokenResponse.Content.ReadFromJsonAsync<ErrorEnvelope>(TestContext.Current.CancellationToken);
        Assert.NotNull(missingError);
        Assert.Equal("server.register.invalid_admin_token", missingError!.Error.Code);

        using var validToken = new HttpRequestMessage(HttpMethod.Post, "/api/servers/register")
        {
            Content = JsonContent.Create(new RegisterServerRequest("server-b", "owner-b"))
        };
        validToken.Headers.Add("X-Admin-Token", AdminTokenFactory.ExpectedAdminToken);

        using var validTokenResponse = await _http.SendAsync(validToken, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, validTokenResponse.StatusCode);
    }

    [Fact]
    public async Task GetUnknownServer_ReturnsNotFoundEnvelope()
    {
        var response = await _http.GetAsync($"/api/servers/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("server.not_found", payload!.Error.Code);
    }

    [Fact]
    public async Task List_InvalidPagination_ReturnsValidationEnvelope()
    {
        var registered = await RegisterAsync("listed-server-invalid", "owner-list-invalid");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/servers?page=0&pageSize=1000");
        request.Headers.Add("X-Api-Key", registered.ApiKey);

        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("server.pagination.invalid", payload!.Error.Code);
    }

    [Fact]
    public async Task List_WithApiKeyAuthentication_ReturnsPagedServers()
    {
        var registered = await RegisterAsync("listed-server", "owner-list");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/servers?page=1&pageSize=10");
        request.Headers.Add("X-Api-Key", registered.ApiKey);

        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<List<ServerInfoDto>>(TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        Assert.NotEmpty(payload!);
        Assert.Contains(payload, s => s.Id == registered.ServerId);
        Assert.True(response.Headers.Contains("X-Total-Count"));
    }

    private async Task<RegisterServerResponse> RegisterAsync(string name, string owner)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/servers/register")
        {
            Content = JsonContent.Create(new RegisterServerRequest(name, owner))
        };
        request.Headers.Add("X-Admin-Token", AdminTokenFactory.ExpectedAdminToken);

        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterServerResponse>(TestContext.Current.CancellationToken))!;
    }

    private sealed class AdminTokenFactory : WebApplicationFactory<Program>
    {
        public const string ExpectedAdminToken = "test-admin-token";
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
                    ["AdminToken"] = ExpectedAdminToken,
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

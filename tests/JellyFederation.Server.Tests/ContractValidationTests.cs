using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class ContractValidationTests : IAsyncLifetime
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

    [Theory]
    [MemberData(nameof(InvalidRegisterServerRequests))]
    public static void RegisterServerRequest_Validation_RequiresNameAndOwnerUserIdWithinStringLength(
        RegisterServerRequest request,
        string expectedMemberName)
    {
        var failures = Validate(request);

        Assert.Contains(failures, result => result.MemberNames.Contains(expectedMemberName));
    }

    [Fact]
    public static void CreateFileRequestDto_Validation_RejectsEmptyJellyfinItemId()
    {
        var request = new CreateFileRequestDto(string.Empty, Guid.NewGuid());

        var failures = Validate(request);

        Assert.Contains(failures, result => result.MemberNames.Contains(nameof(CreateFileRequestDto.JellyfinItemId)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public static void SyncMediaRequest_Validation_RejectsEmptyOrExcessiveItems(int itemCount)
    {
        var request = new SyncMediaRequest(CreateSyncEntries(itemCount));

        var failures = Validate(request);

        Assert.Contains(failures, result => result.MemberNames.Contains(nameof(SyncMediaRequest.Items)));
    }

    [Fact]
    public async Task Register_InvalidPayload_ReturnsStableErrorEnvelopeShape()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/servers/register")
        {
            Content = JsonContent.Create(new { name = string.Empty, ownerUserId = "owner-a" })
        };
        request.Headers.Add("X-Correlation-ID", "contract-validation-register");

        var response = await _http.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        AssertErrorEnvelopeShape(document, "request.validation_failed", nameof(FailureCategory.Validation),
            "contract-validation-register", "Name");
    }

    [Fact]
    public async Task CreateFileRequest_EmptyJellyfinItemId_ReturnsValidationEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var registered = await RegisterAsync("requester", "owner-a", cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/filerequests")
        {
            Content = JsonContent.Create(new CreateFileRequestDto(string.Empty, Guid.NewGuid()))
        };
        request.Headers.Add("X-Api-Key", registered.ApiKey);

        var response = await _http.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("request.validation_failed", payload!.Error.Code);
        Assert.Equal(nameof(FailureCategory.Validation), payload.Error.Category);
        Assert.NotNull(payload.Error.Details);
        Assert.Contains(nameof(CreateFileRequestDto.JellyfinItemId), payload.Error.Details!.Keys);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData(null)]
    public async Task SyncMedia_EmptyOrExcessiveItems_ReturnsValidationEnvelope(string? itemsJson)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var registered = await RegisterAsync($"sync-server-{Guid.NewGuid():N}", "owner-a", cancellationToken);
        var body = $$"""
            {"items":{{itemsJson ?? BuildItemsJson(10_001)}},"replaceAll":true}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/library/sync")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Api-Key", registered.ApiKey);

        var response = await _http.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("request.validation_failed", payload!.Error.Code);
        Assert.Equal(nameof(FailureCategory.Validation), payload.Error.Category);
        Assert.NotNull(payload.Error.Details);
        Assert.Contains(nameof(SyncMediaRequest.Items), payload.Error.Details!.Keys);
    }

    [Fact]
    public async Task MediaDtos_UseStringEnumValuesOverHttpJson()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var registered = await RegisterAsync("enum-server", "owner-a", cancellationToken);
        using var syncRequest = new HttpRequestMessage(HttpMethod.Post, "/api/library/sync")
        {
            Content = new StringContent("""
                {
                  "items": [
                    {
                      "jellyfinItemId": "movie-1",
                      "title": "A Movie",
                      "type": "Movie",
                      "year": 2024,
                      "overview": null,
                      "imageUrl": null,
                      "fileSizeBytes": 123
                    }
                  ],
                  "replaceAll": true
                }
                """, Encoding.UTF8, "application/json")
        };
        syncRequest.Headers.Add("X-Api-Key", registered.ApiKey);
        var syncResponse = await _http.SendAsync(syncRequest, cancellationToken);
        syncResponse.EnsureSuccessStatusCode();

        using var mineRequest = new HttpRequestMessage(HttpMethod.Get, "/api/library/mine");
        mineRequest.Headers.Add("X-Api-Key", registered.ApiKey);
        var mineResponse = await _http.SendAsync(mineRequest, cancellationToken);
        mineResponse.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await mineResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(JsonValueKind.String, item.GetProperty("type").ValueKind);
        Assert.Equal(nameof(MediaType.Movie), item.GetProperty("type").GetString());
    }

    public static TheoryData<RegisterServerRequest, string> InvalidRegisterServerRequests() => new()
    {
        { new RegisterServerRequest(string.Empty, "owner-a"), nameof(RegisterServerRequest.Name) },
        { new RegisterServerRequest(new string('n', 129), "owner-a"), nameof(RegisterServerRequest.Name) },
        { new RegisterServerRequest("server-a", string.Empty), nameof(RegisterServerRequest.OwnerUserId) },
        { new RegisterServerRequest("server-a", new string('o', 129)), nameof(RegisterServerRequest.OwnerUserId) }
    };

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }

    private static List<MediaItemSyncEntry> CreateSyncEntries(int count) => Enumerable.Range(0, count)
        .Select(i => new MediaItemSyncEntry($"item-{i}", $"Title {i}", MediaType.Movie, null, null, null, 100))
        .ToList();

    private static string BuildItemsJson(int count)
    {
        var builder = new StringBuilder(capacity: count * 120);
        builder.Append('[');
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(',');

            builder.Append("{\"jellyfinItemId\":\"item-")
                .Append(i)
                .Append("\",\"title\":\"Title ")
                .Append(i)
                .Append("\",\"type\":\"Movie\",\"fileSizeBytes\":100}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private async Task<RegisterServerResponse> RegisterAsync(string name, string ownerUserId,
        CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync("/api/servers/register", new RegisterServerRequest(name, ownerUserId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterServerResponse>(cancellationToken))!;
    }

    private static void AssertErrorEnvelopeShape(JsonDocument document,
        string expectedCode,
        string expectedCategory,
        string expectedCorrelationId,
        string expectedDetailsKey)
    {
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        var error = root.GetProperty("error");
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.Equal(expectedCategory, error.GetProperty("category").GetString());
        Assert.Equal("One or more validation errors occurred.", error.GetProperty("message").GetString());
        Assert.Equal(expectedCorrelationId, error.GetProperty("correlationId").GetString());
        Assert.True(error.TryGetProperty("details", out var details));
        Assert.Equal(JsonValueKind.Object, details.ValueKind);
        Assert.True(details.TryGetProperty(expectedDetailsKey, out var detail));
        Assert.Equal(JsonValueKind.String, detail.ValueKind);
    }
}

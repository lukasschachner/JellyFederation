using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JellyFederation.Server.Controllers;
using JellyFederation.Shared.Dtos;
using JellyFederation.Shared.Models;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class ApiIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TestServerFactory _factory = new();
    private HttpClient _http = null!;
    private TestApiClient _api = null!;

    public ValueTask InitializeAsync()
    {
        _http = _factory.CreateClient();
        _api = new TestApiClient(_http);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("", "owner-a")]
    [InlineData("server-a", "")]
    public async Task Register_InvalidPayload_ReturnsValidationEnvelope(string name, string ownerUserId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _http.PostAsJsonAsync("/api/servers/register",
            new RegisterServerRequest(name, ownerUserId), JsonOptions, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await ReadErrorAsync(response, cancellationToken);
        Assert.Equal("request.validation_failed", payload.Error.Code);
        Assert.Equal(nameof(FailureCategory.Validation), payload.Error.Category);
    }

    [Fact]
    public async Task InvitationLifecycle_SendAcceptRejectCancelAndDuplicatePrevention()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serverA = await _api.RegisterAsync("server-a", "owner-a", cancellationToken);
        var serverB = await _api.RegisterAsync("server-b", "owner-b", cancellationToken);
        var serverC = await _api.RegisterAsync("server-c", "owner-c", cancellationToken);
        var serverD = await _api.RegisterAsync("server-d", "owner-d", cancellationToken);

        var acceptedInvitation = await _api.SendInvitationAsync(serverA.ApiKey, serverB.ServerId, cancellationToken);
        Assert.Equal(serverA.ServerId, acceptedInvitation.FromServerId);
        Assert.Equal(serverB.ServerId, acceptedInvitation.ToServerId);
        Assert.Equal(InvitationStatus.Pending, acceptedInvitation.Status);

        var acceptResponse = await _api.RespondInvitationAsync(serverB.ApiKey, acceptedInvitation.Id, true, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        var accepted = await acceptResponse.Content.ReadFromJsonAsync<InvitationDto>(JsonOptions, cancellationToken);
        Assert.NotNull(accepted);
        Assert.Equal(InvitationStatus.Accepted, accepted!.Status);

        var duplicate = await SendInvitationRawAsync(serverB.ApiKey, serverA.ServerId, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var duplicateError = await ReadErrorAsync(duplicate, cancellationToken);
        Assert.Equal("invitation.relationship_exists", duplicateError.Error.Code);

        var rejectedInvitation = await _api.SendInvitationAsync(serverA.ApiKey, serverC.ServerId, cancellationToken);
        var rejectResponse = await _api.RespondInvitationAsync(serverC.ApiKey, rejectedInvitation.Id, false, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejected = await rejectResponse.Content.ReadFromJsonAsync<InvitationDto>(JsonOptions, cancellationToken);
        Assert.NotNull(rejected);
        Assert.Equal(InvitationStatus.Declined, rejected!.Status);

        var revokedInvitation = await _api.SendInvitationAsync(serverA.ApiKey, serverD.ServerId, cancellationToken);
        var revokeResponse = await RevokeInvitationAsync(serverA.ApiKey, revokedInvitation.Id, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var respondAfterRevoke = await _api.RespondInvitationAsync(serverD.ApiKey, revokedInvitation.Id, true, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, respondAfterRevoke.StatusCode);
        var revokedError = await ReadErrorAsync(respondAfterRevoke, cancellationToken);
        Assert.Equal("invitation.not_pending", revokedError.Error.Code);
    }

    [Fact]
    public async Task FileRequestAuthorization_RequiresAcceptedInvitationAndRestrictsCancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requester = await _api.RegisterAsync("requester", "owner-a", cancellationToken);
        var owner = await _api.RegisterAsync("owner", "owner-b", cancellationToken);
        var unrelated = await _api.RegisterAsync("unrelated", "owner-c", cancellationToken);

        var withoutInvitation = await CreateFileRequestRawAsync(requester.ApiKey, owner.ServerId, "item-1", cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, withoutInvitation.StatusCode);
        var missingInvitationError = await ReadErrorAsync(withoutInvitation, cancellationToken);
        Assert.Equal("file_request.invitation_required", missingInvitationError.Error.Code);

        var invitation = await _api.SendInvitationAsync(requester.ApiKey, owner.ServerId, cancellationToken);
        var acceptResponse = await _api.RespondInvitationAsync(owner.ApiKey, invitation.Id, true, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        var fileRequest = await _api.CreateFileRequestAsync(requester.ApiKey, owner.ServerId, "item-1", cancellationToken);
        Assert.Equal(requester.ServerId, fileRequest.RequestingServerId);
        Assert.Equal(owner.ServerId, fileRequest.OwningServerId);
        Assert.Equal(FileRequestStatus.Pending, fileRequest.Status);

        var unrelatedCancel = await CancelFileRequestAsync(unrelated.ApiKey, fileRequest.Id, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedCancel.StatusCode);
        var unrelatedCancelError = await ReadErrorAsync(unrelatedCancel, cancellationToken);
        Assert.Equal("file_request.cancel_forbidden", unrelatedCancelError.Error.Code);

        var requesterCancel = await CancelFileRequestAsync(requester.ApiKey, fileRequest.Id, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, requesterCancel.StatusCode);

        var visibleToRequester = await ListFileRequestsAsync(requester.ApiKey, cancellationToken);
        Assert.Equal(fileRequest.Id, Assert.Single(visibleToRequester).Id);
    }

    [Fact]
    public async Task LibrarySync_ReplaceAllDifferentialFilteringPaginationAndRequestableBehavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var browser = await _api.RegisterAsync("browser", "owner-a", cancellationToken);
        var owner = await _api.RegisterAsync("owner", "owner-b", cancellationToken);

        var invitation = await _api.SendInvitationAsync(browser.ApiKey, owner.ServerId, cancellationToken);
        var acceptResponse = await _api.RespondInvitationAsync(owner.ApiKey, invitation.Id, true, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        await SyncLibraryAsync(owner.ApiKey, true,
        [
            Media("movie-alpha", "Alpha Movie", MediaType.Movie, 2001),
            Media("series-beta", "Beta Series", MediaType.Series, 2002),
            Media("movie-gamma", "Gamma Movie", MediaType.Movie, 2003)
        ], cancellationToken);

        var filtered = await BrowseLibraryAsync(browser.ApiKey, "alpha", "Movie", 1, 10, cancellationToken);
        var alpha = Assert.Single(filtered.Items);
        Assert.Equal("movie-alpha", alpha.JellyfinItemId);
        Assert.Equal("1", filtered.TotalCount);

        var ownerItems = await GetMineAsync(owner.ApiKey, cancellationToken: cancellationToken);
        var beta = Assert.Single(ownerItems.Items, item => item.JellyfinItemId == "series-beta");
        var setNotRequestable = await SetRequestableAsync(owner.ApiKey, beta.Id, false, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, setNotRequestable.StatusCode);

        var betaWhenHidden = await BrowseLibraryAsync(browser.ApiKey, "beta", null, 1, 10, cancellationToken);
        Assert.Empty(betaWhenHidden.Items);
        Assert.Equal("0", betaWhenHidden.TotalCount);

        await SyncLibraryAsync(owner.ApiKey, false,
        [
            Media("series-beta", "Beta Series Updated", MediaType.Series, 2012),
            Media("music-delta", "Delta Album", MediaType.Music, 2020)
        ], cancellationToken);

        var afterDifferential = await GetMineAsync(owner.ApiKey, cancellationToken: cancellationToken);
        Assert.Contains(afterDifferential.Items, item => item.JellyfinItemId == "movie-alpha");
        var updatedBeta = Assert.Single(afterDifferential.Items, item => item.JellyfinItemId == "series-beta");
        Assert.Equal("Beta Series Updated", updatedBeta.Title);
        Assert.False(updatedBeta.IsRequestable);
        Assert.Contains(afterDifferential.Items, item => item.JellyfinItemId == "music-delta");

        await SyncLibraryAsync(owner.ApiKey, true,
        [
            Media("series-beta", "Beta Series Final", MediaType.Series, 2013)
        ], cancellationToken);

        var afterReplaceAll = await GetMineAsync(owner.ApiKey, cancellationToken: cancellationToken);
        var remaining = Assert.Single(afterReplaceAll.Items);
        Assert.Equal("series-beta", remaining.JellyfinItemId);
        Assert.Equal("Beta Series Final", remaining.Title);
        Assert.False(remaining.IsRequestable);

        var invalidPagination = await GetMineRawAsync(owner.ApiKey, page: 0, pageSize: 501, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPagination.StatusCode);
        var paginationError = await ReadErrorAsync(invalidPagination, cancellationToken);
        Assert.Equal("library.pagination.invalid", paginationError.Error.Code);

        var setRequestable = await SetRequestableAsync(owner.ApiKey, remaining.Id, true, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, setRequestable.StatusCode);

        var visibleAgain = await BrowseLibraryAsync(browser.ApiKey, "final", "Series", 1, 10, cancellationToken);
        var finalBeta = Assert.Single(visibleAgain.Items);
        Assert.Equal("series-beta", finalBeta.JellyfinItemId);
        Assert.True(finalBeta.IsRequestable);
    }

    private async Task<HttpResponseMessage> SendInvitationRawAsync(string apiKey, Guid toServerId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations")
        {
            Content = JsonContent.Create(new SendInvitationRequest(toServerId), options: JsonOptions)
        };
        request.Headers.Add("X-Api-Key", apiKey);
        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> RevokeInvitationAsync(string apiKey, Guid invitationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/invitations/{invitationId}");
        request.Headers.Add("X-Api-Key", apiKey);
        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> CreateFileRequestRawAsync(string apiKey, Guid owningServerId,
        string jellyfinItemId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/filerequests")
        {
            Content = JsonContent.Create(new CreateFileRequestDto(jellyfinItemId, owningServerId), options: JsonOptions)
        };
        request.Headers.Add("X-Api-Key", apiKey);
        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> CancelFileRequestAsync(string apiKey, Guid fileRequestId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/filerequests/{fileRequestId}/cancel");
        request.Headers.Add("X-Api-Key", apiKey);
        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<IReadOnlyList<FileRequestDto>> ListFileRequestsAsync(string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/filerequests");
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<FileRequestDto>>(JsonOptions, cancellationToken))!;
    }

    private async Task SyncLibraryAsync(string apiKey, bool replaceAll, List<MediaItemSyncEntry> items,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/library/sync")
        {
            Content = JsonContent.Create(new SyncMediaRequest(items, replaceAll), options: JsonOptions)
        };
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<PagedItems> GetMineAsync(string apiKey, int page = 1, int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        using var response = await GetMineRawAsync(apiKey, page, pageSize, cancellationToken);
        response.EnsureSuccessStatusCode();
        var items = (await response.Content.ReadFromJsonAsync<List<MediaItemDto>>(JsonOptions, cancellationToken))!;
        return new PagedItems(items, response.Headers.TryGetValues("X-Total-Count", out var values)
            ? values.Single()
            : null);
    }

    private async Task<HttpResponseMessage> GetMineRawAsync(string apiKey, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/library/mine?page={page}&pageSize={pageSize}");
        request.Headers.Add("X-Api-Key", apiKey);
        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<PagedItems> BrowseLibraryAsync(string apiKey, string? search, string? type, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        var query = $"page={page}&pageSize={pageSize}";
        if (search is not null)
            query += $"&search={Uri.EscapeDataString(search)}";
        if (type is not null)
            query += $"&type={Uri.EscapeDataString(type)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/library?{query}");
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var items = (await response.Content.ReadFromJsonAsync<List<MediaItemDto>>(JsonOptions, cancellationToken))!;
        return new PagedItems(items, response.Headers.TryGetValues("X-Total-Count", out var values)
            ? values.Single()
            : null);
    }

    private async Task<HttpResponseMessage> SetRequestableAsync(string apiKey, Guid itemId, bool isRequestable,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/library/{itemId}/requestable")
        {
            Content = JsonContent.Create(new SetRequestableRequest(isRequestable), options: JsonOptions)
        };
        request.Headers.Add("X-Api-Key", apiKey);
        return await _http.SendAsync(request, cancellationToken);
    }

    private static MediaItemSyncEntry Media(string id, string title, MediaType type, int year) =>
        new(id, title, type, year, Overview: null, ImageUrl: null, FileSizeBytes: 1024);

    private static async Task<ErrorEnvelope> ReadErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions, cancellationToken);
        Assert.NotNull(payload);
        return payload!;
    }

    private sealed record PagedItems(IReadOnlyList<MediaItemDto> Items, string? TotalCount);
}

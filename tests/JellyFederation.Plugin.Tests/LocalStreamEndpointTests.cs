using System.IO.Pipelines;
using JellyFederation.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class LocalStreamEndpointTests
{
    [Fact]
    public async Task RegisterStreamAsync_ServesRegisteredPayload_AndCleansUpToken()
    {
        await using var endpoint = new LocalStreamEndpoint(NullLogger<LocalStreamEndpoint>.Instance);
        var pipe = new Pipe();
        var token = Guid.NewGuid();

        var streamUrl = await endpoint.RegisterStreamAsync(token, pipe.Reader, TestContext.Current.CancellationToken);
        Assert.NotNull(endpoint.GetStreamUrl(token));

        await pipe.Writer.WriteAsync(new byte[] { 1, 2, 3, 4 }, TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();

        using var http = new HttpClient();
        var payload = await http.GetByteArrayAsync(streamUrl, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, payload);

        await EventuallyAsync(() => endpoint.GetStreamUrl(token) is null);
    }

    [Fact]
    public async Task UnknownToken_Returns404()
    {
        await using var endpoint = new LocalStreamEndpoint(NullLogger<LocalStreamEndpoint>.Instance);
        var pipe = new Pipe();
        var token = Guid.NewGuid();

        var knownUrl = await endpoint.RegisterStreamAsync(token, pipe.Reader, TestContext.Current.CancellationToken);
        var uri = new Uri(knownUrl);
        var missingUrl = $"http://127.0.0.1:{uri.Port}/stream/{Guid.NewGuid()}";

        using var http = new HttpClient();
        var response = await http.GetAsync(missingUrl, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 20; i++)
        {
            if (condition())
                return;

            await Task.Yield();
        }

        Assert.True(condition());
    }
}

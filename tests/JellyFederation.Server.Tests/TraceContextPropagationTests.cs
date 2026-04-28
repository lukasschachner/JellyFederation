using System.Diagnostics;
using JellyFederation.Shared.Telemetry;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class TraceContextPropagationTests
{
    [Fact]
    public void ExtractFromHeaders_WithValidTraceParent_ReturnsIncomingContext()
    {
        using var activity = new Activity("incoming");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.TraceStateString = "rojo=1";
        activity.Start();

        IReadOnlyDictionary<string, string?> headers = new Dictionary<string, string?>
        {
            ["traceparent"] = activity.Id,
            ["tracestate"] = activity.TraceStateString
        };

        var context = TraceContextPropagation.ExtractFromHeaders(headers, out var contextSource);

        Assert.Equal("incoming_http", contextSource);
        Assert.Equal(activity.TraceId, context.TraceId);
        Assert.Equal(activity.SpanId, context.SpanId);
        Assert.Equal(activity.TraceStateString, context.TraceState);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    public void ExtractFromHeaders_WithMissingOrMalformedTraceParent_ReturnsDefaultContext(string? traceParent)
    {
        IReadOnlyDictionary<string, string?> headers = new Dictionary<string, string?>
        {
            ["traceparent"] = traceParent
        };

        var context = TraceContextPropagation.ExtractFromHeaders(headers, out var contextSource);

        Assert.Equal("local_generated", contextSource);
        Assert.Equal(default, context.TraceId);
        Assert.Equal(default, context.SpanId);
    }

    [Fact]
    public void InjectToHttpRequest_WithActivity_WritesTraceHeadersAndBaggage()
    {
        using var activity = new Activity("outgoing");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.AddBaggage("apiKey", "secret");
        activity.AddBaggage("feature", "sync");
        activity.Start();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");

        TraceContextPropagation.InjectToHttpRequest(request, activity);

        Assert.True(request.Headers.TryGetValues("traceparent", out var traceParents));
        Assert.Equal(activity.Id, Assert.Single(traceParents));

        Assert.True(request.Headers.TryGetValues("baggage", out var baggageValues));
        var baggage = Assert.Single(baggageValues);
        Assert.Contains("apiKey=%5BREDACTED%5D", baggage);
        Assert.Contains("feature=sync", baggage);
    }

    [Fact]
    public void InjectCorrelationId_ReplacesExistingValue()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        request.Headers.Add("X-Correlation-ID", "old");

        TraceContextPropagation.InjectCorrelationId(request.Headers, "new-id");

        Assert.True(request.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Equal("new-id", Assert.Single(values));
    }

    [Fact]
    public void ExtractCorrelationId_UsesTrimmedHeaderOrGeneratesFallback()
    {
        IReadOnlyDictionary<string, string?> withHeader = new Dictionary<string, string?>
        {
            ["x-correlation-id"] = "  abc123  "
        };
        IReadOnlyDictionary<string, string?> missing = new Dictionary<string, string?>();

        var extracted = TraceContextPropagation.ExtractCorrelationId(withHeader);
        var generated = TraceContextPropagation.ExtractCorrelationId(missing);

        Assert.Equal("abc123", extracted);
        Assert.False(string.IsNullOrWhiteSpace(generated));
        Assert.Equal(32, generated.Length);
    }
}

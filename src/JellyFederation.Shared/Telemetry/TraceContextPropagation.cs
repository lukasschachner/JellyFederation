using System.Diagnostics;
using System.Net.Http.Headers;

namespace JellyFederation.Shared.Telemetry;

public static class TraceContextPropagation
{
    public static ActivityContext ExtractFromHeaders(
        IReadOnlyDictionary<string, string?> headers,
        out string contextSource)
    {
        headers.TryGetValue("traceparent", out var traceParent);
        headers.TryGetValue("tracestate", out var traceState);

        if (!string.IsNullOrWhiteSpace(traceParent) &&
            ActivityContext.TryParse(traceParent, traceState, out var context))
        {
            contextSource = "incoming_http";
            return context;
        }

        contextSource = "local_generated";
        return default;
    }

    public static void InjectToHttpRequest(HttpRequestMessage request, Activity? activity = null)
    {
        var active = activity ?? Activity.Current;
        if (active is null)
            return;

        request.Headers.Remove("traceparent");
        request.Headers.Add("traceparent", active.Id);

        if (!string.IsNullOrWhiteSpace(active.TraceStateString))
        {
            request.Headers.Remove("tracestate");
            request.Headers.Add("tracestate", active.TraceStateString);
        }

        if (active.Baggage.Any())
        {
            var baggage = string.Join(",", active.Baggage.Select(x =>
                $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(TelemetryRedaction.Redact(x.Key, x.Value) ?? string.Empty)}"));
            request.Headers.Remove("baggage");
            request.Headers.TryAddWithoutValidation("baggage", baggage);
        }
    }

    public static void InjectCorrelationId(HttpRequestHeaders headers, string correlationId)
    {
        headers.Remove("X-Correlation-ID");
        headers.Add("X-Correlation-ID", correlationId);
    }

    public static string ExtractCorrelationId(IReadOnlyDictionary<string, string?> headers)
    {
        if (headers.TryGetValue("x-correlation-id", out var correlationId) &&
            !string.IsNullOrWhiteSpace(correlationId))
            return correlationId.Trim();

        return FederationTelemetry.CreateCorrelationId();
    }
}

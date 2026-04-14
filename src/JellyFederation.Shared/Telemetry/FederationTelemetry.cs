using System.Diagnostics;

namespace JellyFederation.Shared.Telemetry;

public static class FederationTelemetry
{
    public const string ActivitySourcePluginName = "JellyFederation.Plugin";
    public const string ActivitySourceServerName = "JellyFederation.Server";
    public const string MeterName = "JellyFederation.Telemetry";

    public const string SpanFederationOperation = "federation.operation";
    public const string SpanPluginHttpClient = "federation.plugin.http.client";
    public const string SpanServerHttpRequest = "federation.server.http.request";
    public const string SpanSignalRWorkflow = "federation.signalr.workflow";

    public const string TagOperation = "federation.operation";
    public const string TagComponent = "federation.component";
    public const string TagCorrelationId = "federation.correlation_id";
    public const string TagPeerServerId = "federation.peer_server_id";
    public const string TagOutcome = "federation.outcome";
    public const string TagFailureCode = "federation.failure_code";
    public const string TagFailureCategory = "federation.failure_category";
    public const string TagContextSource = "telemetry.context_source";
    public const string TagReleaseVersion = "federation.release_version";
    public const string TagTaxonomyVersion = "federation.taxonomy_version";

    public const string OutcomeSuccess = "success";
    public const string OutcomeError = "error";
    public const string OutcomeTimeout = "timeout";
    public const string OutcomeCancelled = "cancelled";

    public const string TaxonomyVersion = "v1";

    public static readonly ActivitySource PluginActivitySource = new(ActivitySourcePluginName);
    public static readonly ActivitySource ServerActivitySource = new(ActivitySourceServerName);
    public static string CurrentReleaseVersion { get; set; } = "dev";

    public static string CreateCorrelationId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static void SetCommonTags(
        Activity? activity,
        string operation,
        string component,
        string correlationId,
        string? peerServerId = null,
        string? releaseVersion = null)
    {
        if (activity is null)
            return;

        activity.SetTag(TagOperation, operation);
        activity.SetTag(TagComponent, component);
        activity.SetTag(TagCorrelationId, correlationId);
        activity.SetTag(TagTaxonomyVersion, TaxonomyVersion);
        if (!string.IsNullOrWhiteSpace(peerServerId))
            activity.SetTag(TagPeerServerId, peerServerId);
        if (!string.IsNullOrWhiteSpace(releaseVersion))
            activity.SetTag(TagReleaseVersion, releaseVersion);
    }

    public static void SetOutcome(Activity? activity, string outcome, Exception? ex = null)
    {
        if (activity is null)
            return;

        activity.SetTag(TagOutcome, outcome);
        if (outcome == OutcomeError && ex is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, TelemetryRedaction.SanitizeErrorMessage(ex.Message));
            activity.SetTag("error.type", ex.GetType().Name);
            activity.SetTag("error.message", TelemetryRedaction.SanitizeErrorMessage(ex.Message));
        }
    }

    public static void SetFailure(Activity? activity, Models.FailureDescriptor? failure)
    {
        if (activity is null || failure is null)
            return;

        var sanitized = TelemetryRedaction.SanitizeFailureDescriptor(failure);
        activity.SetTag(TagFailureCode, sanitized.Code);
        activity.SetTag(TagFailureCategory, sanitized.Category.ToString());
        if (!string.IsNullOrWhiteSpace(sanitized.CorrelationId))
            activity.SetTag(TagCorrelationId, sanitized.CorrelationId);
    }
}

using System.Diagnostics.Metrics;

namespace JellyFederation.Shared.Telemetry;

public static class FederationMetrics
{
    public const string OperationsTotalName = "jellyfederation.federation.operations.total";
    public const string OperationDurationName = "jellyfederation.federation.operation.duration";
    public const string TimeoutsTotalName = "jellyfederation.federation.timeouts.total";
    public const string RetriesTotalName = "jellyfederation.federation.retries.total";
    public const string InflightName = "jellyfederation.federation.inflight";
    private static readonly Meter Meter = new(FederationTelemetry.MeterName);

    private static readonly Counter<long> OperationsTotal =
        Meter.CreateCounter<long>(OperationsTotalName, "{operation}", "Federation operations executed");

    private static readonly Histogram<double> OperationDuration =
        Meter.CreateHistogram<double>(OperationDurationName, "ms", "Federation operation duration");

    private static readonly Counter<long> TimeoutsTotal =
        Meter.CreateCounter<long>(TimeoutsTotalName, "{timeout}", "Federation operation timeouts");

    private static readonly Counter<long> RetriesTotal =
        Meter.CreateCounter<long>(RetriesTotalName, "{retry}", "Federation operation retries");

    private static readonly UpDownCounter<long> Inflight =
        Meter.CreateUpDownCounter<long>(InflightName, "{operation}", "Federation operations in-flight");

    public static readonly string MeterName = FederationTelemetry.MeterName;

    public static void RecordOperation(
        string operation,
        string component,
        string outcome,
        TimeSpan duration,
        string? releaseVersion = null,
        string? failureCategory = null,
        string? failureCode = null)
    {
        var tags = TelemetryRedaction.BuildMetricTags(
            operation,
            component,
            outcome,
            release: releaseVersion,
            failureCategory: failureCategory,
            failureCode: failureCode);
        OperationsTotal.Add(1, tags);
        OperationDuration.Record(duration.TotalMilliseconds, tags);
    }

    public static void RecordTimeout(string operation, string component, string? releaseVersion = null)
    {
        var tags = TelemetryRedaction.BuildMetricTags(operation, component, release: releaseVersion);
        TimeoutsTotal.Add(1, tags);
    }

    public static void RecordRetry(string operation, string component, string? releaseVersion = null)
    {
        var tags = TelemetryRedaction.BuildMetricTags(operation, component, release: releaseVersion);
        RetriesTotal.Add(1, tags);
    }

    public static IDisposable BeginInflight(string operation, string component, string? releaseVersion = null)
    {
        var tags = TelemetryRedaction.BuildMetricTags(operation, component, release: releaseVersion);
        Inflight.Add(1, tags);
        return new InflightScope(tags);
    }

    private sealed class InflightScope : IDisposable
    {
        private readonly KeyValuePair<string, object?>[] _tags;

        public InflightScope(KeyValuePair<string, object?>[] tags)
        {
            _tags = tags;
        }

        public void Dispose()
        {
            Inflight.Add(-1, _tags);
        }
    }
}

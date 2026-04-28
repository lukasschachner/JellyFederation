using JellyFederation.Shared.Telemetry;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class FederationMetricsTests
{
    [Fact]
    public void MetricsApis_CanBeRecordedWithoutExceptions()
    {
        FederationMetrics.RecordOperation(
            operation: "sync",
            component: "server",
            outcome: FederationTelemetry.OutcomeSuccess,
            duration: TimeSpan.FromMilliseconds(12),
            releaseVersion: "1.0.0",
            failureCategory: null,
            failureCode: null);

        FederationMetrics.RecordTimeout("sync", "server", "1.0.0");
        FederationMetrics.RecordRetry("sync", "server", "1.0.0");

        using var scope = FederationMetrics.BeginInflight("sync", "server", "1.0.0");
    }
}

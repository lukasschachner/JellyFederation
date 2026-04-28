using System.Diagnostics;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class FederationTelemetryTests
{
    [Fact]
    public void CreateCorrelationId_Returns32CharacterValue()
    {
        var id = FederationTelemetry.CreateCorrelationId();

        Assert.Equal(32, id.Length);
        Assert.Matches("^[a-f0-9]{32}$", id);
    }

    [Fact]
    public void SetCommonTags_SetsExpectedTags()
    {
        using var activity = new Activity("common-tags").Start();

        FederationTelemetry.SetCommonTags(
            activity,
            operation: "library.sync",
            component: "plugin",
            correlationId: "corr-id",
            peerServerId: "peer-1",
            releaseVersion: "1.2.3");

        Assert.Equal("library.sync", activity.Tags.Single(t => t.Key == FederationTelemetry.TagOperation).Value);
        Assert.Equal("plugin", activity.Tags.Single(t => t.Key == FederationTelemetry.TagComponent).Value);
        Assert.Equal("corr-id", activity.Tags.Single(t => t.Key == FederationTelemetry.TagCorrelationId).Value);
        Assert.Equal("peer-1", activity.Tags.Single(t => t.Key == FederationTelemetry.TagPeerServerId).Value);
        Assert.Equal("1.2.3", activity.Tags.Single(t => t.Key == FederationTelemetry.TagReleaseVersion).Value);
    }

    [Fact]
    public void SetOutcome_WithError_SetsStatusAndErrorTags()
    {
        using var activity = new Activity("outcome").Start();

        FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, new InvalidOperationException("boom"));

        Assert.Equal(FederationTelemetry.OutcomeError,
            activity.Tags.Single(t => t.Key == FederationTelemetry.TagOutcome).Value);
        Assert.Equal("InvalidOperationException", activity.Tags.Single(t => t.Key == "error.type").Value);
    }

    [Fact]
    public void SetFailure_MapsFailureDescriptorTags()
    {
        using var activity = new Activity("failure").Start();
        var failure = FailureDescriptor.Timeout("request.timeout", "timed out", "corr-2");

        FederationTelemetry.SetFailure(activity, failure);

        Assert.Equal("request.timeout", activity.Tags.Single(t => t.Key == FederationTelemetry.TagFailureCode).Value);
        Assert.Equal(nameof(FailureCategory.Timeout),
            activity.Tags.Single(t => t.Key == FederationTelemetry.TagFailureCategory).Value);
        Assert.Equal("corr-2", activity.Tags.Single(t => t.Key == FederationTelemetry.TagCorrelationId).Value);
    }
}

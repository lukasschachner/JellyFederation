using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class PluginComponentTests
{
    [Fact]
    public void TelemetryRedaction_RedactsSensitiveValues_AndTruncatesNonSensitiveValues()
    {
        Assert.Equal("[REDACTED]", TelemetryRedaction.Redact("apiKey", "secret-value"));
        Assert.Equal("[REDACTED]", TelemetryRedaction.Redact("user_email", "user@example.com"));
        Assert.Null(TelemetryRedaction.Redact("apiKey", null));
        Assert.Equal(string.Empty, TelemetryRedaction.Redact("apiKey", string.Empty));

        var longValue = new string('x', 300);

        var redacted = TelemetryRedaction.Redact("title", longValue);

        Assert.Equal(256, redacted!.Length);
        Assert.Equal(new string('x', 256), redacted);
    }

    [Fact]
    public void TelemetryRedaction_SanitizesFailureDescriptorDetails()
    {
        var failure = new FailureDescriptor(
            "auth.failed",
            FailureCategory.Authorization,
            "Token was rejected for email address user@example.com",
            Details: new Dictionary<string, string?>
            {
                ["api_key"] = "abc123",
                ["item_title"] = "Public title"
            });

        var sanitized = TelemetryRedaction.SanitizeFailureDescriptor(failure);

        Assert.DoesNotContain("Token", sanitized.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", sanitized.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("[REDACTED]", sanitized.Details!["api_key"]);
        Assert.Equal("Public title", sanitized.Details["item_title"]);
    }

    [Fact]
    public void TelemetryRedaction_BuildMetricTags_UsesOnlyLowCardinalityNonEmptyDimensions()
    {
        var tags = TelemetryRedaction.BuildMetricTags(
            operation: " transfer ",
            component: "plugin",
            outcome: "success",
            peerServer: new string('p', 80),
            release: " ",
            failureCategory: null,
            failureCode: "none");

        Assert.Equal(["operation", "component", "outcome", "peer_server", "failure_code"], tags.Select(t => t.Key));
        Assert.Equal("transfer", tags.Single(t => t.Key == "operation").Value);
        Assert.Equal(64, Assert.IsType<string>(tags.Single(t => t.Key == "peer_server").Value).Length);
    }


    [Fact]
    public void PluginConfiguration_Defaults_AreSafeForInitialConfiguration()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(string.Empty, configuration.FederationServerUrl);
        Assert.Equal(string.Empty, configuration.ServerId);
        Assert.Equal(string.Empty, configuration.ApiKey);
        Assert.Equal(string.Empty, configuration.ServerName);
        Assert.Equal(string.Empty, configuration.PublicJellyfinUrl);
        Assert.Equal(string.Empty, configuration.DownloadDirectory);
        Assert.Equal(string.Empty, configuration.OverridePublicIp);
        Assert.Equal(0, configuration.HolePunchPort);
        Assert.True(configuration.PreferQuicForLargeFiles);
        Assert.Equal(512L * 1024 * 1024, configuration.LargeFileQuicThresholdBytes);
        Assert.Equal("stun.l.google.com:19302", configuration.StunServer);
        Assert.Equal("jellyfederation-plugin", configuration.TelemetryServiceName);
        Assert.Equal("http://localhost:4317", configuration.TelemetryOtlpEndpoint);
        Assert.Equal(1.0, configuration.TelemetrySamplingRatio);
        Assert.True(configuration.EnableTracing);
        Assert.True(configuration.EnableMetrics);
        Assert.True(configuration.EnableLogs);
        Assert.True(configuration.RedactionEnabled);
    }

}

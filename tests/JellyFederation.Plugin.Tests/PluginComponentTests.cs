using JellyFederation.Plugin;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class PluginComponentTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dev.sh")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }

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
    public void DevScript_PluginId_MatchesPluginGuid()
    {
        var devScript = File.ReadAllText(Path.Combine(RepoRoot, "dev.sh"));
        var pluginSource = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Plugin.cs"));

        const string pluginGuid = "e5c0cda1-805e-41e2-9654-e17143dc31a1";
        Assert.Contains($"PluginGuid = new(\"{pluginGuid}\")", pluginSource);
        Assert.Contains($"PLUGIN_ID=\"{pluginGuid}\"", devScript);
        Assert.DoesNotContain("a1b2c3d4-e5f6-7890-abcd-ef1234567890", devScript);
    }

    [Fact]
    public void PluginConfigurationPage_ReferencesAllPersistedFields()
    {
        var html = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Web",
            "configurationpage.html"));
        var javascript = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Web",
            "configurationpage.js"));

        Assert.Contains("configurationpage?name=jellyfederation.js", html);

        string[] fieldIds =
        [
            "federationServerUrl",
            "serverId",
            "apiKey",
            "serverName",
            "publicJellyfinUrl",
            "downloadDirectory",
            "stunServer",
            "turnServer",
            "turnUsername",
            "turnCredential",
            "overridePublicIp",
            "holePunchPort",
            "preferQuicForLargeFiles",
            "largeFileQuicThresholdBytes"
        ];

        foreach (var fieldId in fieldIds)
        {
            Assert.Contains($"id=\"{fieldId}\"", html);
            Assert.Contains($"#{fieldId}", javascript);
        }
    }

    [Fact]
    public void PluginTelemetryBootstrap_IsRegisteredWithoutOpenTelemetryExporterPackages()
    {
        var registrator = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "PluginServiceRegistrator.cs"));
        var pluginProject = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "JellyFederation.Plugin.csproj"));

        Assert.Contains("AddHostedService<TelemetryBootstrapService>", registrator);
        Assert.DoesNotContain("OpenTelemetry", pluginProject);
    }

    [Fact]
    public void PluginServiceRegistrator_RegisterServices_AddsResolvableRuntimeDescriptors()
    {
        var services = new ServiceCollection();
        var registrator = new PluginServiceRegistrator();

        registrator.RegisterServices(services, applicationHost: null!);

        Assert.Contains(services, d => d.ServiceType == typeof(IPluginConfigurationProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(HolePunchService) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(WebRtcTransportService) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(LocalStreamEndpoint) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(FederationSignalRService) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(TelemetryBootstrapService));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(FederationStartupService));

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IPluginConfigurationProvider>());
        Assert.Contains("FederationPlugin has not been instantiated", ex.Message);
    }

    [Fact]
    public void PluginServiceRegistrator_RegistersRequiredRuntimeServices()
    {
        var registrator = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "PluginServiceRegistrator.cs"));

        Assert.Contains("AddHttpClient<LibrarySyncService>", registrator);
        Assert.Contains("AddSingleton<HolePunchService>", registrator);
        Assert.Contains("AddSingleton<WebRtcTransportService>", registrator);
        Assert.Contains("AddSingleton<LocalStreamEndpoint>", registrator);
        Assert.Contains("AddHttpClient<FileTransferService>", registrator);
        Assert.Contains("AddSingleton<FederationSignalRService>", registrator);
        Assert.Contains("AddHostedService<FederationStartupService>", registrator);
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
        Assert.Equal(string.Empty, configuration.TurnServer);
        Assert.Equal(string.Empty, configuration.TurnUsername);
        Assert.Equal(string.Empty, configuration.TurnCredential);
        Assert.Equal("jellyfederation-plugin", configuration.TelemetryServiceName);
        Assert.Equal("http://localhost:4317", configuration.TelemetryOtlpEndpoint);
        Assert.Equal(1.0, configuration.TelemetrySamplingRatio);
        Assert.True(configuration.EnableTracing);
        Assert.True(configuration.EnableMetrics);
        Assert.True(configuration.EnableLogs);
        Assert.True(configuration.RedactionEnabled);
    }

}

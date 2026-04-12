using MediaBrowser.Model.Plugins;

namespace JellyFederation.Plugin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string FederationServerUrl { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    /// <summary>
    /// Optional fallback Jellyfin base URL used to build artwork links when preview embedding is not possible.
    /// Must be reachable by browsers viewing the federation web UI.
    /// Example: https://jellyfin.example.com
    /// </summary>
    public string PublicJellyfinUrl { get; set; } = string.Empty;
    /// <summary>
    /// Local directory where downloaded federation files will be placed
    /// so Jellyfin can pick them up after a library scan.
    /// </summary>
    public string DownloadDirectory { get; set; } = string.Empty;
    /// <summary>
    /// Optional: override the public IP this server advertises for hole punching.
    /// Useful when running behind NAT or in Docker. Leave empty to auto-detect.
    /// </summary>
    public string OverridePublicIp { get; set; } = string.Empty;
    /// <summary>
    /// UDP port to bind for hole punching. Set a fixed port when behind NAT/Docker
    /// so you can port-forward it. 0 = ephemeral (auto).
    /// </summary>
    public int HolePunchPort { get; set; } = 0;

    public string TelemetryServiceName { get; set; } = "jellyfederation-plugin";
    public string TelemetryOtlpEndpoint { get; set; } = "http://localhost:4317";
    public double TelemetrySamplingRatio { get; set; } = 1.0;
    public bool EnableTracing { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableLogs { get; set; } = true;
    public bool RedactionEnabled { get; set; } = true;
}

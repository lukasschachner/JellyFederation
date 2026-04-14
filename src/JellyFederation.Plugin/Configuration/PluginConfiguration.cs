using MediaBrowser.Model.Plugins;

namespace JellyFederation.Plugin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string FederationServerUrl { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    ///     Optional fallback Jellyfin base URL used to build artwork links when preview embedding is not possible.
    ///     Must be reachable by browsers viewing the federation web UI.
    ///     Example: https://jellyfin.example.com
    /// </summary>
    public string PublicJellyfinUrl { get; set; } = string.Empty;

    /// <summary>
    ///     Local directory where downloaded federation files will be placed
    ///     so Jellyfin can pick them up after a library scan.
    /// </summary>
    public string DownloadDirectory { get; set; } = string.Empty;

    /// <summary>
    ///     Optional hint for backward-compat peers: override the public IP this server advertises for hole punching.
    ///     Only relevant when communicating with peers running old plugin versions (pre-WebRTC ICE).
    ///     Leave empty to auto-detect. WebRTC ICE peers handle NAT traversal automatically.
    /// </summary>
    public string OverridePublicIp { get; set; } = string.Empty;

    /// <summary>
    ///     Optional hint for backward-compat peers: UDP port to bind for hole punching.
    ///     Only relevant when communicating with peers running old plugin versions (pre-WebRTC ICE).
    ///     0 = ephemeral (auto). WebRTC ICE peers do not require a fixed port.
    /// </summary>
    public int HolePunchPort { get; set; } = 0;

    /// <summary>
    ///     Prefer QUIC for large files when both peers support it.
    ///     If disabled, all transfers stay on ARQ-over-UDP.
    /// </summary>
    public bool PreferQuicForLargeFiles { get; set; } = true;

    /// <summary>
    ///     Transfers at or above this size are considered large-file QUIC candidates.
    /// </summary>
    public long LargeFileQuicThresholdBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>
    ///     STUN server used for WebRTC ICE candidate gathering.
    ///     Default is Google's public STUN server. Override for air-gapped deployments.
    /// </summary>
    public string StunServer { get; set; } = "stun.l.google.com:19302";

    public string TelemetryServiceName { get; set; } = "jellyfederation-plugin";
    public string TelemetryOtlpEndpoint { get; set; } = "http://localhost:4317";
    public double TelemetrySamplingRatio { get; set; } = 1.0;
    public bool EnableTracing { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableLogs { get; set; } = true;
    public bool RedactionEnabled { get; set; } = true;
}
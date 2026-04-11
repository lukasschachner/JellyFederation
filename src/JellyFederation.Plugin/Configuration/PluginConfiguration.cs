using MediaBrowser.Model.Plugins;

namespace JellyFederation.Plugin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string FederationServerUrl { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
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
}

namespace JellyFederation.Plugin.Configuration;

/// <summary>
/// Provides access to the current plugin configuration via DI,
/// avoiding the static <see cref="FederationPlugin.Instance"/> singleton.
/// </summary>
public interface IPluginConfigurationProvider
{
    /// <summary>
    /// Returns the current <see cref="PluginConfiguration"/>.
    /// Always reads the live value so config changes take effect without a restart.
    /// </summary>
    PluginConfiguration GetConfiguration();
}

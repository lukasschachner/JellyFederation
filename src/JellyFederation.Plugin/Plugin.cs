using System.Reflection;
using JellyFederation.Plugin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyFederation.Plugin;

public class FederationPlugin : BasePlugin<PluginConfiguration>, IHasWebPages, IPluginConfigurationProvider
{
    public const string PluginName = "JellyFederation";
    public static readonly Guid PluginGuid = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    public FederationPlugin(IApplicationPaths paths, IXmlSerializer serializer)
        : base(paths, serializer)
    {
        Instance = this;
    }

    public static FederationPlugin? Instance { get; private set; }

    public static string ReleaseVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

    public override string Name => PluginName;
    public override Guid Id => PluginGuid;
    public override string Description => "Federation media sharing between Jellyfin servers";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.configurationpage.html"
            }
        ];
    }

    /// <inheritdoc />
    PluginConfiguration IPluginConfigurationProvider.GetConfiguration()
    {
        return Configuration;
    }
}
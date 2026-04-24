using JellyFederation.Plugin.Configuration;
using JellyFederation.Plugin.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyFederation.Plugin;

/// <summary>
///     Registers all plugin services with Jellyfin's DI container.
///     Jellyfin discovers this class automatically via reflection.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        // Configuration provider — delegates to the plugin singleton but accessed via DI
        services.AddSingleton<IPluginConfigurationProvider>(_ => FederationPlugin.Instance
                                                                 ?? throw new InvalidOperationException(
                                                                     "FederationPlugin has not been instantiated yet."));

        services.AddLogging();

        services.AddHttpClient<LibrarySyncService>();
        services.AddSingleton<HolePunchService>();
        services.AddSingleton<WebRtcTransportService>();
        services.AddSingleton<LocalStreamEndpoint>();
        // Use AddHttpClient so the DI container manages HttpClient lifetime via IHttpClientFactory,
        // avoiding socket exhaustion from a long-lived singleton HttpClient.
        services.AddHttpClient<FileTransferService>();
        services.AddSingleton<FederationSignalRService>();
        services.AddHostedService<TelemetryBootstrapService>();
        services.AddHostedService<FederationStartupService>();
    }
}
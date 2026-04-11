using JellyFederation.Plugin.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyFederation.Plugin;

/// <summary>
/// Registers all plugin services with Jellyfin's DI container.
/// Jellyfin discovers this class automatically via reflection.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddHttpClient<LibrarySyncService>();
        services.AddSingleton<HolePunchService>();
        services.AddSingleton<FileTransferService>();
        services.AddSingleton<FederationSignalRService>();
        services.AddHostedService<FederationStartupService>();
    }
}

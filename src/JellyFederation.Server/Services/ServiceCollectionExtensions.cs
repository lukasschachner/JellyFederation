using Microsoft.Extensions.DependencyInjection;

namespace JellyFederation.Server.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFederationWorkflowServices(this IServiceCollection services)
    {
        services.AddScoped<InvitationService>();

        return services;
    }
}

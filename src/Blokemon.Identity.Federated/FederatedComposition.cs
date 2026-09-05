using Blokemon.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blokemon.Identity.Federated;

/// <summary>How the host composes the federation: the provider, the rate limit, the routes.</summary>
public static class FederatedComposition
{
    public static IServiceCollection AddBlokeBotChannels(this IServiceCollection services)
    {
        // The implementation is always present; the registry lists it only when the
        // deployment enables Blokemon:Identity:Providers:BlokeBot.
        services.AddSingleton<IIdentityProvider, BlokeBotProvider>();
        services.AddSingleton<HandoffRateLimits>();
        return services;
    }

    public static IEndpointRouteBuilder MapBlokeBotChannels(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");
        ChannelEndpoints.Map(api.MapGroup("/tenant"));
        OperatorTenantEndpoints.Map(api.MapGroup("/operator"));
        HandoffExchangeEndpoints.Map(endpoints);
        return endpoints;
    }
}

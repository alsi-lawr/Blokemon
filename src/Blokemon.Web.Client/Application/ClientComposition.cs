using Blokemon.Product;
using Blokemon.Web.Application;
using Blokemon.Web.Client.Api;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Blokemon.Web.Client.Application;

public static class ClientComposition
{
    public static IServiceCollection AddBlokemonClient(
        this IServiceCollection services,
        HttpClient http,
        BlokemonCatalogue catalogue,
        PlayModeAvailability playModes,
        EconomyRules economy
    )
    {
        services.AddSingleton(http);
        services.AddSingleton(catalogue);
        services.AddSingleton(playModes);
        services.AddSingleton(economy);
        services.AddScoped<IStateDocumentStore, IndexedDbStateDocumentStore>();
        services.AddScoped<LocalMatchService>();
        services.AddScoped<LocalApplicationService>();
        services.AddScoped<BlokemonApiClient>();
        services.AddScoped<PlayModeApplication>();
        services.AddScoped<IBlokemonApplication>(static provider =>
            provider.GetRequiredService<PlayModeApplication>()
        );
        services.AddScoped<CardArtWarmup>();
        return services;
    }
}

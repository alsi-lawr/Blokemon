using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Client;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Client.Persistence;
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
        // PlayModeApplication takes two IBlokemonApplication implementations, so the container
        // cannot pick them itself: the server side is the HTTP transport, the browser side is
        // the in-browser application service.
        services.AddScoped<PlayModeApplication>(static provider =>
            new(
                provider.GetRequiredService<BlokemonApiClient>(),
                provider.GetRequiredService<LocalApplicationService>(),
                provider.GetRequiredService<IStateDocumentStore>(),
                provider.GetRequiredService<PlayModeAvailability>()
            )
        );
        services.AddScoped<IBlokemonApplication>(static provider =>
            provider.GetRequiredService<PlayModeApplication>()
        );
        services.AddScoped<CardArtWarmup>();
        services.AddScoped<SoundBoard>();
        return services;
    }
}

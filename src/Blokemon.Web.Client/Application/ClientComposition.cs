using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Client;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Client.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Blokemon.Web.Client.Application;

public static class ClientComposition
{
    public static IServiceCollection AddBlokemonClient(
        this IServiceCollection services,
        HttpClient http,
        BlokemonCatalogue catalogue,
        PlayModeAvailability playModes,
        EconomyRules economy,
        SessionTokenStore? tokens = null
    )
    {
        services.AddSingleton(http);
        services.AddSingleton(tokens ?? new SessionTokenStore());
        services.AddSingleton(catalogue);
        services.AddSingleton(playModes);
        services.AddSingleton(economy);
        services.AddScoped<IStateDocumentStore, IndexedDbStateDocumentStore>();
        services.AddScoped<LocalMatchService>();
        services.AddScoped<LocalApplicationService>(static provider =>
            new(
                provider.GetRequiredService<BlokemonCatalogue>(),
                provider.GetRequiredService<IStateDocumentStore>(),
                provider.GetRequiredService<LocalMatchService>(),
                provider.GetRequiredService<EconomyRules>(),
                ProfileAuthorityPolicy.MigrateCompatible
            )
        );
        services.AddScoped<BlokemonApiClient>();
        // PlayModeApplication takes two IBlokemonApplication implementations, so the container
        // cannot pick them itself: the server side is the HTTP transport, the browser side is
        // the in-browser application service.
        services.AddScoped<PlayModeApplication>(static provider =>
            new(
                provider.GetRequiredService<BlokemonApiClient>(),
                provider.GetRequiredService<LocalApplicationService>(),
                provider.GetRequiredService<IStateDocumentStore>(),
                provider.GetRequiredService<PlayModeAvailability>(),
                provider.GetRequiredService<IReauthenticationHost>()
            )
        );
        services.AddScoped<IBlokemonApplication>(static provider =>
            provider.GetRequiredService<PlayModeApplication>()
        );
        services.AddApplicationCapabilities();
        services.AddScoped<CardArtWarmup>();
        services.AddScoped<SoundBoard>();
        return services;
    }

    internal static IServiceCollection AddApplicationCapabilities(this IServiceCollection services)
    {
        // The session the browser holds, the tenant it runs as, the hosted-mode receiver and
        // the way this host re-authenticates; every composition that renders the shell needs
        // them because the menu shows the signed-in state.
        services.TryAddSingleton<SessionTokenStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SessionApiClient>();
        services.AddScoped<SessionHolder>();
        services.AddScoped<TenantContext>();
        services.AddScoped<HostedFrame>();
        services.AddScoped<SignInFlow>();
        services.AddScoped<PasskeyApiClient>();
        services.AddScoped<PasskeyCeremony>();
        services.AddScoped<PasskeyFlow>();
        services.AddScoped<IReauthenticationHost, ClientReauthentication>();
        services.AddScoped<IApplicationDocumentInvalidations, BrowserDocumentInvalidations>();
        services.AddScoped<ApplicationSnapshotCoordinator>();
        services.AddScoped<IApplicationStateReader>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        services.AddScoped<IApplicationStateRefresher>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        services.AddScoped<IDeckOperations>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        services.AddScoped<IStarterDeckOperations>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        services.AddScoped<IMatchOperations>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        services.AddScoped<IMatchRecoveryOperations>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        services.AddScoped<IPackOperations>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        services.AddScoped<IProfileOperations>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        services.AddScoped<IPlayModeOperations>(static provider =>
            provider.GetRequiredService<ApplicationSnapshotCoordinator>()
        );
        return services;
    }
}

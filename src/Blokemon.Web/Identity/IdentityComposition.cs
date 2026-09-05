using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Identity.Federated;
using Blokemon.Product;
using Blokemon.Web.Identity.Passkeys;
using Blokemon.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Blokemon.Web.Identity;

/// <summary>The server host's identity services: configuration, registry, sessions and the sweep.</summary>
public static class IdentityComposition
{
    public static IServiceCollection AddServerIdentity(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Resolved here so that an invalid value fails the host before it listens.
        var identity = IdentityConfigurationModule.Resolve(configuration);
        services.AddSingleton(identity);
        services.AddSingleton(TimeProvider.System);
        // Built from the providers the host registered; none in this host until BLOKEMON-150
        // and 151 ship theirs, so a published host has an empty registry.
        services.AddSingleton(static provider => new IdentityProviderRegistry(
            provider.GetRequiredService<IdentityConfiguration>(),
            provider.GetServices<IIdentityProvider>()
        ));
        services.AddSingleton<ClientLockouts>();
        services.AddSingleton(static provider => new SignInDiagnostics(
            provider.GetRequiredService<TimeProvider>().GetUtcNow()
        ));
        services.AddBlokeBotChannels();
        services.AddSingleton<PasskeyChallenges>();
        if (identity.Passkeys is { Value: { } passkeys })
        {
            // The relying party is configured, so the first-party passkey ceremonies exist.
            services.AddSingleton(provider => new PasskeyCeremonies(
                passkeys,
                provider.GetRequiredService<PasskeyChallenges>()
            ));
        }
        // The first-party provider ships with or without the ceremonies: its simple login needs
        // none. The registry lists it only when the deployment enables it.
        services.AddSingleton<IIdentityProvider>(provider => new FirstPartyProvider(
            provider.GetService<PasskeyCeremonies>(),
            provider.GetRequiredService<PasskeyChallenges>(),
            provider.GetRequiredService<IDbContextFactory<BlokemonDbContext>>()
        ));
        services.AddScoped<CurrentSession>();
        services.AddScoped<ServerApplications>();
        services.AddScoped(static provider => new SignInServices(
            provider.GetRequiredService<IStateDocumentStore>(),
            provider.GetRequiredService<BlokemonCatalogue>(),
            provider.GetRequiredService<EconomyRules>(),
            provider.GetRequiredService<IdentityConfiguration>().SessionLifetime
        ));
        services.AddHostedService<SessionSweepService>();
        return services;
    }

    /// <summary>Puts the session policy in front of every <c>/api</c> request.</summary>
    public static IApplicationBuilder UseApiSessions(this IApplicationBuilder app) =>
        app.UseWhen(
            static context => context.Request.Path.StartsWithSegments("/api"),
            static branch => branch.UseMiddleware<ApiSessionMiddleware>()
        );

    /// <summary>
    /// Finishes identity start-up before the host serves anyone: the registry is built now, so
    /// a provider enabled without an implementation fails the host here, and the default tenant
    /// is made to exist.
    /// </summary>
    public static async Task StartIdentity(this WebApplication app)
    {
        _ = app.Services.GetRequiredService<IdentityProviderRegistry>();
        await using var scope = app.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<StateDocumentStore>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await Tenants.ensureDefault(documents, documents, time.GetUtcNow(), CancellationToken.None);
    }
}

/// <summary>
/// The lock-outs, one per guessable secret: operator bootstrap, recovery codes and the simple
/// login, each five failures per fifteen minutes. Three are keyed by the caller's client
/// address, which is the only client identity an anonymous-to-the-secret caller has; the login
/// is also keyed by the player name presented, so a name cannot be guessed at from many
/// addresses. The address is the connection's, or the forwarded one when the connection is a
/// known proxy (BLOKEMON-D-045, <see cref="Hosting.ForwardedClients"/>); this is the one
/// resolution the lock-outs share.
/// </summary>
public sealed class ClientLockouts
{
    public FailureLockout OperatorBootstrap { get; } = FailureLockout.OnRecoveryTerms();

    public FailureLockout Recovery { get; } = FailureLockout.OnRecoveryTerms();

    public FailureLockout Login { get; } = FailureLockout.OnRecoveryTerms();

    /// <summary>Keyed by the normalized player name a sign-in presented.</summary>
    public FailureLockout LoginName { get; } = FailureLockout.OnRecoveryTerms();

    public static string ClientOf(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Persistence;

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
        services.AddSingleton<OperatorBootstrapLockout>();
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
/// The operator bootstrap lock-out: five failures per client per fifteen minutes, keyed by the
/// caller's remote address, which is the only client identity an anonymous-to-the-code caller
/// has.
/// </summary>
public sealed class OperatorBootstrapLockout
{
    private readonly FailureLockout _lockout = FailureLockout.OnRecoveryTerms();

    public static string ClientOf(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public bool IsLockedOut(string client, DateTimeOffset now) => _lockout.IsLockedOut(client, now);

    public void RecordFailure(string client, DateTimeOffset now) =>
        _lockout.RecordFailure(client, now);
}

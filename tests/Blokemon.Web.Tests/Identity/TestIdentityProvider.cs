using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Blokemon.Web.Tests.Identity;

/// <summary>
/// The test-only provider double: asserts whatever a test scripted for a proof, so the
/// completion path is exercised and sessions are minted for the server-API journeys without
/// any real credential. It is registered only by <see cref="SessionHost"/>, which is the test
/// suite's factory; no published host ships or names it.
/// </summary>
internal sealed class TestIdentityProvider : IIdentityProvider
{
    public const string ProviderName = "test";

    private readonly ConcurrentDictionary<string, VerifiedIdentity> _proofs = new(
        StringComparer.Ordinal
    );

    public IdentityProviderName Name { get; } =
        IdentityProviderName.Create(ProviderName)
            is DomainResult<IdentityProviderName, ExternalIdentityFailure>.Succeeded name
            ? name.Value
            : throw new InvalidOperationException("The test provider name is well formed.");

    public VerifiedIdentity Accept(
        string proof,
        string subject,
        string? displayName,
        SessionProvenance provenance
    )
    {
        var identity = new VerifiedIdentity(
            Name,
            ExternalSubject.Create(subject)
                is DomainResult<ExternalSubject, ExternalIdentityFailure>.Succeeded parsed
                ? parsed.Value
                : throw new ArgumentException("Bad subject.", nameof(subject)),
            displayName,
            provenance
        );
        _proofs[proof] = identity;
        return identity;
    }

    public Task<DomainResult<VerifiedIdentity, SignInFailure>> Verify(
        string proof,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            _proofs.TryGetValue(proof, out var identity)
                ? DomainResult<VerifiedIdentity, SignInFailure>.NewSucceeded(identity)
                : DomainResult<VerifiedIdentity, SignInFailure>.NewFailed(
                    SignInFailure.NewProviderRefused(new("proof.unknown", "Unknown proof."))
                )
        );
}

/// <summary>
/// A server host for the session tests: production environment, its own SQLite directory, the
/// test provider registered and enabled unless a test asks for a bare host, and helpers to sign
/// in through the completion path and to call the API with or without a session.
/// </summary>
internal sealed class SessionHost : IAsyncDisposable
{
    private SessionHost(
        WebApplicationFactory<Program> factory,
        string dataDirectory,
        TestIdentityProvider provider
    )
    {
        Factory = factory;
        DataDirectory = dataDirectory;
        Provider = provider;
    }

    public WebApplicationFactory<Program> Factory { get; }

    public string DataDirectory { get; }

    public TestIdentityProvider Provider { get; }

    public static SessionHost Create(
        Action<IWebHostBuilder>? configure = null,
        bool withProvider = true,
        bool kestrel = false,
        int kestrelPort = 0
    )
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, $"sessions-{Guid.NewGuid():N}");
        var provider = new TestIdentityProvider();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Blokemon:DataDirectory", dataDirectory);
            if (withProvider)
            {
                builder.UseSetting(
                    IdentityConfigurationModule.providerEnabledKey(
                        TestIdentityProvider.ProviderName
                    ),
                    "true"
                );
                builder.ConfigureServices(services =>
                    services.AddSingleton<IIdentityProvider>(provider)
                );
            }

            if (kestrel)
            {
                // The shipped appsettings bind 127.0.0.1:5080; a test host takes any free port.
                builder.UseSetting("Urls", "http://127.0.0.1:0");
            }

            configure?.Invoke(builder);
        });
        if (kestrel && kestrelPort == 0)
        {
            factory.UseKestrel(0);
        }
        else if (kestrel)
        {
            // A check whose origin must be known before the host starts, as the passkey
            // relying party's must, names its own port; localhost binds both loopbacks so the
            // browser's own resolution of the name reaches it.
            factory.UseKestrel(options => options.ListenLocalhost(kestrelPort));
        }

        return new(factory, dataDirectory, provider);
    }

    public HttpClient Client(string? token = null)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token
            );
        }

        return client;
    }

    /// <summary>Signs a subject in through the provider double and the completion path.</summary>
    public async Task<IssuedSession> SignIn(
        string subject,
        string displayName = "Server Player",
        SessionProvenance provenance = SessionProvenance.FirstParty,
        TenantId? tenant = null
    )
    {
        var proof = $"proof:{subject}:{Guid.NewGuid():N}";
        Provider.Accept(proof, subject, displayName, provenance);
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider.GetRequiredService<SignInServices>();
        var registry = scope.ServiceProvider.GetRequiredService<IdentityProviderRegistry>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var target = tenant ?? await DefaultTenantId();
        var outcome = await SignInCompletion.signIn(
            services,
            registry,
            Provider.Name,
            proof,
            target,
            time.GetUtcNow(),
            CancellationToken.None
        );
        return outcome is DomainResult<IssuedSession, SignInFailure>.Succeeded issued
            ? issued.Value
            : throw new InvalidOperationException(
                $"Sign-in failed: {((DomainResult<IssuedSession, SignInFailure>.Failed)outcome).Error}"
            );
    }

    /// <summary>A session issued straight into the store, for expiry and revocation cases.</summary>
    public async Task<IssuedSession> IssueDirectly(
        AccountId account,
        DateTimeOffset now,
        TimeSpan lifetime,
        SessionProvenance provenance = SessionProvenance.FirstParty
    ) =>
        await WithStore(store =>
            Sessions.issue(
                store,
                account,
                DefaultTenantId().GetAwaiter().GetResult(),
                provenance,
                now,
                lifetime,
                CancellationToken.None
            )
        );

    public async Task<T> WithStore<T>(Func<StateDocumentStore, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<StateDocumentStore>());
    }

    public Task WithStore(Func<StateDocumentStore, Task> action) =>
        WithStore(async store =>
        {
            await action(store);
            return true;
        });

    public async Task<TenantDocument> DefaultTenant()
    {
        var found = await WithStore(store =>
            Tenants.findBySlug(store, store, Tenants.DefaultSlug, CancellationToken.None)
        );
        return found is { } some
            ? some.Value
            : throw new InvalidOperationException("The default tenant is missing.");
    }

    public async Task<TenantId> DefaultTenantId() =>
        TenantId.Create((await DefaultTenant()).Id)
            is DomainResult<TenantId, IdentityValueFailure>.Succeeded id
            ? id.Value
            : throw new InvalidOperationException("The default tenant id is malformed.");

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        if (Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}

/// <summary>
/// Lets the headless hosted-mode checks frame the test host from the parent page's origin. The
/// shipped host still answers with the interactive-server render mode's
/// <c>frame-ancestors 'self'</c> and the antiforgery middleware's <c>X-Frame-Options</c>;
/// BLOKEMON-155 replaces both with the per-tenant framing policy. Test assembly only.
/// </summary>
internal sealed class FramingAllowedForTests : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(
                static (context, pipeline) =>
                {
                    context.Response.OnStarting(() =>
                    {
                        context.Response.Headers.Remove("X-Frame-Options");
                        context.Response.Headers.Remove("Content-Security-Policy");
                        return Task.CompletedTask;
                    });
                    return pipeline(context);
                }
            );
            next(app);
        };
}

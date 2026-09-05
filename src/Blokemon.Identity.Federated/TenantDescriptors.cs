using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;

namespace Blokemon.Identity.Federated;

/// <summary>
/// The unauthenticated descriptor of a tenant, as <c>GET /api/tenant/{slug}</c> and the
/// channel's own <c>GET /api/tenant/self</c> state it: the core sign-in when configured, the
/// route a hand-off code for the tenant is exchanged at, and whether the first-party provider
/// offers passkeys (it always offers the simple login).
/// </summary>
public static class TenantDescriptors
{
    public static TenantDescriptorView Describe(
        TenantDocument tenant,
        IdentityProviderRegistry registry,
        IdentityConfiguration identity
    )
    {
        var core = IdentityProviderName.Create(CoreSignIn.ProviderName)
            is DomainResult<IdentityProviderName, ExternalIdentityFailure>.Succeeded name
            ? identity.Provider(name.Value)
            : null;
        return new(
            tenant.Id,
            tenant.Slug,
            tenant.DisplayLabel,
            registry.Enabled.Select(static name => name.Value).ToArray(),
            tenant.RegisteredParentOrigin,
            core?.CoreSignInUrl is { } url
                ? new CoreSignInView(CoreSignIn.Label, url.ToString())
                : null,
            HandoffExchange.ClientPath,
            identity.Passkeys is not null
        );
    }
}

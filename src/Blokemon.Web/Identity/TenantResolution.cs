using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Identity;

/// <summary>The tenant a page runs as: the slug it names, or the default tenant at the root.</summary>
internal static class TenantResolution
{
    public static readonly ApiError NotFound = new(
        "tenant.not_found",
        "That channel is not on this server."
    );

    public static async Task<TenantDocument?> Resolve(
        StateDocumentStore documents,
        string? slug,
        CancellationToken cancellationToken
    )
    {
        if (slug is null)
        {
            var found = await Tenants.findBySlug(
                documents,
                documents,
                Tenants.DefaultSlug,
                cancellationToken
            );
            return found?.Value;
        }

        if (
            TenantSlug.Create(slug)
            is not DomainResult<TenantSlug, TenantSlugFailure>.Succeeded parsed
        )
        {
            return null;
        }

        var tenant = await Tenants.findBySlug(
            documents,
            documents,
            parsed.Value,
            cancellationToken
        );
        return tenant?.Value;
    }

    public static TenantId IdOf(TenantDocument tenant) =>
        TenantId.Create(tenant.Id) is DomainResult<TenantId, IdentityValueFailure>.Succeeded id
            ? id.Value
            : throw new InvalidOperationException("A stored tenant id is malformed.");
}

using Blokemon.App;
using Blokemon.App.Client;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Application;

/// <summary>
/// Which tenant this client is running as, learned from <c>/t/{slug}</c> or, at the root, the
/// default tenant, and described by the server's unauthenticated descriptor.
/// </summary>
public sealed class TenantContext(SessionApiClient api)
{
    public TenantDescriptorView? Current { get; private set; }

    /// <summary>Resolves the tenant a slug names; null resolves the default tenant.</summary>
    public async Task<ApiResponse<TenantDescriptorView>> Resolve(
        string? slug,
        CancellationToken cancellationToken = default
    )
    {
        var wanted = slug ?? Tenants.DefaultSlug.Value;
        if (Current is { } current && string.Equals(current.Slug, wanted, StringComparison.Ordinal))
        {
            return new(true, current, null);
        }

        var response = await api.Descriptor(wanted, cancellationToken);
        if (response.Succeeded && response.Value is { } descriptor)
        {
            Current = descriptor;
        }

        return response;
    }
}

using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Identity.Federated;
using Blokemon.Product;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Api;

/// <summary>
/// The tenant owner's routes (BLOKEMON-152): the tenants the session owns, the tenant's own
/// approval records as identifiers, status and timestamps, and exclusion and readmission of an
/// account within that tenant. Owner authority is derived on every call by <see cref="Roles"/>
/// from the broadcaster link recorded at admission and the session's provenance; a refused
/// call mutates nothing.
/// </summary>
public static class TenantOwnerEndpoints
{
    public static readonly ApiError OwnerRequired = new(
        "owner.required",
        "Only the channel's owner can do this."
    );

    public static IEndpointRouteBuilder MapTenantOwnerEndpoints(
        this IEndpointRouteBuilder endpoints
    )
    {
        var group = endpoints.MapGroup("/api/owner");
        group.MapGet("/tenants", Owned);
        group.MapGet("/{tenantId}/approvals", Approvals);
        group.MapPost("/{tenantId}/accounts/{accountId}/exclude", Exclude);
        group.MapPost("/{tenantId}/accounts/{accountId}/readmit", Readmit);
        return endpoints;
    }

    private static async Task<ApiResponse<OwnedTenantView[]>> Owned(
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        var owned = await Roles.ownedTenants(
            documents,
            documents,
            BlokeBotProvider.LinkProvider,
            current.Session!,
            cancellationToken
        );
        return Envelope.Ok(
            owned
                .Select(static tenant => new OwnedTenantView(
                    tenant.Id,
                    tenant.Slug,
                    tenant.DisplayLabel
                ))
                .ToArray()
        );
    }

    private static async Task<ApiResponse<ApprovalSummaryView[]>> Approvals(
        string tenantId,
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        var owned = await OwnedTenant(tenantId, current, documents, cancellationToken);
        if (owned is null)
        {
            return Envelope.Fail<ApprovalSummaryView[]>(OwnerRequired);
        }

        var approvals = await Listings.approvalsOf(
            documents,
            Tenants.idOf(owned),
            cancellationToken
        );
        return Envelope.Ok(
            approvals
                .Select(static approval => new ApprovalSummaryView(
                    approval.Account,
                    approval.Status,
                    approval.ApprovedAt,
                    approval.ExcludedAt
                ))
                .ToArray()
        );
    }

    private static Task<ApiResponse<ExclusionView>> Exclude(
        string tenantId,
        string accountId,
        CurrentSession current,
        StateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    ) =>
        Sanction(
            tenantId,
            accountId,
            current,
            documents,
            (tenant, account) =>
                Exclusions.exclude(
                    documents,
                    documents,
                    account,
                    tenant,
                    time.GetUtcNow(),
                    cancellationToken
                ),
            excluded: true,
            cancellationToken
        );

    private static Task<ApiResponse<ExclusionView>> Readmit(
        string tenantId,
        string accountId,
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    ) =>
        Sanction(
            tenantId,
            accountId,
            current,
            documents,
            (tenant, account) => Exclusions.readmit(documents, account, tenant, cancellationToken),
            excluded: false,
            cancellationToken
        );

    private static async Task<ApiResponse<ExclusionView>> Sanction(
        string tenantId,
        string accountId,
        CurrentSession current,
        StateDocumentStore documents,
        Func<TenantId, AccountId, Task<DomainResult<Microsoft.FSharp.Core.Unit, ApiError>>> act,
        bool excluded,
        CancellationToken cancellationToken
    )
    {
        var owned = await OwnedTenant(tenantId, current, documents, cancellationToken);
        if (owned is null)
        {
            return Envelope.Fail<ExclusionView>(OwnerRequired);
        }

        if (
            AccountId.Create(accountId)
            is not DomainResult<AccountId, IdentityValueFailure>.Succeeded account
        )
        {
            return Envelope.Fail<ExclusionView>(
                new("account.not_found", "That account is not on this server.")
            );
        }

        var outcome = await act(Tenants.idOf(owned), account.Value);
        return outcome.IsSucceeded
            ? Envelope.Ok(new ExclusionView(account.Value.Value, excluded))
            : Envelope.Fail<ExclusionView>(
                ((DomainResult<Microsoft.FSharp.Core.Unit, ApiError>.Failed)outcome).Error
            );
    }

    /// <summary>The tenant, when it exists and the session holds owner authority in it.</summary>
    private static async Task<TenantDocument?> OwnedTenant(
        string tenantId,
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        if (
            TenantId.Create(tenantId)
            is not DomainResult<TenantId, IdentityValueFailure>.Succeeded id
        )
        {
            return null;
        }

        var tenant = await Tenants.read(documents, id.Value, cancellationToken);
        if (tenant is not { Value: { } found })
        {
            return null;
        }

        var owner = await Roles.isOwner(
            documents,
            BlokeBotProvider.LinkProvider,
            current.Session!,
            found,
            cancellationToken
        );
        return owner ? found : null;
    }
}

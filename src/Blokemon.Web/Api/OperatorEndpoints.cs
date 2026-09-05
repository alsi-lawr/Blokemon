using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Api;

/// <summary>
/// The operator's account and tenant lifecycle routes (BLOKEMON-152): the listings over the
/// store's summary projection, the sign-in diagnostics, disable and enable, erase on behalf,
/// the operator grant (from a first-party session only) and the default tenant's owner. The
/// operator's admission, rotation, closure and revocation of tenants are the federation's
/// routes under the same prefix. Session-required; the role is derived on every call from the
/// account record, and a refused call mutates nothing.
/// </summary>
public static class OperatorEndpoints
{
    public static readonly ApiError OperatorRequired = new(
        "operator.required",
        "Only an operator can see this."
    );

    public static readonly ApiError FirstPartyRequired = new(
        "operator.provenance",
        "Granting operator needs a first-party sign-in."
    );

    public static IEndpointRouteBuilder MapOperatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/operator");
        group.MapGet("/accounts", Accounts);
        group.MapGet("/tenants", Tenants);
        group.MapGet("/diagnostics", Diagnostics);
        group.MapPost("/accounts/{accountId}/disable", Disable);
        group.MapPost("/accounts/{accountId}/enable", Enable);
        group.MapPost("/accounts/{accountId}/erase", Erase);
        group.MapPost("/accounts/{accountId}/grant-operator", GrantOperator);
        group.MapPost("/tenants/{tenantId}/owner", AssignOwner);
        return endpoints;
    }

    private static async Task<ApiResponse<AccountSummaryView[]>> Accounts(
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return Envelope.Fail<AccountSummaryView[]>(OperatorRequired);
        }

        var accounts = await Listings.accounts(documents, cancellationToken);
        return Envelope.Ok(
            accounts
                .Select(static account => new AccountSummaryView(
                    account.Id,
                    account.Status,
                    account.CreatedAt,
                    account.ErasedAt
                ))
                .ToArray()
        );
    }

    private static async Task<ApiResponse<TenantSummaryView[]>> Tenants(
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return Envelope.Fail<TenantSummaryView[]>(OperatorRequired);
        }

        var tenants = await Listings.tenants(documents, cancellationToken);
        return Envelope.Ok(
            tenants
                .Select(static tenant => new TenantSummaryView(
                    tenant.Id,
                    tenant.Status,
                    tenant.CreatedAt
                ))
                .ToArray()
        );
    }

    private static async Task<ApiResponse<SignInDiagnosticsView>> Diagnostics(
        CurrentSession current,
        StateDocumentStore documents,
        SignInDiagnostics diagnostics,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return Envelope.Fail<SignInDiagnosticsView>(OperatorRequired);
        }

        return Envelope.Ok(
            new SignInDiagnosticsView(
                diagnostics.Since,
                diagnostics
                    .Outcomes.Select(static outcome => new SignInOutcomeView(
                        outcome.Code,
                        outcome.Count
                    ))
                    .ToArray()
            )
        );
    }

    private static Task<ApiResponse<AccountLifecycleView>> Disable(
        string accountId,
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    ) =>
        Change(
            accountId,
            current,
            documents,
            account => AccountLifecycle.disable(documents, documents, account, cancellationToken),
            cancellationToken
        );

    private static Task<ApiResponse<AccountLifecycleView>> Enable(
        string accountId,
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    ) =>
        Change(
            accountId,
            current,
            documents,
            account => AccountLifecycle.enable(documents, account, cancellationToken),
            cancellationToken
        );

    private static async Task<ApiResponse<AccountLifecycleView>> GrantOperator(
        string accountId,
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return Envelope.Fail<AccountLifecycleView>(OperatorRequired);
        }

        if (current.Session!.Provenance != SessionProvenance.FirstParty)
        {
            return Envelope.Fail<AccountLifecycleView>(FirstPartyRequired);
        }

        return await Change(
            accountId,
            current,
            documents,
            account => AccountLifecycle.grantOperator(documents, account, cancellationToken),
            cancellationToken
        );
    }

    private static async Task<ApiResponse<AccountErasedView>> Erase(
        string accountId,
        CurrentSession current,
        StateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return Envelope.Fail<AccountErasedView>(OperatorRequired);
        }

        if (
            AccountId.Create(accountId)
            is not DomainResult<AccountId, IdentityValueFailure>.Succeeded account
        )
        {
            return Envelope.Fail<AccountErasedView>(
                AccountErasure.toError(ErasureRefusal.NotFound)
            );
        }

        var erased = await AccountErasure.erase(
            documents,
            documents,
            account.Value,
            time.GetUtcNow(),
            cancellationToken
        );
        return erased is DomainResult<Erasure, ErasureRefusal>.Succeeded erasure
            ? Envelope.Ok(new AccountErasedView(erasure.Value.ErasedAt, erasure.Value.Repeated))
            : Envelope.Fail<AccountErasedView>(
                AccountErasure.toError(((DomainResult<Erasure, ErasureRefusal>.Failed)erased).Error)
            );
    }

    private static async Task<ApiResponse<TenantOwnerView>> AssignOwner(
        string tenantId,
        OwnerAssignmentRequest request,
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return Envelope.Fail<TenantOwnerView>(OperatorRequired);
        }

        if (
            TenantId.Create(tenantId)
            is not DomainResult<TenantId, IdentityValueFailure>.Succeeded tenant
        )
        {
            return Envelope.Fail<TenantOwnerView>(
                AccountLifecycle.toError(LifecycleFailure.TenantNotFound)
            );
        }

        if (
            AccountId.Create(request.AccountId)
            is not DomainResult<AccountId, IdentityValueFailure>.Succeeded account
        )
        {
            return Envelope.Fail<TenantOwnerView>(
                AccountLifecycle.toError(LifecycleFailure.NotFound)
            );
        }

        var assigned = await AccountLifecycle.assignOwner(
            documents,
            tenant.Value,
            account.Value,
            cancellationToken
        );
        return assigned is DomainResult<TenantDocument, LifecycleFailure>.Succeeded document
            ? Envelope.Ok(new TenantOwnerView(document.Value.Id, document.Value.OwnerAccount!))
            : Envelope.Fail<TenantOwnerView>(
                AccountLifecycle.toError(
                    ((DomainResult<TenantDocument, LifecycleFailure>.Failed)assigned).Error
                )
            );
    }

    private static async Task<ApiResponse<AccountLifecycleView>> Change(
        string accountId,
        CurrentSession current,
        StateDocumentStore documents,
        Func<AccountId, Task<DomainResult<AccountDocument, LifecycleFailure>>> change,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return Envelope.Fail<AccountLifecycleView>(OperatorRequired);
        }

        if (
            AccountId.Create(accountId)
            is not DomainResult<AccountId, IdentityValueFailure>.Succeeded account
        )
        {
            return Envelope.Fail<AccountLifecycleView>(
                AccountLifecycle.toError(LifecycleFailure.NotFound)
            );
        }

        var changed = await change(account.Value);
        return changed is DomainResult<AccountDocument, LifecycleFailure>.Succeeded document
            ? Envelope.Ok(
                new AccountLifecycleView(
                    document.Value.Id,
                    document.Value.Status.ToString(),
                    document.Value.Operator
                )
            )
            : Envelope.Fail<AccountLifecycleView>(
                AccountLifecycle.toError(
                    ((DomainResult<AccountDocument, LifecycleFailure>.Failed)changed).Error
                )
            );
    }

    private static Task<bool> IsOperator(
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    ) =>
        current.Session is { } session
            ? Roles.isOperator(documents, session, cancellationToken)
            : Task.FromResult(false);
}

using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Blokemon.Identity.Federated;

/// <summary>
/// The operator's admission of channels: mint (a channel under a fresh slug, or the default
/// tenant's core issuer under the default slug), rotate (which re-admits a closed tenant),
/// close and revoke. Session-required, and refused for any session whose account is not an
/// operator; a refusal mutates nothing.
/// </summary>
public static class OperatorTenantEndpoints
{
    public static readonly ApiError OperatorRequired = new(
        "operator.required",
        "Only an operator can admit, rotate, close or revoke a channel."
    );

    public static void Map(RouteGroupBuilder operatorGroup)
    {
        var tenants = operatorGroup.MapGroup("/tenants");
        tenants.MapPost("/", Admit);
        tenants.MapPost("/{tenantId}/rotate", Rotate);
        tenants.MapPost("/{tenantId}/close", Close);
        tenants.MapPost("/{tenantId}/revoke", Revoke);
    }

    private static async Task<ApiResponse<AdmittedTenantView>> Admit(
        TenantAdmissionRequest request,
        CurrentSession current,
        IStateDocumentStore documents,
        IDocumentListing listing,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return ChannelCalls.Fail<AdmittedTenantView>(OperatorRequired);
        }

        if (
            !string.IsNullOrWhiteSpace(request.BroadcasterTwitchUserId)
            && TwitchSubjects.Parse(request.BroadcasterTwitchUserId)
                is DomainResult<ExternalSubject, ApiError>.Failed malformed
        )
        {
            return ChannelCalls.Fail<AdmittedTenantView>(
                new("tenant.subject", malformed.Error.Message)
            );
        }

        var admitted = await TenantAdmission.admit(
            documents,
            listing,
            request.Slug,
            request.Label,
            request.BroadcasterTwitchUserId,
            request.ParentOrigin,
            time.GetUtcNow(),
            cancellationToken
        );
        return Admitted(admitted);
    }

    private static async Task<ApiResponse<AdmittedTenantView>> Rotate(
        string tenantId,
        CurrentSession current,
        IStateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return ChannelCalls.Fail<AdmittedTenantView>(OperatorRequired);
        }

        if (
            TenantId.Create(tenantId)
            is not DomainResult<TenantId, IdentityValueFailure>.Succeeded id
        )
        {
            return ChannelCalls.Fail<AdmittedTenantView>(
                TenantAdmission.toError(AdmissionFailure.NotFound)
            );
        }

        return Admitted(
            await TenantAdmission.rotate(documents, id.Value, time.GetUtcNow(), cancellationToken)
        );
    }

    private static Task<ApiResponse<TenantStatusView>> Close(
        string tenantId,
        CurrentSession current,
        IStateDocumentStore documents,
        IDocumentListing listing,
        CancellationToken cancellationToken
    ) =>
        End(
            tenantId,
            current,
            documents,
            id => TenantAdmission.close(documents, listing, id, cancellationToken),
            cancellationToken
        );

    private static Task<ApiResponse<TenantStatusView>> Revoke(
        string tenantId,
        CurrentSession current,
        IStateDocumentStore documents,
        IDocumentListing listing,
        CancellationToken cancellationToken
    ) =>
        End(
            tenantId,
            current,
            documents,
            id => TenantAdmission.revoke(documents, listing, id, cancellationToken),
            cancellationToken
        );

    private static async Task<ApiResponse<TenantStatusView>> End(
        string tenantId,
        CurrentSession current,
        IStateDocumentStore documents,
        Func<TenantId, Task<DomainResult<TenantDocument, AdmissionFailure>>> action,
        CancellationToken cancellationToken
    )
    {
        if (!await IsOperator(current, documents, cancellationToken))
        {
            return ChannelCalls.Fail<TenantStatusView>(OperatorRequired);
        }

        if (
            TenantId.Create(tenantId)
            is not DomainResult<TenantId, IdentityValueFailure>.Succeeded id
        )
        {
            return ChannelCalls.Fail<TenantStatusView>(
                TenantAdmission.toError(AdmissionFailure.NotFound)
            );
        }

        var ended = await action(id.Value);
        return ended is DomainResult<TenantDocument, AdmissionFailure>.Succeeded document
            ? ChannelCalls.Ok(
                new TenantStatusView(
                    document.Value.Id,
                    document.Value.Slug,
                    document.Value.Status.ToString()
                )
            )
            : ChannelCalls.Fail<TenantStatusView>(
                TenantAdmission.toError(
                    ((DomainResult<TenantDocument, AdmissionFailure>.Failed)ended).Error
                )
            );
    }

    private static ApiResponse<AdmittedTenantView> Admitted(
        DomainResult<AdmittedTenant, AdmissionFailure> outcome
    ) =>
        outcome is DomainResult<AdmittedTenant, AdmissionFailure>.Succeeded admitted
            ? ChannelCalls.Ok(
                new AdmittedTenantView(
                    admitted.Value.Tenant.Id,
                    admitted.Value.Tenant.Slug,
                    admitted.Value.Tenant.DisplayLabel,
                    admitted.Value.Tenant.Status.ToString(),
                    admitted.Value.Token,
                    admitted.Value.Tenant.IntegrationTokenVerifier!.IssuedAt
                )
            )
            : ChannelCalls.Fail<AdmittedTenantView>(
                TenantAdmission.toError(
                    ((DomainResult<AdmittedTenant, AdmissionFailure>.Failed)outcome).Error
                )
            );

    private static Task<bool> IsOperator(
        CurrentSession current,
        IStateDocumentStore documents,
        CancellationToken cancellationToken
    ) =>
        current.Session is { } session
            ? Accounts.isOperator(documents, session.Account, cancellationToken)
            : Task.FromResult(false);
}

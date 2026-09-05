using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Api;

/// <summary>
/// The person's approval of channels for their own account: the pending list, and the grant.
/// Session-required; the rule for which sessions may grant is <see cref="IssuerAdmission"/>'s,
/// and a <c>Recovery</c> session never reaches here.
/// </summary>
public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var approvals = endpoints.MapGroup("/api/session/approvals");
        approvals.MapGet("/", Pending);
        approvals.MapPost("/{tenantId}", Approve);
        return endpoints;
    }

    private static async Task<ApiResponse<PendingApprovalView[]>> Pending(
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        var session = current.Session!;
        var pending = await IssuerAdmission.pending(
            documents,
            documents,
            session.Account,
            cancellationToken
        );
        return Envelope.Ok(
            pending
                .Select(static item => new PendingApprovalView(
                    item.Tenant.Id,
                    item.Tenant.Slug,
                    item.Tenant.DisplayLabel
                ))
                .ToArray()
        );
    }

    private static async Task<ApiResponse<ApprovalView>> Approve(
        string tenantId,
        CurrentSession current,
        StateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (
            TenantId.Create(tenantId)
            is not DomainResult<TenantId, IdentityValueFailure>.Succeeded id
        )
        {
            return Envelope.Fail<ApprovalView>(
                IssuerAdmission.toError(ApprovalRefusal.TenantNotFound)
            );
        }

        var now = time.GetUtcNow();
        var approved = await IssuerAdmission.approve(
            documents,
            documents,
            current.Session!,
            id.Value,
            now,
            cancellationToken
        );
        return approved.IsSucceeded
            ? Envelope.Ok(new ApprovalView(now))
            : Envelope.Fail<ApprovalView>(
                IssuerAdmission.toError(
                    (
                        (DomainResult<Microsoft.FSharp.Core.Unit, ApprovalRefusal>.Failed)approved
                    ).Error
                )
            );
    }
}

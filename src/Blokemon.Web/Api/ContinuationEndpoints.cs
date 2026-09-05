using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Identity;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Api;

/// <summary>
/// A session's continuation to a top-level window: <c>POST /api/session/continue</c>
/// (session-required) mints a code of kind <c>Continuation</c> bound to the session's account,
/// tenant and provenance; <c>POST /api/session/resume</c> (anonymous) exchanges it, once, for a
/// session with the same three. A hand-off code is refused here and a continuation code at
/// the hand-off exchange: the kind is in the record, not the code.
/// </summary>
public static class ContinuationEndpoints
{
    public static IEndpointRouteBuilder MapContinuationEndpoints(
        this IEndpointRouteBuilder endpoints
    )
    {
        endpoints.MapPost("/api/session/continue", Continue);
        endpoints.MapPost("/api/session/resume", Resume);
        return endpoints;
    }

    private static async Task<ApiResponse<ContinuationView>> Continue(
        CurrentSession current,
        StateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var session = current.Session!;
        var tenant = await Tenants.read(documents, session.Tenant, cancellationToken);
        if (tenant is not { Value: { } found })
        {
            return Envelope.Fail<ContinuationView>(TenantResolution.NotFound);
        }

        var now = time.GetUtcNow();
        await HandoffCodes.sweep(documents, documents, now, cancellationToken);
        var issued = await HandoffCodes.mint(
            documents,
            HandoffBinding.NewContinuation(session),
            now,
            cancellationToken
        );
        return Envelope.Ok(
            new ContinuationView(issued.Code, $"/t/{found.Slug}/continue", issued.ExpiresAt)
        );
    }

    private static async Task<ApiResponse<IssuedSessionView>> Resume(
        SessionExchangeRequest request,
        StateDocumentStore documents,
        IdentityConfiguration identity,
        ServerApplications applications,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var tenant = await TenantResolution.Resolve(documents, request.Slug, cancellationToken);
        if (tenant is null)
        {
            return Envelope.Fail<IssuedSessionView>(TenantResolution.NotFound);
        }

        var now = time.GetUtcNow();
        var consumed = await HandoffCodes.consume(
            documents,
            request.Code,
            HandoffKind.Continuation,
            TenantResolution.IdOf(tenant),
            now,
            cancellationToken
        );
        if (consumed is not DomainResult<HandoffDocument, HandoffFailure>.Succeeded code)
        {
            return Envelope.Fail<IssuedSessionView>(
                HandoffCodes.toError(
                    ((DomainResult<HandoffDocument, HandoffFailure>.Failed)consumed).Error
                )
            );
        }

        if (
            AccountId.Create(code.Value.Account)
                is not DomainResult<AccountId, IdentityValueFailure>.Succeeded account
            || !code.Value.Provenance.HasValue
            || !await Accounts.isActive(documents, account.Value, cancellationToken)
        )
        {
            return Envelope.Fail<IssuedSessionView>(SessionFailures.required());
        }

        var issued = await Sessions.issue(
            documents,
            account.Value,
            TenantResolution.IdOf(tenant),
            code.Value.Provenance.Value,
            now,
            identity.SessionLifetime,
            cancellationToken
        );
        return Envelope.Ok(await SessionViews.Describe(issued, applications, cancellationToken));
    }
}

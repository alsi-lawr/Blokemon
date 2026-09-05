using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Identity.Federated;
using Blokemon.Product;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Api;

/// <summary>
/// The person's own account routes (BLOKEMON-152): self-service erasure, permitted from a
/// first-party session or an Issuer session of the default tenant and refused from a channel
/// tenant's Issuer session (a channel can never erase an account, D-043); and the roles the
/// session holds now, derived on this call and never stored on the session. A Recovery session
/// is refused by the session policy before either runs.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/session/erase", Erase);
        endpoints.MapGet("/api/session/roles", Roles);
        return endpoints;
    }

    private static async Task<ApiResponse<AccountErasedView>> Erase(
        CurrentSession current,
        StateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var session = current.Session!;
        if (!await AccountErasure.maySelfErase(documents, session, cancellationToken))
        {
            return Envelope.Fail<AccountErasedView>(
                AccountErasure.toError(ErasureRefusal.ChannelSession)
            );
        }

        var erased = await AccountErasure.erase(
            documents,
            documents,
            session.Account,
            time.GetUtcNow(),
            cancellationToken
        );
        return erased is DomainResult<Erasure, ErasureRefusal>.Succeeded erasure
            ? Envelope.Ok(new AccountErasedView(erasure.Value.ErasedAt, erasure.Value.Repeated))
            : Envelope.Fail<AccountErasedView>(
                AccountErasure.toError(((DomainResult<Erasure, ErasureRefusal>.Failed)erased).Error)
            );
    }

    private static async Task<ApiResponse<SessionRolesView>> Roles(
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        var session = current.Session!;
        var operatorRole = await App.Roles.isOperator(documents, session, cancellationToken);
        var owned = await App.Roles.ownedTenants(
            documents,
            documents,
            BlokeBotProvider.LinkProvider,
            session,
            cancellationToken
        );
        return Envelope.Ok(
            new SessionRolesView(
                operatorRole,
                owned
                    .Select(static tenant => new OwnedTenantView(
                        tenant.Id,
                        tenant.Slug,
                        tenant.DisplayLabel
                    ))
                    .ToArray()
            )
        );
    }
}

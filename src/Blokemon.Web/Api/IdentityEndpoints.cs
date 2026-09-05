using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Identity.Federated;
using Blokemon.Product;
using Blokemon.Web.Identity;
using Blokemon.Web.Identity.Google;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Api;

/// <summary>
/// The session, tenant and operator routes of BLOKEMON-149: sign-out, the tenant descriptor
/// and the operator bootstrap. The provider ceremonies and exchanges are the first-party
/// routes and the federation's.
/// </summary>
public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");
        api.MapPost(
            "/session/signout",
            static async (
                CurrentSession current,
                StateDocumentStore documents,
                TimeProvider time,
                CancellationToken cancellationToken
            ) =>
            {
                if (current.Session is not { } session)
                {
                    return new ApiResponse<SignOutView>(false, null, SessionFailures.required());
                }

                await Sessions.revoke(documents, session.Id, cancellationToken);
                return new ApiResponse<SignOutView>(true, new(time.GetUtcNow()), null);
            }
        );
        api.MapGet(
            "/tenant/{slug}",
            static async (
                string slug,
                StateDocumentStore documents,
                IdentityProviderRegistry registry,
                IdentityConfiguration identity,
                CancellationToken cancellationToken
            ) =>
            {
                var tenant = TenantSlug.Create(slug)
                    is DomainResult<TenantSlug, TenantSlugFailure>.Succeeded parsed
                    ? await Tenants.findBySlug(
                        documents,
                        documents,
                        parsed.Value,
                        cancellationToken
                    )
                    : null;
                return tenant is { Value: { } found }
                    ? new ApiResponse<TenantDescriptorView>(
                        true,
                        TenantDescriptors.Describe(found, registry, identity) with
                        {
                            SignInLinks = GoogleSignIn.Links(registry, found.Slug),
                        },
                        null
                    )
                    : new ApiResponse<TenantDescriptorView>(
                        false,
                        null,
                        new("tenant.not_found", "That channel is not on this server.")
                    );
            }
        );
        api.MapPost(
            "/operator/bootstrap",
            static async (
                OperatorBootstrapRequest request,
                HttpContext context,
                CurrentSession current,
                StateDocumentStore documents,
                IdentityConfiguration identity,
                ClientLockouts lockouts,
                TimeProvider time,
                CancellationToken cancellationToken
            ) =>
            {
                if (current.Session is not { } session)
                {
                    return new ApiResponse<OperatorBootstrapView>(
                        false,
                        null,
                        SessionFailures.required()
                    );
                }

                var now = time.GetUtcNow();
                var client = ClientLockouts.ClientOf(context);
                if (lockouts.OperatorBootstrap.IsLockedOut(client, now))
                {
                    return new ApiResponse<OperatorBootstrapView>(
                        false,
                        null,
                        OperatorBootstrap.locked()
                    );
                }

                var redeemed = await OperatorBootstrap.redeem(
                    documents,
                    identity.OperatorBootstrapCode,
                    session,
                    request.Code,
                    now,
                    cancellationToken
                );
                if (
                    redeemed
                    is DomainResult<DateTimeOffset, OperatorBootstrapFailure>.Succeeded success
                )
                {
                    return new ApiResponse<OperatorBootstrapView>(true, new(success.Value), null);
                }

                var failure = (
                    (DomainResult<DateTimeOffset, OperatorBootstrapFailure>.Failed)redeemed
                ).Error;
                if (failure.IsRefused)
                {
                    lockouts.OperatorBootstrap.RecordFailure(client, now);
                }

                return new ApiResponse<OperatorBootstrapView>(
                    false,
                    null,
                    OperatorBootstrap.toError(failure)
                );
            }
        );
        return endpoints;
    }
}

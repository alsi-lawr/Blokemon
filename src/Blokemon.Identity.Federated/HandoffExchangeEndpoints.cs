using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Blokemon.Identity.Federated;

/// <summary>
/// <c>POST /api/session/blokebot</c>: the anonymous exchange of a hand-off code for an
/// <c>Issuer</c> session whose tenant is the code's binding. The page states the tenant it runs
/// as; a code bound to another is refused and nothing is consumed. An existing account the
/// channel is not yet approved for is recorded pending and answered with the typed
/// <c>approval.pending</c> outcome rather than a session.
/// </summary>
public static class HandoffExchangeEndpoints
{
    public static readonly ApiError Unavailable = new(
        "provider.unavailable",
        "That way of signing in is not enabled."
    );

    public static readonly ApiError ApprovalPending = new(
        "approval.pending",
        "Confirm from a channel you already play in, or sign in with your own password, passkey or Google account."
    );

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(HandoffExchange.Route, Exchange);

    private static async Task<ApiResponse<IssuedSessionView>> Exchange(
        SessionExchangeRequest request,
        IStateDocumentStore documents,
        IDocumentListing listing,
        IdentityProviderRegistry registry,
        SignInServices services,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (registry.Find(Name(BlokeBotProvider.ProviderName)) is not BlokeBotProvider provider)
        {
            return ChannelCalls.Fail<IssuedSessionView>(Unavailable);
        }

        var slug = TenantSlug.Create(request.Slug ?? Tenants.DefaultSlug.Value);
        var page = slug is DomainResult<TenantSlug, TenantSlugFailure>.Succeeded parsed
            ? await Tenants.findBySlug(documents, listing, parsed.Value, cancellationToken)
            : null;
        if (page is not { Value: { } routed })
        {
            return ChannelCalls.Fail<IssuedSessionView>(
                new("tenant.not_found", "That channel is not on this server.")
            );
        }

        // A code that would create an account needs the person's acceptance of the current
        // terms, and the code is single-use: it is read, not spent, to find out, so the client
        // can show the terms and exchange the same code once they are accepted.
        if (!Terms.accepted(request.AcceptedTerms))
        {
            var peeked = await HandoffCodes.peek(
                documents,
                request.Code,
                HandoffKind.Channel,
                Tenants.idOf(routed),
                time.GetUtcNow(),
                cancellationToken
            );
            if (
                peeked
                    is DomainResult<HandoffDocument, HandoffFailure>.Succeeded
                    {
                        Value: { Subject: { } subject },
                    }
                && ExternalSubject.Create(subject)
                    is DomainResult<
                        ExternalSubject,
                        ExternalIdentityFailure
                    >.Succeeded parsedSubject
                && await IssuerAdmission.wouldCreateAccount(
                    documents,
                    BlokeBotProvider.LinkProvider,
                    parsedSubject.Value,
                    cancellationToken
                )
            )
            {
                return ChannelCalls.Fail<IssuedSessionView>(Terms.required);
            }
        }

        var exchanged = await provider.Exchange(
            documents,
            request.Code,
            Tenants.idOf(routed),
            cancellationToken
        );
        if (
            exchanged
            is not DomainResult<
                (VerifiedIdentity Identity, HandoffDocument Handoff),
                SignInFailure
            >.Succeeded success
        )
        {
            return ChannelCalls.Fail<IssuedSessionView>(
                SignInFailures.toError(
                    (
                        (DomainResult<
                            (VerifiedIdentity, HandoffDocument),
                            SignInFailure
                        >.Failed)exchanged
                    ).Error
                )
            );
        }

        // The tenant is re-read after consumption: a channel closed between mint and
        // exchange refuses the sign-in.
        var tenant = await Tenants.read(documents, Tenants.idOf(routed), cancellationToken);
        if (tenant is not { Value: { } current })
        {
            return ChannelCalls.Fail<IssuedSessionView>(
                new("tenant.not_found", "That channel is not on this server.")
            );
        }

        var admitted = await IssuerAdmission.admit(
            services,
            listing,
            success.Value.Identity,
            current,
            request.AcceptedTerms,
            time.GetUtcNow(),
            cancellationToken
        );
        return admitted switch
        {
            DomainResult<HandoffOutcome, SignInFailure>.Succeeded
            {
                Value: HandoffOutcome.Admitted issued,
            } => ChannelCalls.Ok(
                new IssuedSessionView(
                    issued.Item.Token,
                    issued.Item.Session.ExpiresAt,
                    await DisplayName(services, issued.Item.Session, cancellationToken)
                )
            ),
            DomainResult<HandoffOutcome, SignInFailure>.Succeeded =>
                ChannelCalls.Fail<IssuedSessionView>(ApprovalPending),
            DomainResult<HandoffOutcome, SignInFailure>.Failed failed =>
                ChannelCalls.Fail<IssuedSessionView>(SignInFailures.toError(failed.Error)),
            _ => throw new InvalidOperationException("An outcome is one of the two."),
        };
    }

    private static async Task<string?> DisplayName(
        SignInServices services,
        Session session,
        CancellationToken cancellationToken
    )
    {
        var principal = ApplicationPrincipal.NewAccount(session.Account, session.Tenant);
        var application = new LocalApplicationService(
            services.Catalogue,
            services.Documents,
            principal,
            new LocalMatchService(
                services.Catalogue,
                services.Documents,
                PlayerDocumentKeysModule.ofPrincipal(principal)
            ),
            services.Economy,
            ProfileAuthorityPolicy.Preserve
        );
        var state = await application.State(cancellationToken);
        return state.Value?.Profile?.DisplayName;
    }

    private static IdentityProviderName Name(string name) =>
        IdentityProviderName.Create(name)
            is DomainResult<IdentityProviderName, ExternalIdentityFailure>.Succeeded parsed
            ? parsed.Value
            : throw new InvalidOperationException("The provider name is well formed.");
}

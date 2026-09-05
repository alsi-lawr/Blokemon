using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Microsoft.Extensions.DependencyInjection;

namespace Blokemon.Identity.Federated;

/// <summary>
/// The <c>blokebot</c> provider: a viewer is who the admitted channel says they are, carried
/// in a hand-off code the channel minted. The identities it asserts are linked under the
/// <c>twitch</c> provider name, since a Twitch user is the same person whichever channel hands
/// them off, and every session it produces carries <c>Issuer</c> provenance.
/// </summary>
public sealed class BlokeBotProvider(IServiceScopeFactory scopes, TimeProvider time)
    : IIdentityProvider
{
    public const string ProviderName = "blokebot";

    /// <summary>The provider name a viewer's link is recorded under.</summary>
    public static readonly IdentityProviderName LinkProvider = ProviderNamed("twitch");

    public IdentityProviderName Name { get; } = ProviderNamed(ProviderName);

    /// <summary>
    /// Consumes a hand-off code bound to the tenant and asserts the viewer it named. The code's
    /// binding is the tenant of the token that minted it; a page running as another tenant is
    /// refused before anything is consumed.
    /// </summary>
    public async Task<
        DomainResult<(VerifiedIdentity Identity, HandoffDocument Handoff), SignInFailure>
    > Exchange(
        IStateDocumentStore documents,
        string? code,
        TenantId tenant,
        CancellationToken cancellationToken
    )
    {
        var consumed = await HandoffCodes.consume(
            documents,
            code,
            HandoffKind.Channel,
            tenant,
            time.GetUtcNow(),
            cancellationToken
        );
        if (consumed is not DomainResult<HandoffDocument, HandoffFailure>.Succeeded handoff)
        {
            return Refused(
                HandoffCodes.toError(
                    ((DomainResult<HandoffDocument, HandoffFailure>.Failed)consumed).Error
                )
            );
        }

        var subject = TwitchSubjects.Parse(handoff.Value.Subject);
        if (subject is not DomainResult<ExternalSubject, ApiError>.Succeeded parsed)
        {
            return Refused(((DomainResult<ExternalSubject, ApiError>.Failed)subject).Error);
        }

        var identity = new VerifiedIdentity(
            LinkProvider,
            parsed.Value,
            handoff.Value.DisplayNameHint,
            SessionProvenance.Issuer
        );
        return DomainResult<(VerifiedIdentity, HandoffDocument), SignInFailure>.NewSucceeded(
            (identity, handoff.Value)
        );
    }

    /// <summary>
    /// The registry's shape of the exchange, for a code whose tenant is the default one; the
    /// exchange endpoint uses <see cref="Exchange"/>, which also states the page's tenant.
    /// </summary>
    public async Task<DomainResult<VerifiedIdentity, SignInFailure>> Verify(
        string proof,
        CancellationToken cancellationToken
    )
    {
        using var scope = scopes.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IStateDocumentStore>();
        var tenant = await Tenants.findBySlug(
            documents,
            scope.ServiceProvider.GetRequiredService<IDocumentListing>(),
            Tenants.DefaultSlug,
            cancellationToken
        );
        if (tenant is not { Value: { } found })
        {
            return DomainResult<VerifiedIdentity, SignInFailure>.NewFailed(SignInFailure.Damaged);
        }

        var exchanged = await Exchange(documents, proof, Tenants.idOf(found), cancellationToken);
        return
            exchanged
                is DomainResult<
                    (VerifiedIdentity Identity, HandoffDocument),
                    SignInFailure
                >.Succeeded success
            ? DomainResult<VerifiedIdentity, SignInFailure>.NewSucceeded(success.Value.Identity)
            : DomainResult<VerifiedIdentity, SignInFailure>.NewFailed(
                (
                    (DomainResult<
                        (VerifiedIdentity, HandoffDocument),
                        SignInFailure
                    >.Failed)exchanged
                ).Error
            );
    }

    private static DomainResult<(VerifiedIdentity, HandoffDocument), SignInFailure> Refused(
        ApiError error
    ) =>
        DomainResult<(VerifiedIdentity, HandoffDocument), SignInFailure>.NewFailed(
            SignInFailure.NewProviderRefused(error)
        );

    private static IdentityProviderName ProviderNamed(string name) =>
        IdentityProviderName.Create(name)
            is DomainResult<IdentityProviderName, ExternalIdentityFailure>.Succeeded parsed
            ? parsed.Value
            : throw new InvalidOperationException("The provider name is well formed.");
}

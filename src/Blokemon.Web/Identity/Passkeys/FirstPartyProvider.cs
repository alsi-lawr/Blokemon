using System.Text.Json;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Blokemon.Web.Identity.Passkeys;

/// <summary>
/// The <c>firstparty</c> provider: its proof is a passkey assertion for a pending sign-in, and
/// its subject is the account id the assertion's user handle names. The assertion is verified
/// against the account's stored credential and the sign count recorded, then the ordinary
/// completion path issues a <c>FirstParty</c> session.
/// </summary>
internal sealed class FirstPartyProvider(
    PasskeyCeremonies ceremonies,
    PasskeyChallenges challenges,
    IDbContextFactory<BlokemonDbContext> contexts
) : IIdentityProvider
{
    public IdentityProviderName Name => IdentityConfigurationModule.FirstPartyProvider;

    /// <summary>The proof the authenticate route hands the completion path: the ceremony request as JSON.</summary>
    public static string Proof(PasskeyCeremonyRequest request) =>
        JsonSerializer.Serialize(request, JsonSerializerOptions.Web);

    public async Task<DomainResult<VerifiedIdentity, SignInFailure>> Verify(
        string proof,
        CancellationToken cancellationToken
    )
    {
        PasskeyCeremonyRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<PasskeyCeremonyRequest>(
                proof,
                JsonSerializerOptions.Web
            );
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null)
        {
            return Refused(PasskeyFailures.Malformed);
        }

        var pending = challenges.Take(request.Challenge);
        if (pending is not { Binding: CeremonyBinding.Authentication })
        {
            return Refused(PasskeyFailures.Challenge);
        }

        var raw = PasskeyCeremonies.ParseAssertion(request.Response);
        if (raw is null)
        {
            return Refused(PasskeyFailures.Malformed);
        }

        // A discoverable credential names its account in the user handle; a credential whose
        // handle names another account, or none, is not this account's.
        var account = PasskeyCeremonies.AccountOf(raw.Response.UserHandle);
        if (account is null)
        {
            return Refused(PasskeyFailures.CredentialUnknown);
        }

        var store = new StateDocumentStore(contexts);
        var found = await Credentials.find(
            store,
            store,
            account,
            PasskeyCeremonies.Encode(raw.RawId),
            cancellationToken
        );
        if (found is not { Value: { } credential })
        {
            return Refused(PasskeyFailures.CredentialUnknown);
        }

        var verified = await ceremonies.FinishAuthentication(
            pending,
            raw,
            credential.Document,
            cancellationToken
        );
        if (
            verified
            is not DomainResult<
                Fido2NetLib.Objects.VerifyAssertionResult,
                ApiError
            >.Succeeded success
        )
        {
            return Refused(
                (
                    (DomainResult<
                        Fido2NetLib.Objects.VerifyAssertionResult,
                        ApiError
                    >.Failed)verified
                ).Error
            );
        }

        var recorded = await Credentials.recordSignCount(
            store,
            credential,
            success.Value.SignCount,
            cancellationToken
        );
        if (recorded.IsFailed)
        {
            return Refused(PasskeyFailures.Conflict);
        }

        return DomainResult<VerifiedIdentity, SignInFailure>.NewSucceeded(
            new VerifiedIdentity(
                Name,
                ExternalSubject.Create(account.Value)
                    is DomainResult<ExternalSubject, ExternalIdentityFailure>.Succeeded subject
                    ? subject.Value
                    : throw new InvalidOperationException("An account id is a subject."),
                null,
                SessionProvenance.FirstParty
            )
        );
    }

    private static DomainResult<VerifiedIdentity, SignInFailure> Refused(ApiError error) =>
        DomainResult<VerifiedIdentity, SignInFailure>.NewFailed(
            SignInFailure.NewProviderRefused(error)
        );
}

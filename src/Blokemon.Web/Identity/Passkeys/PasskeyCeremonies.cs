using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Fido2NetLib;
using Fido2NetLib.Exceptions;
using Fido2NetLib.Objects;

namespace Blokemon.Web.Identity.Passkeys;

/// <summary>
/// The two WebAuthn ceremonies, with the relying party and origins from
/// <c>Blokemon:Identity:Passkeys</c>. Options are issued with a fresh challenge and kept pending;
/// a response is verified against the options it answers. Credentials are discoverable so the
/// assertion carries the account as its user handle, and attestation is not asked for.
/// </summary>
public sealed class PasskeyCeremonies
{
    public const string ServerName = "Blokemon";

    /// <summary>
    /// ES256 and RS256, which every platform authenticator offers. EdDSA is left out so the
    /// verifier's native-library path is never taken.
    /// </summary>
    private static readonly PubKeyCredParam[] Algorithms =
    [
        new(COSE.Algorithm.ES256, PublicKeyCredentialType.PublicKey),
        new(COSE.Algorithm.RS256, PublicKeyCredentialType.PublicKey),
    ];

    private static readonly AuthenticatorSelection Selection = new()
    {
        ResidentKey = ResidentKeyRequirement.Required,
        UserVerification = UserVerificationRequirement.Preferred,
    };

    private readonly IFido2 _fido;
    private readonly PasskeyChallenges _challenges;

    public PasskeyCeremonies(PasskeySettings settings, PasskeyChallenges challenges)
    {
        _fido = new Fido2(
            new Fido2Configuration
            {
                ServerDomain = settings.RelyingPartyId,
                ServerName = ServerName,
                Origins = settings.Origins.ToHashSet(StringComparer.Ordinal),
            }
        );
        _challenges = challenges;
    }

    public static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);

    /// <summary>The user handle a credential carries: the account id, so the assertion names the account.</summary>
    public static byte[] UserHandle(AccountId account) => Encoding.UTF8.GetBytes(account.Value);

    public static AccountId? AccountOf(byte[]? userHandle)
    {
        if (userHandle is null || userHandle.Length is 0 or > 64)
        {
            return null;
        }

        return
            AccountId.Create(Encoding.UTF8.GetString(userHandle))
                is DomainResult<AccountId, IdentityValueFailure>.Succeeded account
            ? account.Value
            : null;
    }

    public PasskeyOptionsView BeginRegistration(
        AccountId account,
        string displayName,
        IEnumerable<string> excludeCredentialIds,
        CeremonyBinding binding
    )
    {
        var options = _fido.RequestNewCredential(
            new RequestNewCredentialParams
            {
                User = new Fido2User
                {
                    Id = UserHandle(account),
                    Name = displayName,
                    DisplayName = displayName,
                },
                ExcludeCredentials = excludeCredentialIds
                    .Select(static id => new PublicKeyCredentialDescriptor(
                        Base64Url.DecodeFromChars(id)
                    ))
                    .ToArray(),
                AuthenticatorSelection = Selection,
                AttestationPreference = AttestationConveyancePreference.None,
                PubKeyCredParams = Algorithms,
            }
        );
        return Pending(Encode(options.Challenge), options.ToJson(), binding);
    }

    public PasskeyOptionsView BeginAuthentication()
    {
        var options = _fido.GetAssertionOptions(
            new GetAssertionOptionsParams
            {
                AllowedCredentials = [],
                UserVerification = UserVerificationRequirement.Preferred,
            }
        );
        return Pending(
            Encode(options.Challenge),
            options.ToJson(),
            new CeremonyBinding.Authentication()
        );
    }

    /// <summary>Verifies an attestation response against the pending registration it answers.</summary>
    public async Task<DomainResult<RegisteredPublicKeyCredential, ApiError>> FinishRegistration(
        PendingCeremony pending,
        JsonElement response,
        CancellationToken cancellationToken
    )
    {
        AuthenticatorAttestationRawResponse raw;
        try
        {
            raw =
                JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(
                    response.GetRawText()
                ) ?? throw new JsonException("No credential.");
        }
        catch (JsonException)
        {
            return Failed<RegisteredPublicKeyCredential>(PasskeyFailures.Malformed);
        }

        try
        {
            var registered = await _fido.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = raw,
                    OriginalOptions = CredentialCreateOptions.FromJson(pending.OptionsJson),
                    IsCredentialIdUniqueToUserCallback = static (_, _) => Task.FromResult(true),
                },
                cancellationToken
            );
            return DomainResult<RegisteredPublicKeyCredential, ApiError>.NewSucceeded(registered);
        }
        catch (Fido2VerificationException)
        {
            return Failed<RegisteredPublicKeyCredential>(PasskeyFailures.Refused);
        }
    }

    /// <summary>The assertion as the browser sent it, or null when it is not one.</summary>
    public static AuthenticatorAssertionRawResponse? ParseAssertion(JsonElement response)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(
                response.GetRawText()
            );
            return raw is { RawId.Length: > 0, Response: not null } ? raw : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Verifies an assertion against the pending sign-in it answers and the stored credential.</summary>
    public async Task<DomainResult<VerifyAssertionResult, ApiError>> FinishAuthentication(
        PendingCeremony pending,
        AuthenticatorAssertionRawResponse raw,
        CredentialDocument stored,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var verified = await _fido.MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = raw,
                    OriginalOptions = AssertionOptions.FromJson(pending.OptionsJson),
                    StoredPublicKey = Convert.FromBase64String(stored.PublicKey),
                    StoredSignatureCounter = stored.SignCount,
                    IsUserHandleOwnerOfCredentialIdCallback = (parameters, _) =>
                        Task.FromResult(
                            string.Equals(
                                Encode(parameters.CredentialId),
                                stored.CredentialId,
                                StringComparison.Ordinal
                            )
                                && string.Equals(
                                    AccountOf(parameters.UserHandle)?.Value,
                                    stored.Account,
                                    StringComparison.Ordinal
                                )
                        ),
                },
                cancellationToken
            );
            return DomainResult<VerifyAssertionResult, ApiError>.NewSucceeded(verified);
        }
        catch (Fido2VerificationException)
        {
            return Failed<VerifyAssertionResult>(PasskeyFailures.Refused);
        }
    }

    private PasskeyOptionsView Pending(
        string challenge,
        string optionsJson,
        CeremonyBinding binding
    )
    {
        _challenges.Issue(challenge, binding, optionsJson);
        using var document = JsonDocument.Parse(optionsJson);
        return new(challenge, document.RootElement.Clone());
    }

    private static DomainResult<T, ApiError> Failed<T>(ApiError error) =>
        DomainResult<T, ApiError>.NewFailed(error);
}

/// <summary>The typed refusals of the passkey ceremonies.</summary>
public static class PasskeyFailures
{
    public static readonly ApiError Unavailable = new(
        "passkey.unavailable",
        "Passkeys are not enabled on this server."
    );

    public static readonly ApiError Challenge = new(
        "passkey.challenge",
        "That passkey request has expired or was already answered. Start again."
    );

    public static readonly ApiError Malformed = new(
        "passkey.malformed",
        "The browser's passkey response could not be read."
    );

    public static readonly ApiError Refused = new(
        "passkey.refused",
        "That passkey was not accepted."
    );

    public static readonly ApiError CredentialUnknown = new(
        "credential.unknown",
        "That passkey is not registered to an account on this server."
    );

    public static readonly ApiError AlreadyEnrolled = new(
        "credential.enrolled",
        "That passkey is already on your account."
    );

    public static readonly ApiError Conflict = new(
        "passkey.conflict",
        "Your passkeys changed underneath this request. Try again."
    );
}

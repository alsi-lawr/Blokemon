using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Api;
using Blokemon.Web.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Blokemon.Web.Identity.Passkeys;

/// <summary>
/// The first-party routes a signed-out browser uses: the registration, sign-in and recovery
/// ceremonies. Each ceremony is two calls, options then response, because that is what WebAuthn
/// is; the session policy names these routes exactly. The session-required routes are
/// <see cref="PasskeyEnrolmentEndpoints"/>. Absent the provider every ceremony route answers
/// the typed unavailable.
/// </summary>
public static class FirstPartyEndpoints
{
    public const string Prefix = "/api/session/firstparty";

    public static IEndpointRouteBuilder MapFirstPartyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Prefix);
        group.MapPost("/register/options", RegisterOptions);
        group.MapPost("/register", Register);
        group.MapPost("/authenticate/options", AuthenticateOptions);
        group.MapPost("/authenticate", Authenticate);
        group.MapPost("/recover", Recover);
        PasskeyEnrolmentEndpoints.Map(group);
        return endpoints;
    }

    private static ApiResponse<PasskeyOptionsView> RegisterOptions(
        PasskeyRegisterOptionsRequest request,
        [FromServices] PasskeyCeremonies? ceremonies
    )
    {
        if (ceremonies is null)
        {
            return Envelope.Fail<PasskeyOptionsView>(PasskeyFailures.Unavailable);
        }

        // The account is minted now so the credential's user handle names it; it is created
        // only once the browser's response verifies.
        var account = AccountId.Mint();
        var displayName = SignInCompletion.displayName(request.DisplayName);
        return Envelope.Ok(
            ceremonies.BeginRegistration(
                account,
                displayName,
                [],
                new CeremonyBinding.NewAccount(account, displayName)
            )
        );
    }

    private static async Task<ApiResponse<PasskeyRegistrationView>> Register(
        PasskeyCeremonyRequest request,
        [FromServices] PasskeyCeremonies? ceremonies,
        PasskeyChallenges challenges,
        StateDocumentStore documents,
        SignInServices services,
        ServerApplications applications,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (ceremonies is null)
        {
            return Envelope.Fail<PasskeyRegistrationView>(PasskeyFailures.Unavailable);
        }

        if (
            challenges.Take(request.Challenge)
            is not { Binding: CeremonyBinding.NewAccount binding } pending
        )
        {
            return Envelope.Fail<PasskeyRegistrationView>(PasskeyFailures.Challenge);
        }

        var tenant = await TenantResolution.Resolve(documents, request.Slug, cancellationToken);
        if (tenant is null)
        {
            return Envelope.Fail<PasskeyRegistrationView>(TenantResolution.NotFound);
        }

        var registered = await ceremonies.FinishRegistration(
            pending,
            request.Response,
            cancellationToken
        );
        if (
            registered
            is DomainResult<
                Fido2NetLib.Objects.RegisteredPublicKeyCredential,
                ApiError
            >.Failed refused
        )
        {
            return Envelope.Fail<PasskeyRegistrationView>(refused.Error);
        }

        var credential = (
            (DomainResult<
                Fido2NetLib.Objects.RegisteredPublicKeyCredential,
                ApiError
            >.Succeeded)registered
        ).Value;
        var now = time.GetUtcNow();
        // Always a new account: the subject is the minted id, which no link can name yet.
        var completed = await SignInCompletion.completeAs(
            services,
            new VerifiedIdentity(
                IdentityConfigurationModule.FirstPartyProvider,
                Subject(binding.Account),
                binding.DisplayName,
                SessionProvenance.FirstParty
            ),
            binding.Account,
            TenantResolution.IdOf(tenant),
            now,
            cancellationToken
        );
        if (completed is DomainResult<IssuedSession, SignInFailure>.Failed failed)
        {
            return Envelope.Fail<PasskeyRegistrationView>(SignInFailures.toError(failed.Error));
        }

        var issued = ((DomainResult<IssuedSession, SignInFailure>.Succeeded)completed).Value;
        var enrolled = await App.Credentials.enrol(
            documents,
            documents,
            binding.Account,
            PasskeyCeremonies.Encode(credential.Id),
            Convert.ToBase64String(credential.PublicKey),
            credential.SignCount,
            SessionProvenance.FirstParty,
            null,
            now,
            cancellationToken
        );
        if (enrolled.IsFailed)
        {
            return Envelope.Fail<PasskeyRegistrationView>(PasskeyFailures.Conflict);
        }

        var codes = await RecoveryCodes.issue(documents, binding.Account, now, cancellationToken);
        if (codes is not DomainResult<string[], RecoveryFailure>.Succeeded generated)
        {
            return Envelope.Fail<PasskeyRegistrationView>(
                PasskeyRecovery.toError(
                    ((DomainResult<string[], RecoveryFailure>.Failed)codes).Error
                )
            );
        }

        return Envelope.Ok(
            new PasskeyRegistrationView(
                await SessionViews.Describe(issued, applications, cancellationToken),
                generated.Value
            )
        );
    }

    private static ApiResponse<PasskeyOptionsView> AuthenticateOptions(
        [FromServices] PasskeyCeremonies? ceremonies
    ) =>
        ceremonies is null
            ? Envelope.Fail<PasskeyOptionsView>(PasskeyFailures.Unavailable)
            : Envelope.Ok(ceremonies.BeginAuthentication());

    private static async Task<ApiResponse<IssuedSessionView>> Authenticate(
        PasskeyCeremonyRequest request,
        StateDocumentStore documents,
        SignInServices services,
        IdentityProviderRegistry registry,
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

        var provider = registry.Find(IdentityConfigurationModule.FirstPartyProvider);
        if (provider is null)
        {
            return Envelope.Fail<IssuedSessionView>(PasskeyFailures.Unavailable);
        }

        var verified = await provider.Verify(FirstPartyProvider.Proof(request), cancellationToken);
        if (verified is not DomainResult<VerifiedIdentity, SignInFailure>.Succeeded identity)
        {
            return Envelope.Fail<IssuedSessionView>(
                SignInFailures.toError(
                    ((DomainResult<VerifiedIdentity, SignInFailure>.Failed)verified).Error
                )
            );
        }

        // The credential named the account, so the completion is for that account whether or
        // not a first-party link was written yet: one enrolled from a channel session gets its
        // link on this first sign-in.
        var outcome = await SignInCompletion.completeAs(
            services,
            identity.Value,
            AccountOf(identity.Value.Subject),
            TenantResolution.IdOf(tenant),
            time.GetUtcNow(),
            cancellationToken
        );
        return outcome is DomainResult<IssuedSession, SignInFailure>.Succeeded issued
            ? Envelope.Ok(
                await SessionViews.Describe(issued.Value, applications, cancellationToken)
            )
            : Envelope.Fail<IssuedSessionView>(
                SignInFailures.toError(
                    ((DomainResult<IssuedSession, SignInFailure>.Failed)outcome).Error
                )
            );
    }

    private static async Task<ApiResponse<IssuedSessionView>> Recover(
        RecoveryRequest request,
        HttpContext context,
        StateDocumentStore documents,
        IdentityConfiguration identity,
        ClientLockouts lockouts,
        ServerApplications applications,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var now = time.GetUtcNow();
        var client = ClientLockouts.ClientOf(context);
        if (lockouts.Recovery.IsLockedOut(client, now))
        {
            return Envelope.Fail<IssuedSessionView>(PasskeyRecovery.locked());
        }

        var tenant = await TenantResolution.Resolve(documents, request.Slug, cancellationToken);
        if (tenant is null)
        {
            return Envelope.Fail<IssuedSessionView>(TenantResolution.NotFound);
        }

        var recovered = await PasskeyRecovery.recover(
            documents,
            documents,
            TenantResolution.IdOf(tenant),
            request.Code,
            now,
            identity.SessionLifetime,
            cancellationToken
        );
        if (recovered is DomainResult<IssuedSession, RecoveryFailure>.Succeeded issued)
        {
            return Envelope.Ok(
                await SessionViews.Describe(issued.Value, applications, cancellationToken)
            );
        }

        var failure = ((DomainResult<IssuedSession, RecoveryFailure>.Failed)recovered).Error;
        if (failure.IsRefused)
        {
            lockouts.Recovery.RecordFailure(client, now);
        }

        return Envelope.Fail<IssuedSessionView>(PasskeyRecovery.toError(failure));
    }

    private static AccountId AccountOf(ExternalSubject subject) =>
        AccountId.Create(subject.Value)
            is DomainResult<AccountId, IdentityValueFailure>.Succeeded account
            ? account.Value
            : throw new InvalidOperationException("A first-party subject is an account id.");

    private static ExternalSubject Subject(AccountId account) =>
        ExternalSubject.Create(account.Value)
            is DomainResult<ExternalSubject, ExternalIdentityFailure>.Succeeded subject
            ? subject.Value
            : throw new InvalidOperationException("An account id is a subject.");
}

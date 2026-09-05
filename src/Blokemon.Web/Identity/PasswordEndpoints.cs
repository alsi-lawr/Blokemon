using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Api;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Identity;

/// <summary>
/// The first-party simple login (BLOKEMON-163, D-046): a player name and password beside the
/// passkey. Two anonymous routes create an account and sign one in, held to the recovery
/// lock-out terms per client and per name; one session route sets the password, which a
/// <c>Recovery</c> session may do as its one operation, ending as the passkey replacement does.
/// The login rules are <see cref="Logins"/>'s and the set-password authority is
/// <see cref="PasskeyEnrolment"/>'s; the routes apply them and nothing more.
/// </summary>
public static class PasswordEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/password/register", Register).AddEndpointFilter<SignInDiagnosticsFilter>();
        group.MapPost("/password", SignIn).AddEndpointFilter<SignInDiagnosticsFilter>();
        group.MapPost("/password/set", Set);
    }

    private static async Task<ApiResponse<AccountRegistrationView>> Register(
        PasswordRegistrationRequest request,
        StateDocumentStore documents,
        SignInServices services,
        IdentityProviderRegistry registry,
        ServerApplications applications,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (!registry.IsEnabled(IdentityConfigurationModule.FirstPartyProvider))
        {
            return Envelope.Fail<AccountRegistrationView>(Unavailable);
        }

        var parsed = LoginName.Create(request.Name);
        if (parsed is not DomainResult<LoginName, LoginNameFailure>.Succeeded name)
        {
            return Envelope.Fail<AccountRegistrationView>(
                Logins.toError(
                    LoginFailure.NewName(
                        ((DomainResult<LoginName, LoginNameFailure>.Failed)parsed).Error
                    )
                )
            );
        }

        var tenant = await TenantResolution.Resolve(documents, request.Slug, cancellationToken);
        if (tenant is null)
        {
            return Envelope.Fail<AccountRegistrationView>(TenantResolution.NotFound);
        }

        // The name is reserved and the login written before the account exists, so a taken name
        // refuses with nothing else written; a completion interrupted after this converges on
        // the next sign-in with the name, as every first sign-in does.
        var now = time.GetUtcNow();
        var account = AccountId.Mint();
        var registered = await Logins.register(
            documents,
            account,
            name.Value,
            request.Password,
            now,
            cancellationToken
        );
        if (registered is DomainResult<LoginDocument, LoginFailure>.Failed refused)
        {
            return Envelope.Fail<AccountRegistrationView>(Logins.toError(refused.Error));
        }

        var completed = await SignInCompletion.completeAs(
            services,
            Identity(account, name.Value.Value),
            account,
            TenantResolution.IdOf(tenant),
            now,
            cancellationToken
        );
        if (completed is not DomainResult<IssuedSession, SignInFailure>.Succeeded issued)
        {
            return Envelope.Fail<AccountRegistrationView>(
                SignInFailures.toError(
                    ((DomainResult<IssuedSession, SignInFailure>.Failed)completed).Error
                )
            );
        }

        var codes = await RecoveryCodes.issue(documents, account, now, cancellationToken);
        if (codes is not DomainResult<string[], RecoveryFailure>.Succeeded generated)
        {
            return Envelope.Fail<AccountRegistrationView>(
                PasskeyRecovery.toError(
                    ((DomainResult<string[], RecoveryFailure>.Failed)codes).Error
                )
            );
        }

        return Envelope.Ok(
            new AccountRegistrationView(
                await SessionViews.Describe(issued.Value, applications, cancellationToken),
                generated.Value
            )
        );
    }

    private static async Task<ApiResponse<IssuedSessionView>> SignIn(
        PasswordSignInRequest request,
        HttpContext context,
        StateDocumentStore documents,
        SignInServices services,
        IdentityProviderRegistry registry,
        ClientLockouts lockouts,
        ServerApplications applications,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (!registry.IsEnabled(IdentityConfigurationModule.FirstPartyProvider))
        {
            return Envelope.Fail<IssuedSessionView>(Unavailable);
        }

        // Locked out by the client's own failures, or by failures against this name from
        // anywhere, before the password is looked at.
        var now = time.GetUtcNow();
        var client = ClientLockouts.ClientOf(context);
        var nameKey = (request.Name ?? string.Empty).Trim().ToLowerInvariant();
        if (lockouts.Login.IsLockedOut(client, now) || lockouts.LoginName.IsLockedOut(nameKey, now))
        {
            return Envelope.Fail<IssuedSessionView>(Logins.locked());
        }

        var tenant = await TenantResolution.Resolve(documents, request.Slug, cancellationToken);
        if (tenant is null)
        {
            return Envelope.Fail<IssuedSessionView>(TenantResolution.NotFound);
        }

        var verified = await Logins.verify(
            documents,
            request.Name,
            request.Password,
            cancellationToken
        );
        if (
            verified
            is not DomainResult<Tuple<AccountId, LoginDocument>, LoginFailure>.Succeeded login
        )
        {
            var failure = (
                (DomainResult<Tuple<AccountId, LoginDocument>, LoginFailure>.Failed)verified
            ).Error;
            if (failure.IsRefused)
            {
                lockouts.Login.RecordFailure(client, now);
                lockouts.LoginName.RecordFailure(nameKey, now);
            }

            return Envelope.Fail<IssuedSessionView>(Logins.toError(failure));
        }

        var (account, document) = login.Value;
        var outcome = await SignInCompletion.completeAs(
            services,
            Identity(account, document.Name),
            account,
            TenantResolution.IdOf(tenant),
            now,
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

    private static async Task<ApiResponse<PasswordSetView>> Set(
        PasswordSetRequest request,
        CurrentSession current,
        StateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var session = current.Session!;
        var hasCredential =
            await App.Credentials.anyFor(documents, documents, session.Account, cancellationToken)
            || await Logins.anyFor(documents, session.Account, cancellationToken);
        var authorized = PasskeyEnrolment.authorize(
            session.Provenance,
            hasCredential,
            await RecoveryCodes.hasLive(documents, session.Account, cancellationToken)
        );
        if (authorized is not DomainResult<EnrolmentGrant, EnrolmentFailure>.Succeeded grant)
        {
            return Envelope.Fail<PasswordSetView>(
                PasskeyEnrolment.toError(
                    ((DomainResult<EnrolmentGrant, EnrolmentFailure>.Failed)authorized).Error
                )
            );
        }

        var now = time.GetUtcNow();
        var set = await Logins.set(
            documents,
            session.Account,
            request.Name,
            request.Password,
            now,
            cancellationToken
        );
        if (set is not DomainResult<LoginDocument, LoginFailure>.Succeeded stored)
        {
            return Envelope.Fail<PasswordSetView>(
                Logins.toError(((DomainResult<LoginDocument, LoginFailure>.Failed)set).Error)
            );
        }

        string[]? codes = null;
        if (grant.Value.GeneratesCodes)
        {
            var issued = await RecoveryCodes.issue(
                documents,
                session.Account,
                now,
                cancellationToken
            );
            if (issued is not DomainResult<string[], RecoveryFailure>.Succeeded generated)
            {
                return Envelope.Fail<PasswordSetView>(
                    PasskeyRecovery.toError(
                        ((DomainResult<string[], RecoveryFailure>.Failed)issued).Error
                    )
                );
            }

            codes = generated.Value;
        }

        if (session.Provenance == SessionProvenance.Recovery)
        {
            // The new password is set and the new codes are in the response: the one thing
            // this session could do is done, and the person signs in with the password.
            await Sessions.revoke(documents, session.Id, cancellationToken);
        }

        return Envelope.Ok(new PasswordSetView(stored.Value.Name, codes));
    }

    private static readonly ApiError Unavailable = new(
        "login.unavailable",
        "Sign-in with a player name is not enabled on this server."
    );

    /// <summary>The first-party identity of the account: the subject is the account itself.</summary>
    private static VerifiedIdentity Identity(AccountId account, string displayNameHint) =>
        new(
            IdentityConfigurationModule.FirstPartyProvider,
            ExternalSubject.Create(account.Value)
                is DomainResult<ExternalSubject, ExternalIdentityFailure>.Succeeded subject
                ? subject.Value
                : throw new InvalidOperationException("An account id is a subject."),
            displayNameHint,
            SessionProvenance.FirstParty
        );
}

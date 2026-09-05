using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Api;
using Blokemon.Web.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Blokemon.Web.Identity.Passkeys;

/// <summary>
/// The first-party routes a session uses: enrolling a further passkey (the one operation a
/// <c>Recovery</c> session may perform), listing the account's passkeys and recovery-code
/// count with what this session may do about them, and making a new code set. The enrolment
/// rules are <see cref="PasskeyEnrolment"/>'s; the routes apply them and nothing more.
/// </summary>
public static class PasskeyEnrolmentEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/enrol/options", EnrolOptions);
        group.MapPost("/enrol", Enrol);
        group.MapGet("/credentials", Credentials);
        group.MapPost("/recovery-codes", RecoveryCodesRoute);
    }

    private static async Task<ApiResponse<PasskeyOptionsView>> EnrolOptions(
        CurrentSession current,
        [FromServices] PasskeyCeremonies? ceremonies,
        StateDocumentStore documents,
        ServerApplications applications,
        CancellationToken cancellationToken
    )
    {
        if (ceremonies is null)
        {
            return Envelope.Fail<PasskeyOptionsView>(PasskeyFailures.Unavailable);
        }

        var session = current.Session!;
        var existing = await App.Credentials.forAccount(
            documents,
            documents,
            session.Account,
            cancellationToken
        );
        var authorized = PasskeyEnrolment.authorize(
            session.Provenance,
            existing.Any(),
            await RecoveryCodes.hasLive(documents, session.Account, cancellationToken)
        );
        if (authorized.IsFailed)
        {
            return Envelope.Fail<PasskeyOptionsView>(
                PasskeyEnrolment.toError(
                    ((DomainResult<EnrolmentGrant, EnrolmentFailure>.Failed)authorized).Error
                )
            );
        }

        var state = await applications.For(session).State(cancellationToken);
        return Envelope.Ok(
            ceremonies.BeginRegistration(
                session.Account,
                state.Value?.Profile?.DisplayName ?? SignInCompletion.FallbackDisplayName,
                existing.Select(static loaded => loaded.Document.CredentialId),
                new CeremonyBinding.Enrolment(session.Account, session.Provenance, session.Tenant)
            )
        );
    }

    private static async Task<ApiResponse<PasskeyEnrolmentView>> Enrol(
        PasskeyCeremonyRequest request,
        CurrentSession current,
        [FromServices] PasskeyCeremonies? ceremonies,
        PasskeyChallenges challenges,
        StateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        if (ceremonies is null)
        {
            return Envelope.Fail<PasskeyEnrolmentView>(PasskeyFailures.Unavailable);
        }

        var session = current.Session!;
        // The pending ceremony must be this session's own: one issued to another account, or
        // to another sign-in of this account, does not answer here.
        if (
            challenges.Take(request.Challenge)
                is not { Binding: CeremonyBinding.Enrolment binding } pending
            || binding.Account != session.Account
            || binding.Provenance != session.Provenance
        )
        {
            return Envelope.Fail<PasskeyEnrolmentView>(PasskeyFailures.Challenge);
        }

        var hasCredential = await App.Credentials.anyFor(
            documents,
            documents,
            session.Account,
            cancellationToken
        );
        var authorized = PasskeyEnrolment.authorize(
            session.Provenance,
            hasCredential,
            await RecoveryCodes.hasLive(documents, session.Account, cancellationToken)
        );
        if (authorized is not DomainResult<EnrolmentGrant, EnrolmentFailure>.Succeeded grant)
        {
            return Envelope.Fail<PasskeyEnrolmentView>(
                PasskeyEnrolment.toError(
                    ((DomainResult<EnrolmentGrant, EnrolmentFailure>.Failed)authorized).Error
                )
            );
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
            return Envelope.Fail<PasskeyEnrolmentView>(refused.Error);
        }

        var credential = (
            (DomainResult<
                Fido2NetLib.Objects.RegisteredPublicKeyCredential,
                ApiError
            >.Succeeded)registered
        ).Value;
        var now = time.GetUtcNow();
        var enrolled = await App.Credentials.enrol(
            documents,
            documents,
            session.Account,
            PasskeyCeremonies.Encode(credential.Id),
            Convert.ToBase64String(credential.PublicKey),
            credential.SignCount,
            session.Provenance,
            session.Provenance == SessionProvenance.Issuer ? session.Tenant : null,
            now,
            cancellationToken
        );
        if (enrolled is not DomainResult<CredentialDocument, CredentialFailure>.Succeeded stored)
        {
            var failure = (
                (DomainResult<CredentialDocument, CredentialFailure>.Failed)enrolled
            ).Error;
            return Envelope.Fail<PasskeyEnrolmentView>(
                failure.IsAlreadyEnrolled
                    ? PasskeyFailures.AlreadyEnrolled
                    : PasskeyFailures.Conflict
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
                return Envelope.Fail<PasskeyEnrolmentView>(
                    PasskeyRecovery.toError(
                        ((DomainResult<string[], RecoveryFailure>.Failed)issued).Error
                    )
                );
            }

            codes = generated.Value;
        }

        if (session.Provenance == SessionProvenance.Recovery)
        {
            // The replacement is enrolled and the new codes are in the response: the one thing
            // this session could do is done, and the person signs in with the passkey.
            await Sessions.revoke(documents, session.Id, cancellationToken);
        }

        return Envelope.Ok(
            new PasskeyEnrolmentView(await View(stored.Value, documents, cancellationToken), codes)
        );
    }

    private static async Task<ApiResponse<PasskeyStateView>> Credentials(
        CurrentSession current,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        var session = current.Session!;
        var credentials = await App.Credentials.forAccount(
            documents,
            documents,
            session.Account,
            cancellationToken
        );
        var passkeys = new List<PasskeyView>();
        foreach (var loaded in credentials)
        {
            passkeys.Add(await View(loaded.Document, documents, cancellationToken));
        }

        var codes = await RecoveryCodes.load(documents, session.Account, cancellationToken);
        var remaining = codes
            is DomainResult<
                Microsoft.FSharp.Core.FSharpOption<LoadedRecoveryCodes>,
                RecoveryFailure
            >.Succeeded { Value: { } set }
            ? RecoveryCodes.liveCount(set.Value.Document)
            : (int?)null;
        return Envelope.Ok(
            new PasskeyStateView(
                passkeys.ToArray(),
                remaining,
                PasskeyEnrolment
                    .authorize(session.Provenance, passkeys.Count > 0, remaining > 0)
                    .IsSucceeded,
                PasskeyEnrolment.mayRegenerate(session.Provenance)
            )
        );
    }

    private static async Task<ApiResponse<RecoveryCodesView>> RecoveryCodesRoute(
        CurrentSession current,
        StateDocumentStore documents,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var session = current.Session!;
        if (!PasskeyEnrolment.mayRegenerate(session.Provenance))
        {
            return Envelope.Fail<RecoveryCodesView>(
                PasskeyEnrolment.toError(EnrolmentFailure.ProvenanceRefused)
            );
        }

        var issued = await RecoveryCodes.issue(
            documents,
            session.Account,
            time.GetUtcNow(),
            cancellationToken
        );
        return issued is DomainResult<string[], RecoveryFailure>.Succeeded generated
            ? Envelope.Ok(new RecoveryCodesView(generated.Value))
            : Envelope.Fail<RecoveryCodesView>(
                PasskeyRecovery.toError(
                    ((DomainResult<string[], RecoveryFailure>.Failed)issued).Error
                )
            );
    }

    private static async Task<PasskeyView> View(
        CredentialDocument credential,
        StateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        string? tenantLabel = null;
        if (
            credential.Tenant is { } tenantId
            && TenantId.Create(tenantId)
                is DomainResult<TenantId, IdentityValueFailure>.Succeeded id
        )
        {
            var tenant = await Tenants.read(documents, id.Value, cancellationToken);
            tenantLabel = tenant?.Value.DisplayLabel;
        }

        return new(
            credential.Id,
            credential.EnrolledAt,
            credential.Provenance.ToString(),
            tenantLabel
        );
    }
}

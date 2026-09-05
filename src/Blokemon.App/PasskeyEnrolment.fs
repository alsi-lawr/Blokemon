namespace Blokemon.App

open Blokemon.App.Contracts
open Blokemon.Product

/// Why a session may not enrol a passkey or make new recovery codes.
[<RequireQualifiedAccess>]
type EnrolmentFailure =
    /// The session's provenance does not carry that authority for the account as it stands.
    | ProvenanceRefused

/// What an authorised enrolment brings with it.
type EnrolmentGrant =
    {
        /// Whether a recovery-code set is issued with this passkey: the account's first, or the
        /// replacement set a recovery ends with.
        GeneratesCodes: bool
    }

/// Who may add a passkey, and who may make new recovery codes. Registering a credential always
/// needs a session for the account; beyond that the provenance decides. A FirstParty session may
/// add one at any time. A Recovery session enrols the replacement passkey, with a fresh code set,
/// which is the one thing it can do. An Issuer session may enrol only the first passkey on an
/// account that has none and no live code set, the one route by which a channel-only player
/// migrates to core; adding a second, or making new codes, needs the person's own credential.
module PasskeyEnrolment =

    let toError (failure: EnrolmentFailure) : ApiError =
        match failure with
        | EnrolmentFailure.ProvenanceRefused ->
            ApiError(
                "passkey.provenance",
                "Sign in with your passkey to add another passkey or make new recovery codes."
            )

    let authorize
        (provenance: SessionProvenance)
        (hasCredential: bool)
        (hasLiveCodes: bool)
        : DomainResult<EnrolmentGrant, EnrolmentFailure> =
        match provenance with
        | SessionProvenance.FirstParty ->
            DomainResult.Succeeded { GeneratesCodes = not hasCredential }
        | SessionProvenance.Recovery -> DomainResult.Succeeded { GeneratesCodes = true }
        | SessionProvenance.Issuer when hasCredential || hasLiveCodes ->
            DomainResult.Failed EnrolmentFailure.ProvenanceRefused
        | SessionProvenance.Issuer -> DomainResult.Succeeded { GeneratesCodes = true }
        | _ -> DomainResult.Failed EnrolmentFailure.ProvenanceRefused

    /// Making a new code set invalidates the old one, so only the person's own credential may.
    let mayRegenerate (provenance: SessionProvenance) =
        provenance = SessionProvenance.FirstParty

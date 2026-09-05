namespace Blokemon.App

open System
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// What a channel's hand-off produced once its code was consumed.
[<RequireQualifiedAccess>]
type HandoffOutcome =
    | Admitted of IssuedSession
    /// The account exists and this tenant is not yet approved for it: recorded pending, no
    /// session.
    | ApprovalPending

/// Why an approval was not granted or a tenant not dissociated. Nothing is written for any of
/// these.
[<RequireQualifiedAccess>]
type ApprovalRefusal =
    /// The session's provenance may not approve.
    | NotEligible
    /// The session's own tenant is not a live route for the account.
    | NoRoute
    /// Nothing is pending for that tenant.
    | NothingPending
    /// The owner has excluded the account there; exclusion dominates.
    | Excluded
    | TenantNotFound
    | Conflict
    | Damaged

/// A channel waiting for the person's approval.
type PendingApproval =
    { Tenant: TenantDocument
      Approval: ApprovalDocument }

/// Issuer admission: whether a channel's hand-off signs an account in, records the channel as
/// pending, or, for the core sign-in, adopts an account with no other way in; who may approve a
/// pending channel; and the relay that dissociates a channel from an account.
module IssuerAdmission =

    let toError (refusal: ApprovalRefusal) : ApiError =
        match refusal with
        | ApprovalRefusal.NotEligible ->
            ApiError(
                "approval.provenance",
                "Approve from a channel you already play in, or sign in with your passkey."
            )
        | ApprovalRefusal.NoRoute ->
            ApiError("approval.route", "This channel cannot approve another for your account.")
        | ApprovalRefusal.NothingPending ->
            ApiError("approval.none", "That channel is not waiting for your approval.")
        | ApprovalRefusal.Excluded ->
            ApiError("tenant.excluded", "This channel has excluded your account.")
        | ApprovalRefusal.TenantNotFound ->
            ApiError("tenant.not_found", "That channel is not on this server.")
        | ApprovalRefusal.Conflict ->
            ApiError(
                "approval.conflict",
                "The approval changed underneath this request. Try again."
            )
        | ApprovalRefusal.Damaged ->
            ApiError("approval.damaged", "An approval record could not be read. Nothing changed.")

    /// The refusal a hand-off for a closed or revoked tenant gets.
    let tenantClosed (tenant: TenantDocument) =
        match tenant.Status with
        | TenantStatus.Revoked -> ApiError("tenant.revoked", "That channel was revoked.")
        | _ -> ApiError("tenant.closed", "That channel has closed. Play on at the main Blokemon.")

    let private ofApprovalFailure (failure: ApprovalFailure) =
        match failure with
        | ApprovalFailure.Conflict -> SignInFailure.Conflict
        | ApprovalFailure.Damaged -> SignInFailure.Damaged

    /// Whether any approved, active, unexcluded tenant is a way into the account.
    let hasLiveRoute
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        task {
            let! approvals = Approvals.forAccount documents listing account cancellationToken

            return
                approvals
                |> List.exists (fun (approval, tenant) ->
                    match tenant with
                    | Some tenant -> Approvals.isLiveRoute approval tenant
                    | None -> false)
        }

    /// The channels waiting for the account's approval: pending, not excluded, still active.
    let pending
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<PendingApproval list> =
        task {
            let! approvals = Approvals.forAccount documents listing account cancellationToken

            return
                approvals
                |> List.choose (fun (approval, tenant) ->
                    match tenant with
                    | Some tenant when
                        approval.Status = ApprovalStatus.Pending
                        && not (Approvals.isExcluded approval)
                        && tenant.Status = TenantStatus.Active
                        ->
                        Some { Tenant = tenant; Approval = approval }
                    | _ -> None)
        }

    /// Admits the identity a channel handed off: a subject with no account gets one and the
    /// channel is approved for it; an account the channel is approved for signs in; the core
    /// sign-in adopts an account with no passkey and no live route; any other existing account
    /// is recorded pending and gets no session.
    let admit
        (services: SignInServices)
        (listing: IDocumentListing)
        (identity: VerifiedIdentity)
        (tenant: TenantDocument)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<HandoffOutcome, SignInFailure>> =
        task {
            let documents = services.Documents
            let tenantId = Tenants.idOf tenant

            let signIn () =
                task {
                    let! completed =
                        SignInCompletion.complete services identity tenantId now cancellationToken

                    match completed with
                    | DomainResult.Succeeded issued ->
                        return DomainResult.Succeeded(HandoffOutcome.Admitted issued)
                    | DomainResult.Failed failure -> return DomainResult.Failed failure
                }

            let approveThenSignIn account adopted =
                task {
                    let! approved =
                        Approvals.approve documents account tenantId now adopted cancellationToken

                    match approved with
                    | DomainResult.Failed failure ->
                        return DomainResult.Failed(ofApprovalFailure failure)
                    | DomainResult.Succeeded() -> return! signIn ()
                }

            if tenant.Status <> TenantStatus.Active then
                return DomainResult.Failed(SignInFailure.ProviderRefused(tenantClosed tenant))
            else
                let! resolution =
                    IdentityLinks.resolve
                        documents
                        identity.Provider
                        identity.Subject
                        cancellationToken

                match resolution with
                | LinkResolution.Damaged -> return DomainResult.Failed SignInFailure.Damaged
                | LinkResolution.Unlinked ->
                    // First sign-in: the account is created by completion and the creating
                    // channel becomes its first live route.
                    let! completed = signIn ()

                    match completed with
                    | DomainResult.Succeeded(HandoffOutcome.Admitted issued) ->
                        let! approved =
                            Approvals.approve
                                documents
                                issued.Session.Account
                                tenantId
                                now
                                false
                                cancellationToken

                        match approved with
                        | DomainResult.Succeeded() -> return completed
                        | DomainResult.Failed failure ->
                            return DomainResult.Failed(ofApprovalFailure failure)
                    | other -> return other
                | LinkResolution.Linked account ->
                    let! approval = Approvals.load documents account tenantId cancellationToken

                    match approval with
                    | DomainResult.Failed failure ->
                        return DomainResult.Failed(ofApprovalFailure failure)
                    | DomainResult.Succeeded(Some loaded) when Approvals.isExcluded loaded.Document ->
                        return DomainResult.Failed SignInFailure.TenantExcluded
                    | DomainResult.Succeeded(Some loaded) when
                        loaded.Document.Status = ApprovalStatus.Approved
                        ->
                        return! signIn ()
                    | DomainResult.Succeeded _ ->
                        let! hasCredential =
                            Credentials.anyFor documents listing account cancellationToken

                        let! routed = hasLiveRoute documents listing account cancellationToken

                        if Tenants.isDefault tenant && not hasCredential && not routed then
                            return! approveThenSignIn account true
                        else
                            let! recorded =
                                Approvals.ensurePending documents account tenantId cancellationToken

                            match recorded with
                            | DomainResult.Succeeded() ->
                                return DomainResult.Succeeded HandoffOutcome.ApprovalPending
                            | DomainResult.Failed failure ->
                                return DomainResult.Failed(ofApprovalFailure failure)
        }

    /// Grants a pending approval for the session's own account: from a first-party session, or
    /// from an issuer session of a tenant that is a live route for the account.
    let approve
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (approver: Session)
        (tenant: TenantId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, ApprovalRefusal>> =
        task {
            let account = approver.Account

            let! eligible =
                task {
                    match approver.Provenance with
                    | SessionProvenance.FirstParty -> return Ok()
                    | SessionProvenance.Issuer ->
                        let! own = Tenants.read documents approver.Tenant cancellationToken

                        let! route =
                            Approvals.load documents account approver.Tenant cancellationToken

                        match own, route with
                        | Some ownTenant, DomainResult.Succeeded(Some loaded) when
                            Approvals.isLiveRoute loaded.Document ownTenant
                            ->
                            return Ok()
                        | _, DomainResult.Failed failure ->
                            return
                                Error(ofApprovalFailure failure |> fun _ -> ApprovalRefusal.Damaged)
                        | _ -> return Error ApprovalRefusal.NoRoute
                    | _ -> return Error ApprovalRefusal.NotEligible
                }

            match eligible with
            | Error refusal -> return DomainResult.Failed refusal
            | Ok() ->
                let! target = Tenants.read documents tenant cancellationToken

                match target with
                | None -> return DomainResult.Failed ApprovalRefusal.TenantNotFound
                | Some _ ->
                    let! loaded = Approvals.load documents account tenant cancellationToken

                    match loaded with
                    | DomainResult.Failed ApprovalFailure.Damaged ->
                        return DomainResult.Failed ApprovalRefusal.Damaged
                    | DomainResult.Failed ApprovalFailure.Conflict ->
                        return DomainResult.Failed ApprovalRefusal.Conflict
                    | DomainResult.Succeeded None ->
                        return DomainResult.Failed ApprovalRefusal.NothingPending
                    | DomainResult.Succeeded(Some existing) when
                        Approvals.isExcluded existing.Document
                        ->
                        return DomainResult.Failed ApprovalRefusal.Excluded
                    | DomainResult.Succeeded(Some _) ->
                        let! approved =
                            Approvals.approve documents account tenant now false cancellationToken

                        match approved with
                        | DomainResult.Succeeded() -> return DomainResult.Succeeded()
                        | DomainResult.Failed ApprovalFailure.Conflict ->
                            return DomainResult.Failed ApprovalRefusal.Conflict
                        | DomainResult.Failed ApprovalFailure.Damaged ->
                            return DomainResult.Failed ApprovalRefusal.Damaged
        }

    /// The erasure relay: the calling tenant's approval for the subject's account is removed
    /// and nothing else is touched. A subject with no account, or one the tenant holds no
    /// approval for, is a no-op; the relay is idempotent.
    let relayErasure
        (documents: IStateDocumentStore)
        (provider: IdentityProviderName)
        (subject: ExternalSubject)
        (tenant: TenantId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<bool, ApprovalRefusal>> =
        task {
            let! resolution = IdentityLinks.resolve documents provider subject cancellationToken

            match resolution with
            | LinkResolution.Unlinked -> return DomainResult.Succeeded false
            | LinkResolution.Damaged -> return DomainResult.Failed ApprovalRefusal.Damaged
            | LinkResolution.Linked account ->
                let! dissociated = Approvals.dissociate documents account tenant cancellationToken

                match dissociated with
                | DomainResult.Succeeded changed -> return DomainResult.Succeeded changed
                | DomainResult.Failed ApprovalFailure.Conflict ->
                    return DomainResult.Failed ApprovalRefusal.Conflict
                | DomainResult.Failed ApprovalFailure.Damaged ->
                    return DomainResult.Failed ApprovalRefusal.Damaged
        }

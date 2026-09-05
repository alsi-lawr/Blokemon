namespace Blokemon.App.Tests

open System
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product
open FsUnit
open TUnit.Core

type IssuerAdmissionTests() =

    let provider = "example"

    let viewer subject =
        identity provider subject "Viewer" SessionProvenance.Issuer

    let channel documents slug =
        task {
            let! admitted =
                TenantAdmission.admit
                    documents
                    documents
                    slug
                    slug
                    null
                    null
                    now
                    Unchecked.defaultof<_>

            return (succeeded admitted).Tenant
        }

    let admit documents tenant subject =
        IssuerAdmission.admit
            (services documents)
            documents
            (viewer subject)
            tenant
            now
            Unchecked.defaultof<_>

    let approvalOf (documents: MemoryDocumentStore) account tenant =
        task {
            let! loaded =
                Approvals.load documents account (Tenants.idOf tenant) Unchecked.defaultof<_>

            match succeeded loaded with
            | Some approval -> return Some approval.Document
            | None -> return None
        }

    let accountOf outcome =
        match outcome with
        | HandoffOutcome.Admitted issued -> issued.Session.Account
        | other -> failwith $"Expected admission, received {other}."

    [<Test>]
    member _.``a subject with no account should be created and the channel approved as its route``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! tenant = channel documents "alpha"
            let! outcome = admit documents tenant "viewer-1"
            let account = accountOf (succeeded outcome)
            let! approval = approvalOf documents account tenant

            (Option.get approval).Status |> should equal ApprovalStatus.Approved
            keysUnder documents "link/" |> should equal [ $"link/{provider}/viewer-1" ]

            let! routed =
                IssuerAdmission.hasLiveRoute documents documents account Unchecked.defaultof<_>

            routed |> should be True
        }

    [<Test>]
    member _.``an existing account should be pending for a new channel until approved``() =
        task {
            let documents = MemoryDocumentStore()
            let! alpha = channel documents "alpha"
            let! beta = channel documents "beta"
            let! gamma = channel documents "gamma"
            let! first = admit documents alpha "viewer-1"
            let account = accountOf (succeeded first)
            let sessionsBefore = keysUnder documents "session/"

            let! pending = admit documents beta "viewer-1"
            succeeded pending |> should equal HandoffOutcome.ApprovalPending
            keysUnder documents "session/" |> should equal sessionsBefore
            let! record = approvalOf documents account beta
            (Option.get record).Status |> should equal ApprovalStatus.Pending

            let! listed = IssuerAdmission.pending documents documents account Unchecked.defaultof<_>
            listed |> List.map (fun item -> item.Tenant.Slug) |> should equal [ "beta" ]

            let approver provenance tenant : Session =
                { Id = Guid.NewGuid().ToString "D"
                  Account = account
                  Tenant = Tenants.idOf tenant
                  Provenance = provenance
                  ExpiresAt = now.AddHours 8.0 }

            let approve session target =
                IssuerAdmission.approve
                    documents
                    documents
                    session
                    (Tenants.idOf target)
                    now
                    Unchecked.defaultof<_>

            let! recovery = approve (approver SessionProvenance.Recovery alpha) beta
            let! fromBeta = approve (approver SessionProvenance.Issuer beta) beta
            let! nothing = approve (approver SessionProvenance.FirstParty alpha) gamma
            let! already = approve (approver SessionProvenance.FirstParty alpha) alpha
            failed recovery |> should equal ApprovalRefusal.NotEligible
            failed fromBeta |> should equal ApprovalRefusal.NoRoute
            failed nothing |> should equal ApprovalRefusal.NothingPending
            succeeded already |> should equal ()

            let! fromAlpha = approve (approver SessionProvenance.Issuer alpha) beta
            succeeded fromAlpha |> should equal ()
            let! admitted = admit documents beta "viewer-1"
            accountOf (succeeded admitted) |> should equal account

            let! excludedApproval =
                Approvals.exclude documents account (Tenants.idOf beta) now Unchecked.defaultof<_>

            succeeded excludedApproval |> should equal ()
            let! excluded = admit documents beta "viewer-1"
            failed excluded |> should equal SignInFailure.TenantExcluded
            let! reapprove = approve (approver SessionProvenance.FirstParty alpha) beta
            failed reapprove |> should equal ApprovalRefusal.Excluded
        }

    [<Test>]
    member _.``only the default tenant should adopt and only an account with no passkey and no live route``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! core = Tenants.ensureDefault documents documents now Unchecked.defaultof<_>
            let! alpha = channel documents "alpha"
            let! beta = channel documents "beta"
            let! orphan = admit documents alpha "orphan"
            let! keyed = admit documents alpha "keyed"
            let orphanAccount = accountOf (succeeded orphan)
            let keyedAccount = accountOf (succeeded keyed)

            let! _ =
                Credentials.enrol
                    documents
                    documents
                    keyedAccount
                    "cred"
                    "key"
                    0u
                    SessionProvenance.Issuer
                    (Tenants.idOf alpha)
                    now
                    Unchecked.defaultof<_>

            let! routed = admit documents core "orphan"
            succeeded routed |> should equal HandoffOutcome.ApprovalPending

            let! _ =
                TenantAdmission.close
                    documents
                    documents
                    (Tenants.idOf alpha)
                    Unchecked.defaultof<_>

            let! byBeta = admit documents beta "orphan"
            succeeded byBeta |> should equal HandoffOutcome.ApprovalPending
            let! adopted = admit documents core "orphan"
            accountOf (succeeded adopted) |> should equal orphanAccount
            let! record = approvalOf documents orphanAccount core
            (Option.get record).Status |> should equal ApprovalStatus.Approved
            (Option.get record).AdoptedAt.HasValue |> should be True
            let! withKey = admit documents core "keyed"
            succeeded withKey |> should equal HandoffOutcome.ApprovalPending
        }

    [<Test>]
    member _.``a closed tenant's hand-off should be refused``() =
        task {
            let documents = MemoryDocumentStore()
            let! alpha = channel documents "alpha"

            let! closed =
                TenantAdmission.close
                    documents
                    documents
                    (Tenants.idOf alpha)
                    Unchecked.defaultof<_>

            let before = documents.Snapshot

            let! refused = admit documents (succeeded closed) "viewer-1"

            match failed refused with
            | SignInFailure.ProviderRefused error -> error.Code |> should equal "tenant.closed"
            | other -> failwith $"Expected the closed refusal, received {other}."

            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``the erasure relay should remove only the calling tenant's approval and be idempotent``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! alpha = channel documents "alpha"
            let! beta = channel documents "beta"
            let! first = admit documents alpha "viewer-1"
            let account = accountOf (succeeded first)
            let! _ = admit documents beta "viewer-1"

            let! _ =
                Approvals.approve
                    documents
                    account
                    (Tenants.idOf beta)
                    now
                    false
                    Unchecked.defaultof<_>

            let providerName = (viewer "viewer-1").Provider
            let subject = (viewer "viewer-1").Subject

            let relay tenant =
                IssuerAdmission.relayErasure
                    documents
                    providerName
                    subject
                    (Tenants.idOf tenant)
                    Unchecked.defaultof<_>

            let! relayed = relay alpha
            succeeded relayed |> should be True

            keysUnder documents "approval/"
            |> should equal [ approvalKey account (Tenants.idOf beta) ]

            keysUnder documents "link/" |> should equal [ $"link/{provider}/viewer-1" ]
            keysUnder documents "account/" |> should equal [ accountKey account ]

            let! again = relay alpha
            succeeded again |> should be False

            let! unknown =
                IssuerAdmission.relayErasure
                    documents
                    providerName
                    (match ExternalSubject.Create "nobody" with
                     | DomainResult.Succeeded s -> s
                     | DomainResult.Failed f -> failwith $"{f}")
                    (Tenants.idOf alpha)
                    Unchecked.defaultof<_>

            succeeded unknown |> should be False

            // An excluded record keeps its exclusion and loses its approval.
            let! _ =
                Approvals.exclude documents account (Tenants.idOf beta) now Unchecked.defaultof<_>

            let! dissociated = relay beta
            succeeded dissociated |> should be True
            let! record = approvalOf documents account beta
            (Option.get record).Status |> should equal ApprovalStatus.Pending
            (Option.get record).ExcludedAt.HasValue |> should be True
        }

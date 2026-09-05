namespace Blokemon.App.Tests

open System
open System.Text.Json
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product
open FsUnit
open TUnit.Core

type RolesTests() =

    let linkProvider =
        match IdentityProviderName.Create "example" with
        | DomainResult.Succeeded name -> name
        | DomainResult.Failed failure -> failwith $"{failure}"

    let slug text =
        match TenantSlug.Create text with
        | DomainResult.Succeeded slug -> slug
        | DomainResult.Failed failure -> failwith $"{failure}"

    let create (documents: MemoryDocumentStore) key value =
        task {
            let! _ =
                (documents :> IStateDocumentStore)
                    .Create(key, JsonSerializer.Serialize(value, json))

            return ()
        }

    /// A channel tenant whose broadcaster subject the operator recorded at admission.
    let channel documents (name: string) (broadcaster: string | null) =
        task {
            let id = TenantId.Mint()

            let tenant =
                { newTenant id (slug name) name now with
                    BroadcasterSubject = broadcaster }

            do! create documents (tenantKey id) tenant
            return tenant
        }

    let link documents (subject: string) (account: AccountId) =
        task {
            let subject =
                match ExternalSubject.Create subject with
                | DomainResult.Succeeded subject -> subject
                | DomainResult.Failed failure -> failwith $"{failure}"

            let! _ =
                IdentityLinks.create
                    documents
                    { Provider = linkProvider
                      Subject = subject
                      Account = account }
                    now
                    Unchecked.defaultof<_>

            return ()
        }

    let session account tenant provenance : Session =
        { Id = Guid.NewGuid().ToString "D"
          Account = account
          Tenant = tenant
          Provenance = provenance
          ExpiresAt = now + lifetime }

    let isOwner documents session tenant =
        Roles.isOwner documents linkProvider session tenant Unchecked.defaultof<_>

    [<Test>]
    member _.``owner authority in a channel tenant should need the broadcaster link and that tenant's own issuer session or an approved first party session``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let broadcaster = AccountId.Mint()
            let viewer = AccountId.Mint()
            let! tenant = channel documents "the-regular" "1001"
            let! other = channel documents "night-shift" "2002"
            let tenantId = Tenants.idOf tenant
            do! link documents "1001" broadcaster
            do! link documents "2002" viewer

            let! issuedHere =
                isOwner documents (session broadcaster tenantId SessionProvenance.Issuer) tenant

            let! issuedElsewhere =
                isOwner
                    documents
                    (session broadcaster (Tenants.idOf other) SessionProvenance.Issuer)
                    tenant

            let! firstPartyUnrecorded =
                isOwner documents (session broadcaster tenantId SessionProvenance.FirstParty) tenant

            let! _ = Approvals.ensurePending documents broadcaster tenantId Unchecked.defaultof<_>

            let! firstPartyPending =
                isOwner documents (session broadcaster tenantId SessionProvenance.FirstParty) tenant

            let! _ =
                Approvals.approve documents broadcaster tenantId now false Unchecked.defaultof<_>

            let! firstPartyApproved =
                isOwner
                    documents
                    (session broadcaster (TenantId.Mint()) SessionProvenance.FirstParty)
                    tenant

            let! recovery =
                isOwner documents (session broadcaster tenantId SessionProvenance.Recovery) tenant

            let! notLinked =
                isOwner documents (session viewer tenantId SessionProvenance.Issuer) tenant

            let! noBroadcaster =
                task {
                    let! bare = channel documents "bare" null

                    return!
                        isOwner
                            documents
                            (session broadcaster (Tenants.idOf bare) SessionProvenance.Issuer)
                            bare
                }

            issuedHere |> should be True
            issuedElsewhere |> should be False
            firstPartyUnrecorded |> should be False
            firstPartyPending |> should be False
            firstPartyApproved |> should be True
            recovery |> should be False
            notLinked |> should be False
            noBroadcaster |> should be False
        }

    [<Test>]
    member _.``a subject claimed through another channel should confer nothing in the broadcaster's tenant``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let claimant = AccountId.Mint()
            let! tenant = channel documents "the-regular" "1001"
            let! claiming = channel documents "claiming" "3003"
            // The claiming channel handed off "1001" first, so the link names its viewer.
            do! link documents "1001" claimant

            let! viaClaimingIssuer =
                isOwner
                    documents
                    (session claimant (Tenants.idOf claiming) SessionProvenance.Issuer)
                    tenant

            let! viaFirstParty =
                isOwner
                    documents
                    (session claimant (Tenants.idOf tenant) SessionProvenance.FirstParty)
                    tenant

            // Only the broadcaster's tenant can approve its own record, through its own hand-off.
            let! _ =
                Approvals.ensurePending
                    documents
                    claimant
                    (Tenants.idOf tenant)
                    Unchecked.defaultof<_>

            let! viaFirstPartyPending =
                isOwner
                    documents
                    (session claimant (Tenants.idOf tenant) SessionProvenance.FirstParty)
                    tenant

            viaClaimingIssuer |> should be False
            viaFirstParty |> should be False
            viaFirstPartyPending |> should be False
        }

    [<Test>]
    member _.``the default tenant's owner should be the assigned account with a first party or its own issuer session``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! core = Tenants.ensureDefault documents documents now Unchecked.defaultof<_>
            let owner = AccountId.Mint()
            let other = AccountId.Mint()
            do! create documents (accountKey owner) (newAccount owner now)
            let! channel = channel documents "the-regular" "1001"

            let! unassigned =
                isOwner
                    documents
                    (session owner (Tenants.idOf core) SessionProvenance.FirstParty)
                    core

            let! assigned =
                AccountLifecycle.assignOwner
                    documents
                    (Tenants.idOf core)
                    owner
                    Unchecked.defaultof<_>

            let core = succeeded assigned

            let! firstParty =
                isOwner
                    documents
                    (session owner (Tenants.idOf channel) SessionProvenance.FirstParty)
                    core

            let! coreIssuer =
                isOwner documents (session owner (Tenants.idOf core) SessionProvenance.Issuer) core

            let! channelIssuer =
                isOwner
                    documents
                    (session owner (Tenants.idOf channel) SessionProvenance.Issuer)
                    core

            let! someoneElse =
                isOwner
                    documents
                    (session other (Tenants.idOf core) SessionProvenance.FirstParty)
                    core

            let! onAChannel =
                AccountLifecycle.assignOwner
                    documents
                    (Tenants.idOf channel)
                    owner
                    Unchecked.defaultof<_>

            let! owned =
                Roles.ownedTenants
                    documents
                    documents
                    linkProvider
                    (session owner (Tenants.idOf core) SessionProvenance.FirstParty)
                    Unchecked.defaultof<_>

            unassigned |> should be False
            firstParty |> should be True
            coreIssuer |> should be True
            channelIssuer |> should be False
            someoneElse |> should be False
            failed onAChannel |> should equal LifecycleFailure.NotDefaultTenant
            owned |> List.map (fun tenant -> tenant.Slug) |> should equal [ "core" ]
        }

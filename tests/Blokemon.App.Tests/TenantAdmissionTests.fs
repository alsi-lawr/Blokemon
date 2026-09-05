namespace Blokemon.App.Tests

open System
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.Product
open FsUnit
open TUnit.Core

type TenantAdmissionTests() =

    let admit
        documents
        (slug: string | null)
        (label: string | null)
        (broadcaster: string | null)
        (origin: string | null)
        =
        TenantAdmission.admit
            documents
            documents
            slug
            label
            broadcaster
            origin
            now
            Unchecked.defaultof<_>

    let authenticate documents (token: string | null) =
        TenantAdmission.authenticate documents token Unchecked.defaultof<_>

    [<Test>]
    member _.``admitting a channel should record its slots and mint a token that authenticates it``
        ()
        =
        task {
            let documents = MemoryDocumentStore()

            let! admitted =
                admit documents "the-regular" " The Regular " "1001" "https://parent.example/"

            let channel = succeeded admitted

            channel.Tenant.Slug |> should equal "the-regular"
            channel.Tenant.DisplayLabel |> should equal "The Regular"
            channel.Tenant.BroadcasterSubject |> should equal "1001"
            channel.Tenant.RegisteredParentOrigin |> should equal "https://parent.example"
            channel.Tenant.Status |> should equal TenantStatus.Active

            let! authenticated = authenticate documents channel.Token

            match authenticated with
            | ChannelAuthentication.Authenticated tenant ->
                tenant.Id |> should equal channel.Tenant.Id
            | other -> failwith $"Expected authentication, received {other}."

            let! noToken = authenticate documents null
            let! unknown = authenticate documents $"blkm_{channel.Tenant.Id}_wrong"
            let! nobody = authenticate documents $"blkm_{TenantId.Mint()}_wrong"
            noToken |> should equal ChannelAuthentication.NoToken
            unknown |> should equal ChannelAuthentication.Unknown
            nobody |> should equal ChannelAuthentication.Unknown
        }

    [<Test>]
    member _.``admission should refuse a reserved taken or malformed slug and bad label subject or origin``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! _ = admit documents "taken" "Taken" null null
            let before = documents.Snapshot

            let! reserved = admit documents "handoff" "Handoff" null null
            let! taken = admit documents "taken" "Again" null null
            let! malformed = admit documents "Not A Slug" "x" null null
            let! label = admit documents "fresh" "   " null null
            let! subject = admit documents "fresh" "Fresh" "not valid!" null
            let! origin = admit documents "fresh" "Fresh" null "parent.example"
            let! path = admit documents "fresh" "Fresh" null "https://parent.example/page"

            failed reserved
            |> should equal (AdmissionFailure.SlugInvalid TenantSlugFailure.Reserved)

            failed taken |> should equal AdmissionFailure.SlugTaken

            failed malformed
            |> should equal (AdmissionFailure.SlugInvalid TenantSlugFailure.Malformed)

            failed label |> should equal AdmissionFailure.LabelInvalid
            failed subject |> should equal AdmissionFailure.SubjectInvalid
            failed origin |> should equal AdmissionFailure.OriginInvalid
            failed path |> should equal AdmissionFailure.OriginInvalid
            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``admitting the default slug should mint the default tenant's token and keep its label``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! existing = Tenants.ensureDefault documents documents now Unchecked.defaultof<_>
            let! admitted = admit documents "core" "Ignored" "2002" "https://bot.example"
            let core = succeeded admitted

            core.Tenant.Id |> should equal existing.Id
            core.Tenant.DisplayLabel |> should equal Tenants.DefaultLabel
            core.Tenant.BroadcasterSubject |> should equal "2002"
            core.Tenant.RegisteredParentOrigin |> should equal "https://bot.example"
            keysUnder documents "tenant/" |> should haveLength 1
        }

    [<Test>]
    member _.``rotation should invalidate the previous token and re-admit a closed tenant``() =
        task {
            let documents = MemoryDocumentStore()
            let! admitted = admit documents "rotating" "Rotating" null null
            let channel = succeeded admitted
            let tenant = Tenants.idOf channel.Tenant

            let! rotated =
                TenantAdmission.rotate documents tenant (now.AddHours 1.0) Unchecked.defaultof<_>

            let fresh = succeeded rotated
            let! old = authenticate documents channel.Token
            let! current = authenticate documents fresh.Token
            old |> should equal ChannelAuthentication.Unknown
            current.IsAuthenticated |> should be True

            let! _ = TenantAdmission.close documents documents tenant Unchecked.defaultof<_>
            let! closed = authenticate documents fresh.Token
            closed |> should equal ChannelAuthentication.Closed

            let! readmitted =
                TenantAdmission.rotate documents tenant (now.AddHours 2.0) Unchecked.defaultof<_>

            (succeeded readmitted).Tenant.Status |> should equal TenantStatus.Active
            let! again = authenticate documents (succeeded readmitted).Token
            again.IsAuthenticated |> should be True
        }

    [<Test>]
    member _.``closing and revoking should revoke the tenant's sessions and leave the others``() =
        task {
            let documents = MemoryDocumentStore()
            let! admitted = admit documents "closing" "Closing" null null
            let! other = admit documents "staying" "Staying" null null
            let tenant = Tenants.idOf (succeeded admitted).Tenant
            let otherTenant = Tenants.idOf (succeeded other).Tenant
            let account = AccountId.Mint()

            let issue holder provenance =
                Sessions.issue
                    documents
                    account
                    holder
                    provenance
                    now
                    lifetime
                    Unchecked.defaultof<_>

            let! _ = issue tenant SessionProvenance.Issuer
            let! kept = issue otherTenant SessionProvenance.Issuer
            // The person's own passkey session acting in the closing tenant is not the tenant's.
            let! own = issue tenant SessionProvenance.FirstParty

            let! closed = TenantAdmission.close documents documents tenant Unchecked.defaultof<_>
            (succeeded closed).Status |> should equal TenantStatus.Closed
            (succeeded closed).IntegrationTokenVerifier |> should be Null

            keysUnder documents "session/"
            |> should
                equal
                (List.sort [ $"session/{kept.Session.Id}"; $"session/{own.Session.Id}" ])

            let! closedAgain =
                TenantAdmission.close documents documents tenant Unchecked.defaultof<_>

            (succeeded closedAgain).Status |> should equal TenantStatus.Closed

            let! revoked = TenantAdmission.revoke documents documents tenant Unchecked.defaultof<_>
            (succeeded revoked).Status |> should equal TenantStatus.Revoked
            let! rotate = TenantAdmission.rotate documents tenant now Unchecked.defaultof<_>
            let! close = TenantAdmission.close documents documents tenant Unchecked.defaultof<_>
            failed rotate |> should equal AdmissionFailure.Revoked
            failed close |> should equal AdmissionFailure.Revoked
            let! authenticated = authenticate documents (succeeded admitted).Token
            authenticated |> should equal ChannelAuthentication.Revoked
        }

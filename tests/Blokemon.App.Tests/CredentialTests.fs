namespace Blokemon.App.Tests

open System
open Blokemon.App
open Blokemon.Product
open FsUnit
open TUnit.Core

type CredentialTests() =

    let enrol (documents: MemoryDocumentStore) account credentialId provenance tenant =
        Credentials.enrol
            documents
            documents
            account
            credentialId
            "cHVibGljLWtleQ=="
            0u
            provenance
            tenant
            now
            Unchecked.defaultof<_>

    [<Test>]
    member _.``enrolling a passkey should record its provenance and tenant and be found by account and credential id``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let other = AccountId.Mint()
            let tenant = TenantId.Mint()

            let! first = enrol documents account "cred-one" SessionProvenance.FirstParty null
            let! second = enrol documents account "cred-two" SessionProvenance.Issuer tenant

            let! found =
                Credentials.find documents documents account "cred-two" Unchecked.defaultof<_>

            let! missing =
                Credentials.find documents documents other "cred-two" Unchecked.defaultof<_>

            let! any = Credentials.anyFor documents documents account Unchecked.defaultof<_>
            let! none = Credentials.anyFor documents documents other Unchecked.defaultof<_>

            (succeeded first).Provenance |> should equal SessionProvenance.FirstParty
            (succeeded first).Tenant |> should be Null
            (succeeded second).Tenant |> should equal tenant.Value
            (Option.get found).Document.Provenance |> should equal SessionProvenance.Issuer
            (Option.get found).Document.Account |> should equal account.Value
            missing |> should equal None
            any |> should be True
            none |> should be False

            keysUnder documents "credential/"
            |> should
                equal
                (List.sort
                    [ Credentials.key account (succeeded first).Id
                      Credentials.key account (succeeded second).Id ])
        }

    [<Test>]
    member _.``a credential id already enrolled on the account should be refused and nothing written``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let! _ = enrol documents account "cred-one" SessionProvenance.FirstParty null
            let before = documents.Snapshot

            let! refused = enrol documents account "cred-one" SessionProvenance.FirstParty null

            failed refused |> should equal CredentialFailure.AlreadyEnrolled
            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``recording a sign count should write against the revision it was read at``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let! _ = enrol documents account "cred-one" SessionProvenance.FirstParty null

            let! loaded =
                Credentials.find documents documents account "cred-one" Unchecked.defaultof<_>

            let loaded = Option.get loaded

            let! first = Credentials.recordSignCount documents loaded 7u Unchecked.defaultof<_>
            let! stale = Credentials.recordSignCount documents loaded 8u Unchecked.defaultof<_>

            let! reread =
                Credentials.find documents documents account "cred-one" Unchecked.defaultof<_>

            succeeded first |> should equal ()
            failed stale |> should equal CredentialFailure.Conflict
            (Option.get reread).Document.SignCount |> should equal 7u
            (Option.get reread).Revision |> should equal (loaded.Revision + 1L)
        }

    [<Test>]
    member _.``every credential and recovery key should be within the widened bound``() =
        let account = AccountId.Mint()
        let id = Guid.NewGuid().ToString "D"
        (Credentials.key account id).Length |> should equal 84
        (Credentials.key account id).Length |> should be (lessThanOrEqualTo 160)
        (RecoveryCodes.key account).Length |> should equal 45

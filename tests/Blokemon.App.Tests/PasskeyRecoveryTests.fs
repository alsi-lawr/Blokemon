namespace Blokemon.App.Tests

open Blokemon.App
open Blokemon.Product
open FsUnit
open TUnit.Core

type PasskeyRecoveryTests() =

    let signedIn (documents: MemoryDocumentStore) subject tenant =
        task {
            let identity = identity "example" subject "Player" SessionProvenance.FirstParty

            let! issued =
                SignInCompletion.complete
                    (services documents)
                    identity
                    tenant
                    now
                    Unchecked.defaultof<_>

            return succeeded issued
        }

    let recover (documents: MemoryDocumentStore) tenant (code: string | null) =
        PasskeyRecovery.recover documents documents tenant code now lifetime Unchecked.defaultof<_>

    [<Test>]
    member _.``recovering with a code should revoke the account's sessions and issue a recovery session``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let! first = signedIn documents "one" tenant
            let! second = signedIn documents "one" tenant
            let! bystander = signedIn documents "two" tenant
            let account = first.Session.Account
            let! codes = RecoveryCodes.issue documents account now Unchecked.defaultof<_>
            let codes = succeeded codes

            let! recovered = recover documents tenant codes[0]
            let recovered = succeeded recovered
            let! firstAgain = Sessions.validate documents first.Token now Unchecked.defaultof<_>
            let! secondAgain = Sessions.validate documents second.Token now Unchecked.defaultof<_>

            let! bystanderAgain =
                Sessions.validate documents bystander.Token now Unchecked.defaultof<_>

            let! recovery = Sessions.validate documents recovered.Token now Unchecked.defaultof<_>

            recovered.Session.Account |> should equal account
            recovered.Session.Provenance |> should equal SessionProvenance.Recovery
            recovered.Session.Tenant |> should equal tenant
            firstAgain |> should equal SessionValidation.Required
            secondAgain |> should equal SessionValidation.Required
            bystanderAgain.IsValid |> should be True
            recovery.IsValid |> should be True

            keysUnder documents "session/"
            |> should
                equal
                (List.sort [ Sessions.key bystander.Session.Id; Sessions.key recovered.Session.Id ])
        }

    [<Test>]
    member _.``a disabled account's code should be refused and left unspent``() =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let! signedIn = signedIn documents "one" tenant
            let account = signedIn.Session.Account
            let! codes = RecoveryCodes.issue documents account now Unchecked.defaultof<_>
            let codes = succeeded codes
            let! record = readAccount documents account

            do!
                writeAccount
                    documents
                    account
                    { record with
                        Status = AccountStatus.Disabled }

            let before = documents.Snapshot

            let! refused = recover documents tenant codes[0]

            failed refused |> should equal RecoveryFailure.Refused
            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``an unknown code should be refused with no session issued``() =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let! _ = signedIn documents "one" tenant
            let before = documents.Snapshot

            let! refused = recover documents tenant "00000000-00000000-00000000-00000000"

            failed refused |> should equal RecoveryFailure.Refused
            documents.Snapshot |> should equal before
        }

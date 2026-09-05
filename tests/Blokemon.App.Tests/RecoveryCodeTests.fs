namespace Blokemon.App.Tests

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.Product
open FsUnit
open TUnit.Core

/// A store in which a second use of the same code lands between this use's read and its write:
/// the interleaving two concurrent recoveries produce.
type private ConsumingRacingStore(inner: MemoryDocumentStore, code: string) =

    let store = inner :> IStateDocumentStore
    let mutable raced = false

    interface IStateDocumentStore with
        member _.Read(key, cancellationToken) = store.Read(key, cancellationToken)

        member _.Create(key, json, cancellationToken) =
            store.Create(key, json, cancellationToken)

        member _.Update(key, revision, json, cancellationToken) =
            task {
                if not raced && key.StartsWith("recovery/", StringComparison.Ordinal) then
                    raced <- true

                    let! located = RecoveryCodes.locate store inner code cancellationToken

                    match located with
                    | Some(key, set, index) ->
                        let! _ = RecoveryCodes.consumeFrom store key set index now cancellationToken
                        ()
                    | None -> ()

                return! store.Update(key, revision, json, cancellationToken)
            }

        member _.Delete(key, cancellationToken) = store.Delete(key, cancellationToken)

        member _.DeleteIfUnchanged(key, revision, json, cancellationToken) =
            store.DeleteIfUnchanged(key, revision, json, cancellationToken)

type RecoveryCodeTests() =

    let issue (documents: IStateDocumentStore) account =
        task {
            let! issued = RecoveryCodes.issue documents account now Unchecked.defaultof<_>
            return succeeded issued
        }

    let recover (documents: IStateDocumentStore) (listing: IDocumentListing) (code: string | null) =
        task {
            let! located = RecoveryCodes.locate documents listing code Unchecked.defaultof<_>

            match located with
            | None -> return DomainResult.Failed RecoveryFailure.Refused
            | Some(key, set, index) ->
                let! consumed =
                    RecoveryCodes.consumeFrom documents key set index now Unchecked.defaultof<_>

                match consumed with
                | DomainResult.Succeeded() ->
                    return DomainResult.Succeeded(succeeded (AccountId.Create set.Document.Account))
                | DomainResult.Failed failure -> return DomainResult.Failed failure
        }

    [<Test>]
    member _.``issuing a set should produce ten distinct codes of sixteen random bytes stored only as hashes``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()

            let! codes = issue documents account
            let stored = documents.Snapshot[RecoveryCodes.key account].Json

            let document =
                Unchecked.nonNull (
                    JsonSerializer.Deserialize<RecoveryCodeDocument>(stored, TenancyDocuments.json)
                )

            codes.Length |> should equal RecoveryCodes.SetSize
            RecoveryCodes.CodeBytes * 8 |> should be (greaterThanOrEqualTo 128)
            codes |> Array.distinct |> Array.length |> should equal RecoveryCodes.SetSize
            document.Codes.Length |> should equal RecoveryCodes.SetSize

            for index, code in Array.indexed codes do
                let normalized = Option.get (RecoveryCodes.normalize code)
                normalized.Length |> should equal (RecoveryCodes.CodeBytes * 2)

                stored.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                |> should be False

                document.Codes[index].Hash |> should equal (RecoveryCodes.hash normalized)
                document.Codes[index].ConsumedAt.HasValue |> should be False

            let! live = RecoveryCodes.hasLive documents account Unchecked.defaultof<_>
            live |> should be True
        }

    [<Test>]
    member _.``a presented code should be read without regard to dashes case or spaces``() =
        let plain = "0123456789abcdef0123456789ABCDEF"

        RecoveryCodes.normalize "01234567-89abcdef-01234567-89ABCDEF"
        |> should equal (Some(plain.ToLowerInvariant()))

        RecoveryCodes.normalize " 01234567 89abcdef 01234567 89abcdef "
        |> should equal (Some(plain.ToLowerInvariant()))

        RecoveryCodes.normalize "0123456789abcdef0123456789abcde" |> should equal None
        RecoveryCodes.normalize "0123456789abcdef0123456789abcdeg" |> should equal None
        RecoveryCodes.normalize null |> should equal None
        RecoveryCodes.normalize "" |> should equal None

    [<Test>]
    member _.``consuming a code should mark it used name its account and refuse a second use``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let other = AccountId.Mint()
            let! codes = issue documents account
            let! _ = issue documents other

            let! first = recover documents documents codes[3]
            let! again = recover documents documents codes[3]
            let! loaded = RecoveryCodes.load documents account Unchecked.defaultof<_>
            let set = Option.get (succeeded loaded)

            succeeded first |> should equal account
            failed again |> should equal RecoveryFailure.Refused
            RecoveryCodes.liveCount set.Document |> should equal (RecoveryCodes.SetSize - 1)
            set.Document.Codes[3].ConsumedAt |> should equal (Nullable now)
        }

    [<Test>]
    member _.``two concurrent uses of one code should succeed exactly once``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let! codes = issue documents account
            let racing = ConsumingRacingStore(documents, codes[0])

            let! outcome = recover racing documents codes[0]
            let! loaded = RecoveryCodes.load documents account Unchecked.defaultof<_>

            // The interposed use won; this one re-read the set, found the code spent and refused.
            failed outcome |> should equal RecoveryFailure.Refused

            RecoveryCodes.liveCount (Option.get (succeeded loaded)).Document
            |> should equal (RecoveryCodes.SetSize - 1)
        }

    [<Test>]
    member _.``two concurrent uses of different codes should both succeed``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let! codes = issue documents account
            let racing = ConsumingRacingStore(documents, codes[1])

            let! outcome = recover racing documents codes[0]
            let! loaded = RecoveryCodes.load documents account Unchecked.defaultof<_>

            succeeded outcome |> should equal account

            RecoveryCodes.liveCount (Option.get (succeeded loaded)).Document
            |> should equal (RecoveryCodes.SetSize - 2)
        }

    [<Test>]
    member _.``a code from a previous set should be refused after regeneration``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let! previous = issue documents account

            let! replacement = issue documents account
            let! stale = recover documents documents previous[0]
            let! fresh = recover documents documents replacement[0]

            failed stale |> should equal RecoveryFailure.Refused
            succeeded fresh |> should equal account
            keysUnder documents "recovery/" |> should equal [ RecoveryCodes.key account ]
        }

    [<Test>]
    member _.``a malformed or unknown code should be refused and nothing written``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let! _ = issue documents account
            let before = documents.Snapshot

            let! malformed = recover documents documents "not-a-code"
            let! unknown = recover documents documents (String('0', 32))

            failed malformed |> should equal RecoveryFailure.Refused
            failed unknown |> should equal RecoveryFailure.Refused
            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``a damaged set should be reported and never consumed``() =
        task {
            let documents = MemoryDocumentStore()
            let store = documents :> IStateDocumentStore
            let account = AccountId.Mint()
            let! _ = store.Create(RecoveryCodes.key account, "not json")

            let! loaded = RecoveryCodes.load documents account Unchecked.defaultof<_>

            let! located =
                RecoveryCodes.locate documents documents (String('0', 32)) Unchecked.defaultof<_>

            failed loaded |> should equal RecoveryFailure.Damaged
            located |> should equal None
        }

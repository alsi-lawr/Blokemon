namespace Blokemon.App.Tests

open System
open System.Text.Json
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product
open FsUnit
open TUnit.Core

type HandoffCodeTests() =

    let tenant = TenantId.Mint()

    let subject =
        match ExternalSubject.Create "viewer-1" with
        | DomainResult.Succeeded subject -> subject
        | DomainResult.Failed failure -> failwith $"{failure}"

    let channel = HandoffBinding.Channel(tenant, subject, "Viewer")

    let session: Session =
        { Id = Guid.NewGuid().ToString "D"
          Account = AccountId.Mint()
          Tenant = tenant
          Provenance = SessionProvenance.Issuer
          ExpiresAt = now.AddHours 8.0 }

    let consume documents code kind at =
        HandoffCodes.consume documents code kind tenant at Unchecked.defaultof<_>

    [<Test>]
    member _.``a minted code should be stored as a hash with its kind and binding and consumed exactly once``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! issued = HandoffCodes.mint documents channel now Unchecked.defaultof<_>
            let secret = issued.Code.Split('.')[1]
            let store = documents :> IStateDocumentStore
            let! stored = store.Read(HandoffCodes.key issued.Id)

            issued.ExpiresAt |> should equal (now + HandoffCodes.Lifetime)
            secret.Length |> should be (greaterThanOrEqualTo 32)
            let record = Unchecked.nonNull stored
            record.Json.Contains secret |> should be False

            let document =
                Unchecked.nonNull (JsonSerializer.Deserialize<HandoffDocument>(record.Json, json))

            document.Kind |> should equal HandoffKind.Channel
            document.Tenant |> should equal tenant.Value
            document.Subject |> should equal "viewer-1"
            document.DisplayNameHint |> should equal "Viewer"

            let! first = consume documents issued.Code HandoffKind.Channel (now.AddSeconds 59.0)
            let! second = consume documents issued.Code HandoffKind.Channel (now.AddSeconds 59.0)

            (succeeded first).Id |> should equal issued.Id
            failed second |> should equal HandoffFailure.Refused
            keysUnder documents "handoff/" |> should be Empty
        }

    [<Test>]
    member _.``an expired malformed wrong kind or other tenant code should be refused and nothing written``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! code = HandoffCodes.mint documents channel now Unchecked.defaultof<_>

            let! continuation =
                HandoffCodes.mint
                    documents
                    (HandoffBinding.Continuation session)
                    now
                    Unchecked.defaultof<_>

            let before = documents.Snapshot

            let! expired = consume documents code.Code HandoffKind.Channel (now.AddSeconds 60.0)
            let! malformed = consume documents "not-a-code" HandoffKind.Channel now
            let! unknown = consume documents $"{Guid.NewGuid():D}.secret" HandoffKind.Channel now
            let! wrongSecret = consume documents $"{code.Id}.wrong" HandoffKind.Channel now
            let! wrongKind = consume documents continuation.Code HandoffKind.Channel now
            let! otherWay = consume documents code.Code HandoffKind.Continuation now

            let! otherTenant =
                HandoffCodes.consume
                    documents
                    code.Code
                    HandoffKind.Channel
                    (TenantId.Mint())
                    now
                    Unchecked.defaultof<_>

            failed expired |> should equal HandoffFailure.Expired
            failed malformed |> should equal HandoffFailure.Refused
            failed unknown |> should equal HandoffFailure.Refused
            failed wrongSecret |> should equal HandoffFailure.Refused
            failed wrongKind |> should equal HandoffFailure.WrongKind
            failed otherWay |> should equal HandoffFailure.WrongKind
            failed otherTenant |> should equal HandoffFailure.OtherTenant
            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``peeking at a code should read its binding under the same checks and spend nothing``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! issued = HandoffCodes.mint documents channel now Unchecked.defaultof<_>

            let peek code kind at =
                HandoffCodes.peek documents code kind tenant at Unchecked.defaultof<_>

            let! seen = peek issued.Code HandoffKind.Channel (now.AddSeconds 59.0)
            let! again = peek issued.Code HandoffKind.Channel (now.AddSeconds 59.0)
            let! wrongKind = peek issued.Code HandoffKind.Continuation now
            let! expired = peek issued.Code HandoffKind.Channel (now.AddSeconds 60.0)
            let! malformed = peek "not-a-code" HandoffKind.Channel now

            (succeeded seen).Subject |> should equal subject.Value
            (succeeded again).Subject |> should equal subject.Value
            failed wrongKind |> should equal HandoffFailure.WrongKind
            failed expired |> should equal HandoffFailure.Expired
            failed malformed |> should equal HandoffFailure.Refused
            (keysUnder documents "handoff/").Length |> should equal 1

            let! consumed = consume documents issued.Code HandoffKind.Channel (now.AddSeconds 59.0)
            succeeded consumed |> ignore
            let! gone = peek issued.Code HandoffKind.Channel now
            failed gone |> should equal HandoffFailure.Refused
        }

    [<Test>]
    member _.``a continuation should carry the session's account tenant and provenance``() =
        task {
            let documents = MemoryDocumentStore()

            let! issued =
                HandoffCodes.mint
                    documents
                    (HandoffBinding.Continuation session)
                    now
                    Unchecked.defaultof<_>

            let! consumed = consume documents issued.Code HandoffKind.Continuation now
            let document = succeeded consumed

            document.Kind |> should equal HandoffKind.Continuation
            document.Account |> should equal session.Account.Value
            document.Tenant |> should equal tenant.Value
            document.Provenance |> should equal (Nullable SessionProvenance.Issuer)
            document.Subject |> should be Null
        }

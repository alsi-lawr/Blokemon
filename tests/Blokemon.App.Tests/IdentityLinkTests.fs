namespace Blokemon.App.Tests

open System
open System.Threading
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.Product
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private LinkFixtures =

    let succeeded (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Succeeded value -> value
        | DomainResult.Failed error -> failwith $"Expected success, received {error}."

    let failed (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Failed error -> error
        | DomainResult.Succeeded value -> failwith $"Expected failure, received {value}."

    let provider = succeeded (IdentityProviderName.Create "example")

    let subject = succeeded (ExternalSubject.Create "44556677")

    let link (account: AccountId) : ExternalIdentityLink =
        { Provider = provider
          Subject = subject
          Account = account }

    let linkedAt = DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)

type IdentityLinkTests() =

    [<Test>]
    member _.``linking the same provider subject twice should fail and mutate nothing``() =
        task {
            let documents = MemoryDocumentStore()
            let first = AccountId.Mint()
            let second = AccountId.Mint()

            let! created =
                IdentityLinks.create documents (link first) linkedAt CancellationToken.None

            let after = documents.Snapshot

            let! duplicate =
                IdentityLinks.create
                    documents
                    (link second)
                    (linkedAt.AddDays 1.0)
                    CancellationToken.None

            let! resolved = IdentityLinks.resolve documents provider subject CancellationToken.None

            succeeded created |> should equal ()
            failed duplicate |> should equal IdentityLinkFailure.AlreadyLinked
            documents.Snapshot |> should equal after
            resolved |> should equal (LinkResolution.Linked first)
        }

    [<Test>]
    member _.``resolving an unlinked subject should say so and a damaged link should not resolve``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let store = documents :> IStateDocumentStore

            let! unlinked = IdentityLinks.resolve documents provider subject CancellationToken.None

            let! _ = store.Create($"link/{provider}/{subject}", "not json")
            let! damaged = IdentityLinks.resolve documents provider subject CancellationToken.None

            unlinked |> should equal LinkResolution.Unlinked
            damaged |> should equal LinkResolution.Damaged
        }

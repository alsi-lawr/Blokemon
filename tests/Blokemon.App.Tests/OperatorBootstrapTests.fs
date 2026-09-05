namespace Blokemon.App.Tests

open System
open Blokemon.App
open Blokemon.Product
open FsUnit
open TUnit.Core

type OperatorBootstrapTests() =

    let code = "correct-horse-battery-staple"

    let signedIn (documents: MemoryDocumentStore) provenance =
        task {
            let identity = identity "example" "op" "Operator" provenance

            let! issued =
                SignInCompletion.complete
                    (services documents)
                    identity
                    (TenantId.Mint())
                    now
                    Unchecked.defaultof<_>

            return (succeeded issued).Session
        }

    let redeem documents (configured: string | null) session (presented: string | null) =
        OperatorBootstrap.redeem documents configured session presented now Unchecked.defaultof<_>

    [<Test>]
    member _.``the bootstrap code should be redeemable exactly once from a first party session``() =
        task {
            let documents = MemoryDocumentStore()
            let! session = signedIn documents SessionProvenance.FirstParty

            let! first = redeem documents code session code
            let! record = readAccount documents session.Account
            let! second = redeem documents code session code

            succeeded first |> should equal now
            record.Operator |> should be True
            documents.Keys |> should contain OperatorBootstrap.Key
            failed second |> should equal OperatorBootstrapFailure.Redeemed
        }

    [<Test>]
    member _.``an issuer or recovery session should be refused before any comparison``() =
        task {
            for provenance in [ SessionProvenance.Issuer; SessionProvenance.Recovery ] do
                let documents = MemoryDocumentStore()
                let! session = signedIn documents provenance
                let before = documents.Snapshot

                let! refused = redeem documents code session code

                failed refused |> should equal OperatorBootstrapFailure.FirstPartyRequired
                documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``a wrong code of any length should be refused and nothing written``() =
        task {
            let documents = MemoryDocumentStore()
            let! session = signedIn documents SessionProvenance.FirstParty
            let before = documents.Snapshot

            let presented: (string | null) list =
                [ null
                  ""
                  "correct-horse-battery-stapl"
                  "correct-horse-battery-staple!"
                  String('x', 28) ]

            for presented in presented do
                let! refused = redeem documents code session presented
                failed refused |> should equal OperatorBootstrapFailure.Refused

            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``no configured code should be unavailable``() =
        task {
            let documents = MemoryDocumentStore()
            let! session = signedIn documents SessionProvenance.FirstParty

            let! refused = redeem documents null session code

            failed refused |> should equal OperatorBootstrapFailure.NotConfigured
        }

    [<Test>]
    member _.``codes should compare equal only when identical``() =
        OperatorBootstrap.codesMatch code code |> should be True
        OperatorBootstrap.codesMatch code (code.ToUpperInvariant()) |> should be False
        OperatorBootstrap.codesMatch code (code + " ") |> should be False
        OperatorBootstrap.codesMatch code "" |> should be False

    [<Test>]
    member _.``the lockout should refuse after five failures in fifteen minutes and relent afterwards``
        ()
        =
        let lockout = FailureLockout.OnRecoveryTerms()
        let client = "203.0.113.7"
        let other = "203.0.113.8"

        for minute in 0..3 do
            lockout.RecordFailure(client, now.AddMinutes(float minute))
            lockout.IsLockedOut(client, now.AddMinutes(float minute)) |> should be False

        lockout.RecordFailure(client, now.AddMinutes 4.0)

        lockout.IsLockedOut(client, now.AddMinutes 4.0) |> should be True
        lockout.IsLockedOut(other, now.AddMinutes 4.0) |> should be False
        lockout.IsLockedOut(client, now.AddMinutes 14.9) |> should be True
        // The first failure ages out of the window; four remain.
        lockout.IsLockedOut(client, now.AddMinutes 15.0) |> should be False
        lockout.IsLockedOut(client, now.AddMinutes 30.0) |> should be False

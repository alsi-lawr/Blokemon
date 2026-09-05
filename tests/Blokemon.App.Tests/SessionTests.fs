namespace Blokemon.App.Tests

open System
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product
open FsUnit
open TUnit.Core

type SessionTests() =

    let account (documents: MemoryDocumentStore) =
        task {
            let account = AccountId.Mint()
            let store = documents :> IStateDocumentStore

            let! _ =
                store.Create(
                    accountKey account,
                    System.Text.Json.JsonSerializer.Serialize(newAccount account now, json)
                )

            return account
        }

    [<Test>]
    member _.``issuing a session should carry the account tenant and provenance with an absolute expiry``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! account = account documents
            let tenant = TenantId.Mint()

            let! issued =
                Sessions.issue
                    documents
                    account
                    tenant
                    SessionProvenance.Issuer
                    now
                    lifetime
                    Unchecked.defaultof<_>

            let! validation =
                Sessions.validate documents issued.Token (now.AddHours 7.0) Unchecked.defaultof<_>

            match validation with
            | SessionValidation.Valid session ->
                session.Account |> should equal account
                session.Tenant |> should equal tenant
                session.Provenance |> should equal SessionProvenance.Issuer
                session.ExpiresAt |> should equal (now + lifetime)
                session.Id |> should equal issued.Session.Id
            | other -> failwith $"Expected a valid session, received {other}."

            keysUnder documents "session/"
            |> should equal [ $"session/{issued.Session.Id}" ]
        }

    [<Test>]
    member _.``every provenance should be issued and read back``() =
        task {
            let documents = MemoryDocumentStore()
            let! account = account documents
            let tenant = TenantId.Mint()

            for provenance in
                [ SessionProvenance.FirstParty
                  SessionProvenance.Recovery
                  SessionProvenance.Issuer ] do
                let! issued =
                    Sessions.issue
                        documents
                        account
                        tenant
                        provenance
                        now
                        lifetime
                        Unchecked.defaultof<_>

                let! validation =
                    Sessions.validate documents issued.Token now Unchecked.defaultof<_>

                match validation with
                | SessionValidation.Valid session -> session.Provenance |> should equal provenance
                | other -> failwith $"Expected a valid session, received {other}."
        }

    [<Test>]
    member _.``a session should be refused from its expiry on and nothing should extend it``() =
        task {
            let documents = MemoryDocumentStore()
            let! account = account documents
            let tenant = TenantId.Mint()

            let! issued =
                Sessions.issue
                    documents
                    account
                    tenant
                    SessionProvenance.FirstParty
                    now
                    lifetime
                    Unchecked.defaultof<_>

            let before = documents.Snapshot
            // Presenting the session again and again is not a renewal.
            let! _ =
                Sessions.validate documents issued.Token (now.AddHours 1.0) Unchecked.defaultof<_>

            let! _ =
                Sessions.validate documents issued.Token (now.AddHours 7.9) Unchecked.defaultof<_>

            documents.Snapshot |> should equal before

            let! atExpiry =
                Sessions.validate documents issued.Token (now + lifetime) Unchecked.defaultof<_>

            let! after =
                Sessions.validate documents issued.Token (now.AddDays 1.0) Unchecked.defaultof<_>

            atExpiry |> should equal SessionValidation.Expired
            after |> should equal SessionValidation.Expired
        }

    [<Test>]
    member _.``the shipped default should expire within hours and the bound within a day``() =
        Sessions.DefaultLifetime |> should be (lessThan (TimeSpan.FromDays 1.0))
        Sessions.DefaultLifetime |> should be (greaterThan TimeSpan.Zero)
        Sessions.MaximumLifetime |> should equal (TimeSpan.FromDays 1.0)

    [<Test>]
    member _.``a revoked session should be refused thereafter``() =
        task {
            let documents = MemoryDocumentStore()
            let! account = account documents

            let! issued =
                Sessions.issue
                    documents
                    account
                    (TenantId.Mint())
                    SessionProvenance.FirstParty
                    now
                    lifetime
                    Unchecked.defaultof<_>

            do! Sessions.revoke documents issued.Session.Id Unchecked.defaultof<_>
            let! validation = Sessions.validate documents issued.Token now Unchecked.defaultof<_>

            validation |> should equal SessionValidation.Required
            keysUnder documents "session/" |> should be Empty
        }

    [<Test>]
    member _.``an absent malformed unknown or tampered token should be refused``() =
        task {
            let documents = MemoryDocumentStore()
            let! account = account documents

            let! issued =
                Sessions.issue
                    documents
                    account
                    (TenantId.Mint())
                    SessionProvenance.FirstParty
                    now
                    lifetime
                    Unchecked.defaultof<_>

            let tampered = $"{issued.Session.Id}.{String('x', 43)}"
            let unknown = $"{Guid.NewGuid():D}.{issued.Token.Split('.')[1]}"

            let tokens: (string | null) list =
                [ null; ""; "garbage"; issued.Session.Id; tampered; unknown ]

            for token in tokens do
                let! validation = Sessions.validate documents token now Unchecked.defaultof<_>
                validation |> should equal SessionValidation.Required

            let! genuine = Sessions.validate documents issued.Token now Unchecked.defaultof<_>
            genuine.IsValid |> should be True
        }

    [<Test>]
    member _.``a session whose account is disabled or erased should be revoked when presented``() =
        task {
            for status in [ AccountStatus.Disabled; AccountStatus.Erased ] do
                let documents = MemoryDocumentStore()
                let! account = account documents

                let! issued =
                    Sessions.issue
                        documents
                        account
                        (TenantId.Mint())
                        SessionProvenance.FirstParty
                        now
                        lifetime
                        Unchecked.defaultof<_>

                let! record = readAccount documents account
                do! writeAccount documents account { record with Status = status }

                let! validation =
                    Sessions.validate documents issued.Token now Unchecked.defaultof<_>

                validation |> should equal SessionValidation.Required
                keysUnder documents "session/" |> should be Empty
        }

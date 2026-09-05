namespace Blokemon.App.Tests

open System
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.Product
open FsUnit
open TUnit.Core

/// A store in which another sign-in for the same subject wins the link at the moment this one
/// tries to create it: the interleaving a concurrent first sign-in produces.
type private LinkRacingStore(inner: MemoryDocumentStore, winner: AccountId) =

    let store = inner :> IStateDocumentStore
    let mutable raced = false

    interface IStateDocumentStore with
        member _.Read(key, cancellationToken) = store.Read(key, cancellationToken)

        member _.Create(key, json, cancellationToken) =
            task {
                if not raced && key.StartsWith("link/", StringComparison.Ordinal) then
                    raced <- true
                    let winning = Unchecked.nonNull(JsonNode.Parse json).AsObject()
                    winning["account"] <- JsonValue.Create winner.Value
                    let! _ = store.Create(key, winning.ToJsonString(), cancellationToken)
                    ()

                return! store.Create(key, json, cancellationToken)
            }

        member _.Update(key, revision, json, cancellationToken) =
            store.Update(key, revision, json, cancellationToken)

        member _.Delete(key, cancellationToken) = store.Delete(key, cancellationToken)

        member _.DeleteIfUnchanged(key, revision, json, cancellationToken) =
            store.DeleteIfUnchanged(key, revision, json, cancellationToken)

type SignInCompletionTests() =

    let complete documents identity tenant =
        SignInCompletion.complete (services documents) identity tenant now Unchecked.defaultof<_>

    let profileOf (documents: MemoryDocumentStore) (session: Session) =
        task {
            let principal = ApplicationPrincipal.Account(session.Account, session.Tenant)
            let! state = (application principal documents).State()
            return profile (value state)
        }

    [<Test>]
    member _.``the first sign-in for an unlinked identity should create the account and profile once and replay idempotently``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let identity = identity "example" "12345" "  Alex  " SessionProvenance.Issuer

            let! first = complete documents identity tenant
            let! second = complete documents identity tenant
            let first = succeeded first
            let second = succeeded second

            second.Session.Account |> should equal first.Session.Account

            keysUnder documents "account/"
            |> should equal [ $"account/{first.Session.Account}" ]

            keysUnder documents "link/" |> should equal [ "link/example/12345" ]

            keysUnder documents "a/"
            |> should equal [ $"a/{first.Session.Account}/profile" ]

            (keysUnder documents "session/").Length |> should equal 2

            let! profile = profileOf documents first.Session
            profile.DisplayName |> should equal "Alex"
            first.Session.Provenance |> should equal SessionProvenance.Issuer
            first.Session.Tenant |> should equal tenant
        }

    [<Test>]
    member _.``a first sign-in that loses the link to a concurrent one should follow the winner``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let winner = AccountId.Mint()
            let racing = LinkRacingStore(documents, winner)
            let tenant = TenantId.Mint()
            let identity = identity "example" "777" "Racer" SessionProvenance.Issuer

            let! outcome =
                SignInCompletion.complete
                    (services racing)
                    identity
                    tenant
                    now
                    CancellationToken.None

            (succeeded outcome).Session.Account |> should equal winner
            keysUnder documents "account/" |> should equal [ $"account/{winner}" ]
            keysUnder documents "a/" |> should equal [ $"a/{winner}/profile" ]
            keysUnder documents "link/" |> should equal [ "link/example/777" ]
        }

    [<Test>]
    member _.``a disabled or erased account should be refused with no session``() =
        task {
            for status, expected in
                [ AccountStatus.Disabled, SignInFailure.AccountDisabled
                  AccountStatus.Erased, SignInFailure.AccountErased ] do
                let documents = MemoryDocumentStore()
                let tenant = TenantId.Mint()
                let identity = identity "example" "42" "Someone" SessionProvenance.FirstParty
                let! first = complete documents identity tenant
                let account = (succeeded first).Session.Account
                let! record = readAccount documents account
                do! writeAccount documents account { record with Status = status }
                let sessionsBefore = keysUnder documents "session/"

                let! refused = complete documents identity tenant

                failed refused |> should equal expected
                keysUnder documents "session/" |> should equal sessionsBefore
        }

    [<Test>]
    member _.``an account excluded from a tenant should be refused there and admitted elsewhere``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let excluding = TenantId.Mint()
            let other = TenantId.Mint()
            let identity = identity "example" "99" "Excluded" SessionProvenance.Issuer
            let! first = complete documents identity excluding
            let account = (succeeded first).Session.Account

            let! _ = Approvals.exclude documents account excluding now Unchecked.defaultof<_>
            let sessionsBefore = keysUnder documents "session/"

            let! refused = complete documents identity excluding
            let! admitted = complete documents identity other

            failed refused |> should equal SignInFailure.TenantExcluded
            (succeeded admitted).Session.Tenant |> should equal other

            (keysUnder documents "session/").Length
            |> should equal (sessionsBefore.Length + 1)
        }

    [<Test>]
    member _.``completing as a given account should create that account and link the subject to it``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let account = AccountId.Mint()
            let identity = identity "own" account.Value "Alex" SessionProvenance.FirstParty

            let! first =
                SignInCompletion.completeAs
                    (services documents)
                    identity
                    account
                    tenant
                    now
                    CancellationToken.None

            let! replay =
                SignInCompletion.completeAs
                    (services documents)
                    identity
                    (AccountId.Mint())
                    tenant
                    now
                    CancellationToken.None

            (succeeded first).Session.Account |> should equal account
            (succeeded replay).Session.Account |> should equal account
            keysUnder documents "account/" |> should equal [ $"account/{account}" ]
            keysUnder documents "link/" |> should equal [ $"link/own/{account}" ]
            keysUnder documents "a/" |> should equal [ $"a/{account}/profile" ]
        }

    [<Test>]
    member _.``a display name hint should be trimmed bounded and defaulted``() =
        SignInCompletion.displayName null |> should equal "Player"
        SignInCompletion.displayName "   " |> should equal "Player"
        SignInCompletion.displayName "  Alex " |> should equal "Alex"

        SignInCompletion.displayName (String('n', 40))
        |> should equal (String('n', DisplayName.MaximumLength))

    [<Test>]
    member _.``a session should carry the provenance the provider stated``() =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()

            for provenance in
                [ SessionProvenance.FirstParty
                  SessionProvenance.Recovery
                  SessionProvenance.Issuer ] do
                let! issued = complete documents (identity "example" "1" null provenance) tenant
                (succeeded issued).Session.Provenance |> should equal provenance
        }

    [<Test>]
    member _.``signing in through the registry should verify with the named provider and refuse an unknown one``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()

            let provider =
                ScriptedIdentityProvider("example", SessionProvenance.Issuer)
                    .Accept("proof-1", "555", "Verified")
                    .Refuse("proof-2", ApiError("example.refused", "Refused."))

            let registry =
                IdentityProviderRegistry(
                    identityConfiguration
                        [ IdentityConfiguration.providerEnabledKey "example", "true" ],
                    [ provider ]
                )

            let signIn name proof =
                SignInCompletion.signIn
                    (services documents)
                    registry
                    (match IdentityProviderName.Create name with
                     | DomainResult.Succeeded name -> name
                     | DomainResult.Failed failure -> failwith $"{failure}")
                    proof
                    tenant
                    now
                    Unchecked.defaultof<_>

            let! verified = signIn "example" "proof-1"
            let! refused = signIn "example" "proof-2"
            let! unknown = signIn "missing" "proof-1"

            let! profile = profileOf documents (succeeded verified).Session
            profile.DisplayName |> should equal "Verified"

            match failed refused with
            | SignInFailure.ProviderRefused error -> error.Code |> should equal "example.refused"
            | other -> failwith $"Expected the provider's refusal, received {other}."

            match failed unknown with
            | SignInFailure.ProviderRefused error ->
                error.Code |> should equal "provider.unavailable"
            | other -> failwith $"Expected an unavailable provider, received {other}."

            keysUnder documents "session/" |> List.length |> should equal 1
        }

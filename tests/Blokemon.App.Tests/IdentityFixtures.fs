namespace Blokemon.App.Tests

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product
open Microsoft.Extensions.Configuration

/// A provider that asserts whatever the test scripted for a proof, so the completion path is
/// exercised without any real credential. It lives in the test assembly only.
type ScriptedIdentityProvider(name: string, provenance: SessionProvenance) =

    let name =
        match IdentityProviderName.Create name with
        | DomainResult.Succeeded name -> name
        | DomainResult.Failed failure -> failwith $"Bad provider name: {failure}"

    let identities = Dictionary<string, VerifiedIdentity>(StringComparer.Ordinal)
    let refusals = Dictionary<string, ApiError>(StringComparer.Ordinal)

    member _.Name = name

    /// Scripts a proof to verify as the given subject.
    member this.Accept(proof: string, subject: string, hint: string | null) =
        identities[proof] <-
            { Provider = name
              Subject =
                match ExternalSubject.Create subject with
                | DomainResult.Succeeded subject -> subject
                | DomainResult.Failed failure -> failwith $"Bad subject: {failure}"
              DisplayNameHint = hint
              Provenance = provenance }

        this

    member this.Refuse(proof: string, error: ApiError) =
        refusals[proof] <- error
        this

    interface IIdentityProvider with
        member _.Name = name

        member _.Verify(proof: string, _cancellationToken: CancellationToken) =
            let outcome =
                match identities.TryGetValue proof with
                | true, identity -> DomainResult.Succeeded identity
                | _ ->
                    match refusals.TryGetValue proof with
                    | true, error -> DomainResult.Failed(SignInFailure.ProviderRefused error)
                    | _ ->
                        DomainResult.Failed(
                            SignInFailure.ProviderRefused(
                                ApiError("proof.unknown", "Unknown proof.")
                            )
                        )

            Task.FromResult outcome

[<AutoOpen>]
module IdentityFixtures =

    let now = DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)

    let lifetime = TimeSpan.FromHours 8.0

    let configuration (settings: (string * string) list) =
        let pairs =
            settings
            |> List.map (fun (key, value) -> KeyValuePair<string, string | null>(key, value))

        ConfigurationBuilder().AddInMemoryCollection(pairs).Build() :> IConfiguration

    let identityConfiguration settings =
        IdentityConfiguration.Resolve(configuration settings)

    let services (documents: IStateDocumentStore) : SignInServices =
        { Documents = documents
          Catalogue = catalogue.Value
          Economy = EconomyRules.Unlimited
          SessionLifetime = lifetime }

    let identity (provider: string) (subject: string) (hint: string | null) provenance =
        { Provider =
            match IdentityProviderName.Create provider with
            | DomainResult.Succeeded name -> name
            | DomainResult.Failed failure -> failwith $"Bad provider: {failure}"
          Subject =
            match ExternalSubject.Create subject with
            | DomainResult.Succeeded subject -> subject
            | DomainResult.Failed failure -> failwith $"Bad subject: {failure}"
          DisplayNameHint = hint
          Provenance = provenance }

    let succeeded (result: DomainResult<'T, 'F>) : 'T =
        match result with
        | DomainResult.Succeeded value -> value
        | DomainResult.Failed failure -> failwith $"Expected success, received {failure}."

    let failed (result: DomainResult<'T, 'F>) : 'F =
        match result with
        | DomainResult.Succeeded value -> failwith $"Expected a failure, received {value}."
        | DomainResult.Failed failure -> failure

    let readAccount (documents: MemoryDocumentStore) (account: AccountId) =
        task {
            let! stored = (documents :> IStateDocumentStore).Read(accountKey account)

            match stored with
            | null -> return failwith "Expected an account document."
            | document ->
                return
                    Unchecked.nonNull (
                        JsonSerializer.Deserialize<AccountDocument>(document.Json, json)
                    )
        }

    let writeAccount
        (documents: MemoryDocumentStore)
        (account: AccountId)
        (document: AccountDocument)
        =
        task {
            let store = documents :> IStateDocumentStore
            let! stored = store.Read(accountKey account)

            match stored with
            | null -> failwith "Expected an account document."
            | existing ->
                let! _ =
                    store.Update(
                        accountKey account,
                        existing.Revision,
                        JsonSerializer.Serialize(document, json)
                    )

                ()
        }

    let keysUnder (documents: MemoryDocumentStore) (prefix: string) =
        documents.Keys
        |> List.filter (fun key -> key.StartsWith(prefix, StringComparison.Ordinal))

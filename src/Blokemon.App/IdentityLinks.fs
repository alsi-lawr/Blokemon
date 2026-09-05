namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// Why a link could not be created.
[<RequireQualifiedAccess>]
type internal IdentityLinkFailure =
    /// The pair is already linked, to this account or another; nothing changed.
    | AlreadyLinked

/// What the link index says about a provider's subject.
[<RequireQualifiedAccess>]
type internal LinkResolution =
    | Unlinked
    | Linked of AccountId
    | Damaged

/// The lookup index from a provider's subject to the one account it signs in to. Uniqueness on
/// the pair is the store's create-once rule on the link key.
module internal IdentityLinks =

    let create
        (documents: IStateDocumentStore)
        (link: ExternalIdentityLink)
        (linkedAt: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, IdentityLinkFailure>> =
        task {
            let document =
                { SchemaVersion = linkSchemaVersion
                  Provider = link.Provider.Value
                  Subject = link.Subject.Value
                  Account = link.Account.Value
                  LinkedAt = linkedAt }

            let! write =
                documents.Create(
                    linkKey link.Provider link.Subject,
                    JsonSerializer.Serialize(document, json),
                    cancellationToken
                )

            match write with
            | :? DocumentWriteResult.Written -> return DomainResult.Succeeded()
            | _ -> return DomainResult.Failed IdentityLinkFailure.AlreadyLinked
        }

    let resolve
        (documents: IStateDocumentStore)
        (provider: IdentityProviderName)
        (subject: ExternalSubject)
        (cancellationToken: CancellationToken)
        : Task<LinkResolution> =
        task {
            let! stored = documents.Read(linkKey provider subject, cancellationToken)

            match stored with
            | null -> return LinkResolution.Unlinked
            | document ->
                let parsed =
                    try
                        Ok(JsonSerializer.Deserialize<IdentityLinkDocument>(document.Json, json))
                    with :? JsonException ->
                        Error()

                match parsed with
                | Ok(NonNull value) when value.SchemaVersion = linkSchemaVersion ->
                    match AccountId.Create value.Account with
                    | DomainResult.Succeeded account -> return LinkResolution.Linked account
                    | DomainResult.Failed _ -> return LinkResolution.Damaged
                | _ -> return LinkResolution.Damaged
        }

    /// The keys of every link that resolves to the account. The link index runs from subject to
    /// account, so this reads every link; erasure is the one caller and is rare.
    let keysFor
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<string list> =
        task {
            let! summaries = listing.List("link/", cancellationToken)
            let mutable keys = []

            for summary in summaries do
                let! stored = documents.Read(summary.Key, cancellationToken)

                match stored with
                | null -> ()
                | document ->
                    let parsed =
                        try
                            Ok(
                                JsonSerializer.Deserialize<IdentityLinkDocument>(
                                    document.Json,
                                    json
                                )
                            )
                        with :? JsonException ->
                            Error()

                    match parsed with
                    | Ok(NonNull value) when
                        String.Equals(value.Account, account.Value, StringComparison.Ordinal)
                        ->
                        keys <- summary.Key :: keys
                    | _ -> ()

            return List.rev keys
        }

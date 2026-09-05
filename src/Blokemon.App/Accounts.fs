namespace Blokemon.App

open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// Account records as the server reads them. A record that is absent or cannot be read is an
/// account that cannot act.
module Accounts =

    let private parse (document: StoredDocument) : AccountDocument option =
        let parsed =
            try
                Ok(JsonSerializer.Deserialize<AccountDocument>(document.Json, json))
            with :? JsonException ->
                Error()

        match parsed with
        | Ok(NonNull value) when value.SchemaVersion = accountSchemaVersion -> Some value
        | _ -> None

    let read
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<AccountDocument option> =
        task {
            let! stored = documents.Read(accountKey account, cancellationToken)

            match stored with
            | null -> return None
            | document -> return parse document
        }

    /// Whether the account may act at all: it exists, reads, and is neither disabled nor erased.
    let isActive
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        task {
            let! record = read documents account cancellationToken

            match record with
            | Some value -> return value.Status = AccountStatus.Active
            | None -> return false
        }

    /// Whether the account is an operator: it exists, reads, is active and carries the flag.
    let isOperator
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        task {
            let! record = read documents account cancellationToken

            match record with
            | Some value -> return value.Status = AccountStatus.Active && value.Operator
            | None -> return false
        }

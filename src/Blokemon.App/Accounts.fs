namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// What remains at `account/{id}` once the account is erased: the id, so that it is never
/// reissued, and when it was erased. Nothing else.
type AccountTombstone =
    { Id: string; ErasedAt: DateTimeOffset }

/// What `account/{id}` holds.
[<RequireQualifiedAccess>]
type AccountRecord =
    | Live of Revision: int64 * Document: AccountDocument
    | Erased of AccountTombstone
    | Absent
    /// The record cannot be read as either shape.
    | Damaged

/// Account records as the server reads them. A record that is absent, erased or cannot be read
/// is an account that cannot act.
module Accounts =

    let tombstone (account: AccountId) (erasedAt: DateTimeOffset) : AccountTombstone =
        { Id = account.Value
          ErasedAt = erasedAt }

    let private parseDocument (document: StoredDocument) : AccountDocument option =
        let parsed =
            try
                Ok(JsonSerializer.Deserialize<AccountDocument>(document.Json, json))
            with :? JsonException ->
                Error()

        match parsed with
        | Ok(NonNull value) when value.SchemaVersion = accountSchemaVersion -> Some value
        | _ -> None

    /// The tombstone is the only account shape without a schema version: it carries exactly its
    /// two fields, and the serializer refuses any other member.
    let private parseTombstone (document: StoredDocument) : AccountTombstone option =
        let parsed =
            try
                Ok(JsonSerializer.Deserialize<AccountTombstone>(document.Json, json))
            with :? JsonException ->
                Error()

        match parsed with
        | Ok(NonNull value) when not (String.IsNullOrEmpty value.Id) -> Some value
        | _ -> None

    let load
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<AccountRecord> =
        task {
            let! stored = documents.Read(accountKey account, cancellationToken)

            match stored with
            | null -> return AccountRecord.Absent
            | document ->
                match parseDocument document with
                | Some value -> return AccountRecord.Live(document.Revision, value)
                | None ->
                    match parseTombstone document with
                    | Some value -> return AccountRecord.Erased value
                    | None -> return AccountRecord.Damaged
        }

    /// The live record, or none: an erased, absent or damaged account reads as none.
    let read
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<AccountDocument option> =
        task {
            let! record = load documents account cancellationToken

            match record with
            | AccountRecord.Live(_, value) -> return Some value
            | _ -> return None
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

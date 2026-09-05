namespace Blokemon.App.Tests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts

/// An in-memory document store with the same create-once and revision-checked rules as the
/// hosts' stores, plus a view of every key it holds. Its listing carries keys and revisions
/// only; the server store's per-type projection is that store's own.
type MemoryDocumentStore() =

    let documents = Dictionary<string, StoredDocument>(StringComparer.Ordinal)
    let gate = obj ()

    /// Every key held, in ordinal order.
    member _.Keys = lock gate (fun () -> documents.Keys |> Seq.sort |> List.ofSeq)

    /// Every document held, by key.
    member _.Snapshot =
        lock gate (fun () -> documents |> Seq.map (fun pair -> pair.Key, pair.Value) |> Map.ofSeq)

    interface IStateDocumentStore with

        member _.Read(key: string, _cancellationToken: CancellationToken) =
            lock gate (fun () ->
                let found: StoredDocument | null =
                    match documents.TryGetValue key with
                    | true, document -> document
                    | _ -> null

                Task.FromResult found)

        member _.Create(key: string, json: string, _cancellationToken: CancellationToken) =
            lock gate (fun () ->
                let result: DocumentWriteResult =
                    if documents.ContainsKey key then
                        DocumentWriteResult.Conflict()
                    else
                        documents.Add(key, StoredDocument(1L, json))
                        DocumentWriteResult.Written 1L

                Task.FromResult result)

        member _.Update
            (
                key: string,
                expectedRevision: int64,
                json: string,
                _cancellationToken: CancellationToken
            ) =
            lock gate (fun () ->
                let result: DocumentWriteResult =
                    match documents.TryGetValue key with
                    | true, current when current.Revision = expectedRevision ->
                        documents[key] <- StoredDocument(expectedRevision + 1L, json)
                        DocumentWriteResult.Written(expectedRevision + 1L)
                    | _ -> DocumentWriteResult.Conflict()

                Task.FromResult result)

        member _.Delete(key: string, _cancellationToken: CancellationToken) =
            lock gate (fun () ->
                documents.Remove key |> ignore
                Task.CompletedTask)

        member _.DeleteIfUnchanged
            (
                key: string,
                expectedRevision: int64,
                expectedJson: string,
                _cancellationToken: CancellationToken
            ) =
            lock gate (fun () ->
                let result: DocumentDeleteResult =
                    match documents.TryGetValue key with
                    | false, _ -> DocumentDeleteResult.Missing()
                    | true, current when
                        current.Revision = expectedRevision
                        && String.Equals(current.Json, expectedJson, StringComparison.Ordinal)
                        ->
                        documents.Remove key |> ignore
                        DocumentDeleteResult.Deleted()
                    | _ -> DocumentDeleteResult.Conflict()

                Task.FromResult result)

    interface IDocumentListing with

        member _.List(prefix: string, _cancellationToken: CancellationToken) =
            lock gate (fun () ->
                let summaries =
                    documents
                    |> Seq.filter (fun pair ->
                        pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    |> Seq.sortBy (fun pair -> pair.Key)
                    |> Seq.map (fun pair -> DocumentSummary(pair.Key, pair.Value.Revision, null))
                    |> Seq.toArray
                    :> IReadOnlyList<DocumentSummary>

                Task.FromResult summaries)

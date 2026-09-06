namespace Blokemon.App

open System
open System.Collections.Immutable
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.DamagedDocument
open Blokemon.App.MatchFailures
open Blokemon.App.MatchMigration
open Blokemon.App.MatchPayloads
open Blokemon.App.MatchReplay
open Blokemon.Product
open Blokemon.Game

/// Reads the saved battle back through the verified replay, and archives a finished one into the
/// match history as it stands.
module internal MatchStore =

    let load
        (context: MatchContext)
        (profile: LocalProfile)
        (cancellationToken: CancellationToken)
        =
        let documents = context.Documents
        let replayDocument = replayDocument context

        task {
            let! stored = documents.Read(context.Keys.Match, cancellationToken)

            match stored with
            | null ->
                context.Cached <- null

                return
                    { Match = null
                      Error = null
                      Recovery = None }
            | document ->
                match context.Cached with
                | NonNull cached when cached.DocumentRevision = document.Revision ->
                    return
                        { Match = cached
                          Error = null
                          Recovery = None }
                | _ ->
                    let! resolved = resolveMatch context profile document cancellationToken

                    match resolved with
                    | MatchMigrationOutcome.RecoveryRequired requirement ->
                        let error = recoveryError requirement

                        return
                            { Match = null
                              Error = error
                              Recovery = Some requirement }
                    | MatchMigrationOutcome.Failed error ->
                        return
                            { Match = null
                              Error = error
                              Recovery = None }
                    | MatchMigrationOutcome.Ready ready ->
                        let replayed = replayDocument profile ready.Stored.Revision ready.Document

                        match replayed.Error with
                        | NonNull error ->
                            context.Cached <- null

                            return
                                { Match = null
                                  Error = error
                                  Recovery = Some(activeReplayRecovery context ready.Stored error) }
                        | Null ->
                            context.Cached <- replayed.Match

                            return
                                { Match = replayed.Match
                                  Error = null
                                  Recovery = None }
        }

    let historyRecovery
        (context: MatchContext)
        (profile: LocalProfile)
        (cancellationToken: CancellationToken)
        =
        task {
            let! stored = context.Documents.Read(context.Keys.MatchHistory, cancellationToken)

            match stored with
            | null -> return Ok None
            | document ->
                let! resolved = resolveHistory context profile document cancellationToken

                match resolved with
                | MatchMigrationOutcome.Ready _ -> return Ok None
                | MatchMigrationOutcome.RecoveryRequired requirement -> return Ok(Some requirement)
                | MatchMigrationOutcome.Failed error -> return Error error
        }

    /// Archives a finished battle as it stands: the battle becomes its own document, and its id
    /// goes on the index. The history is data that exists: nothing here reads, replays or checks
    /// what is already archived, and the battle being archived was verified while it was the
    /// saved battle. A battle already on the index is left as it is, so a start retried after
    /// the index was written, but before the new battle was, does not archive it twice.
    let archiveCompletedMatch
        (context: MatchContext)
        (profile: LocalProfile)
        (completed: LoadedMatch)
        (cancellationToken: CancellationToken)
        : Task<MatchArchiveOutcome> =
        let catalogue = context.Catalogue
        let documents = context.Documents

        task {
            let! stored = documents.Read(context.Keys.MatchHistory, cancellationToken)

            let! history =
                task {
                    match stored with
                    | null ->
                        return
                            Ok(
                                None,
                                { SchemaVersion = matchHistorySchemaVersion
                                  AuthorityVersion = catalogue.Mechanics.ManifestVersion
                                  MatchIds = ImmutableArray<string>.Empty }
                            )
                    | document ->
                        let! resolved = resolveHistory context profile document cancellationToken

                        match resolved with
                        | MatchMigrationOutcome.Ready ready ->
                            return Ok(Some ready.Stored, ready.Document)
                        | MatchMigrationOutcome.RecoveryRequired requirement ->
                            return Error(MatchArchiveOutcome.RecoveryRequired requirement)
                        | MatchMigrationOutcome.Failed error ->
                            return Error(MatchArchiveOutcome.Failed error)
                }

            match history with
            | Error failure -> return failure
            | Ok(resolvedStored, document) ->
                let matchId = completed.Document.Start.MatchId.Value

                if document.MatchIds |> Seq.contains matchId then
                    return MatchArchiveOutcome.Ready
                else
                    // The battle as it stands, as its own document; one already there is left.
                    let! _ =
                        documents.Create(
                            PlayerDocumentKeys.archivedMatch context.Keys matchId,
                            JsonSerializer.Serialize(completed.Document, MatchJson.Options),
                            cancellationToken
                        )

                    let changed =
                        { document with
                            MatchIds = document.MatchIds.Add matchId }

                    let json = JsonSerializer.Serialize(changed, MatchJson.Options)

                    let! write =
                        match resolvedStored with
                        | None ->
                            documents.Create(context.Keys.MatchHistory, json, cancellationToken)
                        | Some existing ->
                            documents.Update(
                                context.Keys.MatchHistory,
                                existing.Revision,
                                json,
                                cancellationToken
                            )

                    match write with
                    | :? DocumentWriteResult.Written -> return MatchArchiveOutcome.Ready
                    | _ -> return MatchArchiveOutcome.Failed(historyConflictError ())
        }

namespace Blokemon.App

open System
open System.Collections.Immutable
open System.Linq
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
/// match history. Both damaged-document gates live here.
module internal MatchStore =

    let load
        (context: MatchContext)
        (profile: LocalProfile)
        (cancellationToken: CancellationToken)
        =
        let documents = context.Documents
        let replayDocument = replayDocument context

        task {
            let! stored = documents.Read(matchKey, cancellationToken)

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

                        context.Cached <- replayed.Match

                        return
                            { Match = replayed.Match
                              Error = replayed.Error
                              Recovery = None }
        }

    let historyRecovery
        (context: MatchContext)
        (profile: LocalProfile)
        (cancellationToken: CancellationToken)
        =
        task {
            let! stored = context.Documents.Read(matchHistoryKey, cancellationToken)

            match stored with
            | null -> return Ok None
            | document ->
                let! resolved = resolveHistory context profile document cancellationToken

                match resolved with
                | MatchMigrationOutcome.Ready _ -> return Ok None
                | MatchMigrationOutcome.RecoveryRequired requirement -> return Ok(Some requirement)
                | MatchMigrationOutcome.Failed error -> return Error error
        }

    let archiveCompletedMatch
        (context: MatchContext)
        (profile: LocalProfile)
        (completed: LoadedMatch)
        (cancellationToken: CancellationToken)
        : Task<MatchArchiveOutcome> =
        let catalogue = context.Catalogue
        let documents = context.Documents
        let replayDocument = replayDocument context

        task {
            let! stored = documents.Read(matchHistoryKey, cancellationToken)

            let! history =
                task {
                    match stored with
                    | null ->
                        return
                            Ok(
                                None,
                                { SchemaVersion = matchHistorySchemaVersion
                                  AuthorityVersion = catalogue.Mechanics.ManifestVersion
                                  Matches = ImmutableArray<MatchDocument>.Empty }
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
                let archiveFailure =
                    document.Matches
                    |> Seq.tryPick (fun archived ->
                        if
                            isMissing archived
                            || isMissing archived.StartCommand
                            || isMissing archived.Start
                        then
                            Some(historyCorrupt ())
                        elif archived.SchemaVersion <> matchSchemaVersion then
                            Some(historyVersion ())
                        else
                            let replay = replayDocument profile 0L archived

                            match replay.Error with
                            | NonNull error when error.Code = "match.authority_changed" ->
                                Some(historyAuthorityChanged ())
                            | NonNull _ -> Some(historyCorrupt ())
                            | Null ->
                                match replay.Match with
                                | Null -> Some(historyCorrupt ())
                                | NonNull loaded when loaded.State.Phase <> MatchPhase.Complete ->
                                    Some(historyCorrupt ())
                                | NonNull _ -> None)

                match archiveFailure with
                | Some failure -> return MatchArchiveOutcome.Failed failure
                | None ->
                    if
                        document.Matches
                        |> Seq.countBy _.Start.MatchId
                        |> Seq.exists (fun (_, count) -> count > 1)
                    then
                        return MatchArchiveOutcome.Failed(historyCorrupt ())
                    else
                        match
                            document.Matches.SingleOrDefault(fun archived ->
                                archived.Start.MatchId = completed.Document.Start.MatchId)
                        with
                        | NonNull duplicate ->
                            return
                                (if documentsMatch duplicate completed.Document then
                                     MatchArchiveOutcome.Ready
                                 else
                                     MatchArchiveOutcome.Failed(historyCorrupt ()))
                        | Null ->
                            let changed =
                                { document with
                                    Matches =
                                        ImmutableArray.CreateRange(
                                            Seq.append document.Matches [ completed.Document ]
                                        ) }

                            let json = JsonSerializer.Serialize(changed, MatchJson.Options)

                            let! write =
                                match resolvedStored with
                                | None -> documents.Create(matchHistoryKey, json, cancellationToken)
                                | Some existing ->
                                    documents.Update(
                                        matchHistoryKey,
                                        existing.Revision,
                                        json,
                                        cancellationToken
                                    )

                            match write with
                            | :? DocumentWriteResult.Written -> return MatchArchiveOutcome.Ready
                            | _ -> return MatchArchiveOutcome.Failed(historyConflictError ())
        }

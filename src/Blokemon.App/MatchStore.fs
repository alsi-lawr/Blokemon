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
                return { Match = null; Error = null }
            | document ->
                match context.Cached with
                | NonNull cached when cached.DocumentRevision = document.Revision ->
                    return { Match = cached; Error = null }
                | _ ->
                    let! resolved = resolveMatch context profile document cancellationToken

                    match resolved with
                    | MatchMigrationOutcome.RecoveryRequired requirement ->
                        let error = recoveryError requirement
                        return { Match = null; Error = error }
                    | MatchMigrationOutcome.Failed error -> return { Match = null; Error = error }
                    | MatchMigrationOutcome.Ready ready ->
                        let replayed = replayDocument profile ready.Stored.Revision ready.Document

                        context.Cached <- replayed.Match
                        return replayed
        }

    let archiveCompletedMatch
        (context: MatchContext)
        (profile: LocalProfile)
        (completed: LoadedMatch)
        (cancellationToken: CancellationToken)
        // The archive either rejects the saved history with a typed error, or reports nothing;
        // an F# option carries that better than a null across the whole body.
        : Task<ApiError option> =
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
                            return Error(recoveryError requirement)
                        | MatchMigrationOutcome.Failed error -> return Error error
                }

            match history with
            | Error failure -> return Some failure
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
                | Some failure -> return Some failure
                | None ->
                    if
                        document.Matches
                        |> Seq.countBy _.Start.MatchId
                        |> Seq.exists (fun (_, count) -> count > 1)
                    then
                        return Some(historyCorrupt ())
                    else
                        match
                            document.Matches.SingleOrDefault(fun archived ->
                                archived.Start.MatchId = completed.Document.Start.MatchId)
                        with
                        | NonNull duplicate ->
                            return
                                (if documentsMatch duplicate completed.Document then
                                     None
                                 else
                                     Some(historyCorrupt ()))
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
                            | :? DocumentWriteResult.Written -> return None
                            | _ -> return Some(historyConflictError ())
        }

namespace Blokemon.App

open System
open System.Linq
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.DamagedDocument
open Blokemon.App.MatchFailures
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
                    let schemaVersion = readSchemaVersion document.Json

                    if not schemaVersion.HasValue then
                        return
                            invalidDocument
                                "match.document_corrupt"
                                "The saved battle is damaged. No data changed."
                    elif schemaVersion.Value <> matchSchemaVersion then
                        return
                            invalidDocument
                                "match.document_version"
                                "This saved battle uses an unsupported version. No data changed."
                    else
                        let parsed =
                            try
                                Ok(
                                    JsonSerializer.Deserialize<MatchDocument>(
                                        document.Json,
                                        MatchJson.Options
                                    )
                                )
                            with
                            | :? JsonException -> Error()
                            | :? NotSupportedException -> Error()

                        match parsed with
                        | Error() ->
                            return
                                invalidDocument
                                    "match.document_corrupt"
                                    "The saved battle is damaged. No data changed."
                        | Ok Null ->
                            return
                                invalidDocument
                                    "match.document_corrupt"
                                    "The saved battle is damaged. No data changed."
                        | Ok(NonNull value) ->
                            if isMissing value.StartCommand || isMissing value.Start then
                                return
                                    invalidDocument
                                        "match.document_corrupt"
                                        "The saved battle is damaged. No data changed."
                            else
                                let replayed = replayDocument profile document.Revision value
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

            let history =
                match stored with
                | null ->
                    Ok
                        { SchemaVersion = matchHistorySchemaVersion
                          AuthorityVersion = catalogue.Mechanics.ManifestVersion
                          Matches = FrozenList<MatchDocument>.Empty }
                | document ->
                    let parsed =
                        try
                            match
                                JsonSerializer.Deserialize<MatchHistoryDocument>(
                                    document.Json,
                                    MatchJson.Options
                                )
                            with
                            | null -> Error(historyCorrupt ())
                            | value -> Ok value
                        with
                        | :? JsonException -> Error(historyCorrupt ())
                        | :? NotSupportedException -> Error(historyCorrupt ())

                    match parsed with
                    | Error failure -> Error failure
                    | Ok value ->
                        if value.SchemaVersion <> matchHistorySchemaVersion then
                            Error(historyVersion ())
                        elif
                            not (
                                String.Equals(
                                    value.AuthorityVersion,
                                    catalogue.Mechanics.ManifestVersion,
                                    StringComparison.Ordinal
                                )
                            )
                        then
                            Error(historyAuthorityChanged ())
                        else
                            Ok value

            match history with
            | Error failure -> return Some failure
            | Ok document ->
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
                                        FrozenList<MatchDocument>
                                            .Create(
                                                Seq.append document.Matches [ completed.Document ]
                                            ) }

                            let json = JsonSerializer.Serialize(changed, MatchJson.Options)

                            let! write =
                                match stored with
                                | null -> documents.Create(matchHistoryKey, json, cancellationToken)
                                | existing ->
                                    documents.Update(
                                        matchHistoryKey,
                                        existing.Revision,
                                        json,
                                        cancellationToken
                                    )

                            match write with
                            | :? DocumentWriteResult.Written -> return None
                            | _ ->
                                return
                                    Some(
                                        ApiError(
                                            "state.conflict",
                                            "The saved battle history changed in another tab. Start the battle again."
                                        )
                                    )
        }

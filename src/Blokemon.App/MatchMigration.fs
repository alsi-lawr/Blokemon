namespace Blokemon.App

open System
open System.Collections.Immutable
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.MatchFailures
open Blokemon.App.MatchMigrationJson
open Blokemon.App.MatchReplay
open Blokemon.Game
open Blokemon.Product

/// Validates migration candidates against the checked-out game, durably preserves the exact
/// source document, and only then replaces the primary key by revision-checked compare-and-swap.
module internal MatchMigration =

    let backupKey (key: string) (source: StoredDocument) =
        $"match-migration-backup/{key}/{source.Revision}/{DocumentIdentity.ofText source.Json}"

    let private backupJson (key: string) (source: StoredDocument) (identity: string) =
        let backup = JsonObject()
        backup["schemaVersion"] <- JsonValue.Create 1
        backup["sourceKey"] <- JsonValue.Create key
        backup["sourceRevision"] <- JsonValue.Create source.Revision
        backup["sourceJson"] <- JsonValue.Create source.Json
        backup["migration"] <- JsonValue.Create identity
        backup.ToJsonString(MatchJson.Options)

    let private exactDocument revision json (document: StoredDocument | null) =
        match document with
        | null -> false
        | value ->
            value.Revision = revision
            && String.Equals(value.Json, json, StringComparison.Ordinal)

    let private exactStored revision json (document: StoredDocument | null) =
        match document with
        | null -> None
        | value when
            value.Revision = revision
            && String.Equals(value.Json, json, StringComparison.Ordinal)
            ->
            Some value
        | _ -> None

    let private ensureBackup
        (documents: IStateDocumentStore)
        key
        (source: StoredDocument)
        identity
        (cancellationToken: CancellationToken)
        =
        task {
            let primaryKey = key
            let key = backupKey primaryKey source
            let json = backupJson primaryKey source identity

            try
                let! created = documents.Create(key, json, cancellationToken)

                match created with
                | :? DocumentWriteResult.Written -> return true
                | _ ->
                    let! stored = documents.Read(key, CancellationToken.None)
                    return exactDocument 1L json stored
            with error ->
                let! stored = documents.Read(key, CancellationToken.None)

                if exactDocument 1L json stored then
                    match error with
                    | :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                        return raise error
                    | _ -> return true
                else
                    return raise error
        }

    let private updatePrimary
        (documents: IStateDocumentStore)
        key
        (source: StoredDocument)
        candidateJson
        (cancellationToken: CancellationToken)
        =
        task {
            let expectedRevision = source.Revision + 1L

            try
                let! written =
                    documents.Update(key, source.Revision, candidateJson, cancellationToken)

                match written with
                | :? DocumentWriteResult.Written as committed when
                    committed.Revision = expectedRevision
                    ->
                    return Some(StoredDocument(committed.Revision, candidateJson))
                | _ ->
                    let! stored = documents.Read(key, CancellationToken.None)

                    return exactStored expectedRevision candidateJson stored
            with error ->
                let! stored = documents.Read(key, CancellationToken.None)

                match exactStored expectedRevision candidateJson stored with
                | Some committed -> return Some committed
                | None -> return raise error
        }

    let private recovery
        (document: MatchRecoveryDocument)
        (key: string)
        (reason: MatchRecoveryReason)
        (stored: StoredDocument)
        =
        MatchMigrationOutcome.RecoveryRequired
            { Document = document
              Key = key
              Reason = reason
              Stored = stored }

    let recoveryError (requirement: MatchRecoveryRequirement) =
        match requirement.Document, requirement.Reason with
        | MatchRecoveryDocument.ActiveMatch, MatchRecoveryReason.Corrupt ->
            ApiError("match.document_corrupt", "The saved battle is damaged. No data changed.")
        | MatchRecoveryDocument.ActiveMatch, MatchRecoveryReason.UnsupportedVersion ->
            ApiError(
                "match.document_version",
                "This saved battle uses an unsupported version. No data changed."
            )
        | MatchRecoveryDocument.ActiveMatch, MatchRecoveryReason.UnsupportedCpuPolicy ->
            ApiError(
                "match.cpu_policy_version",
                "This saved battle uses an unsupported computer policy. No data changed."
            )
        | MatchRecoveryDocument.ActiveMatch, MatchRecoveryReason.IncompatibleWithCurrentRules ->
            ApiError(
                "match.authority_changed",
                "The card rules changed after this battle started. Start a new battle."
            )
        | MatchRecoveryDocument.MatchHistory, MatchRecoveryReason.Corrupt -> historyCorrupt ()
        | MatchRecoveryDocument.MatchHistory, MatchRecoveryReason.UnsupportedVersion ->
            historyVersion ()
        | MatchRecoveryDocument.MatchHistory, MatchRecoveryReason.UnsupportedCpuPolicy ->
            historyVersion ()
        | MatchRecoveryDocument.MatchHistory, MatchRecoveryReason.IncompatibleWithCurrentRules ->
            historyAuthorityChanged ()

    let recoveryView (requirement: MatchRecoveryRequirement) =
        let kind =
            match requirement.Document, requirement.Reason with
            | MatchRecoveryDocument.ActiveMatch, MatchRecoveryReason.Corrupt ->
                Some MatchRecoveryKindView.ActiveMatchCorrupt
            | MatchRecoveryDocument.ActiveMatch, MatchRecoveryReason.UnsupportedVersion ->
                Some MatchRecoveryKindView.ActiveMatchUnsupportedVersion
            | MatchRecoveryDocument.ActiveMatch, MatchRecoveryReason.UnsupportedCpuPolicy ->
                Some MatchRecoveryKindView.ActiveMatchUnsupportedVersion
            | MatchRecoveryDocument.ActiveMatch, MatchRecoveryReason.IncompatibleWithCurrentRules ->
                Some MatchRecoveryKindView.ActiveMatchIncompatibleWithCurrentRules
            | MatchRecoveryDocument.MatchHistory, MatchRecoveryReason.UnsupportedVersion ->
                Some MatchRecoveryKindView.MatchHistoryUnsupportedVersion
            | MatchRecoveryDocument.MatchHistory, MatchRecoveryReason.UnsupportedCpuPolicy ->
                Some MatchRecoveryKindView.MatchHistoryUnsupportedVersion
            | MatchRecoveryDocument.MatchHistory, MatchRecoveryReason.IncompatibleWithCurrentRules ->
                Some MatchRecoveryKindView.MatchHistoryIncompatibleWithCurrentRules
            | MatchRecoveryDocument.MatchHistory, MatchRecoveryReason.Corrupt -> None

        kind
        |> Option.map (fun value ->
            MatchRecoveryView(
                value,
                requirement.Stored.Revision,
                DocumentIdentity.ofText requirement.Stored.Json
            ))
        |> Option.toObj

    let activeReplayRecovery (context: MatchContext) (stored: StoredDocument) (error: ApiError) =
        let reason =
            match error.Code with
            | "match.authority_changed" -> MatchRecoveryReason.IncompatibleWithCurrentRules
            | "match.document_version" -> MatchRecoveryReason.UnsupportedVersion
            | "match.cpu_policy_version" -> MatchRecoveryReason.UnsupportedCpuPolicy
            | _ -> MatchRecoveryReason.Corrupt

        { Document = MatchRecoveryDocument.ActiveMatch
          Key = context.Keys.Match
          Reason = reason
          Stored = stored }

    let private persist
        (documentKind: MatchRecoveryDocument)
        (key: string)
        (documents: IStateDocumentStore)
        (source: StoredDocument)
        (candidate: MatchMigrationCandidate<'Document>)
        (cancellationToken: CancellationToken)
        =
        task {
            let! backedUp = ensureBackup documents key source candidate.Identity cancellationToken

            if not backedUp then
                return MatchMigrationOutcome.Failed(stateConflictError ())
            else
                cancellationToken.ThrowIfCancellationRequested()

                let! stored = updatePrimary documents key source candidate.Json cancellationToken

                match stored with
                | Some committed ->
                    return
                        MatchMigrationOutcome.Ready
                            { Stored = committed
                              Document = candidate.Document }
                | None -> return MatchMigrationOutcome.Failed(stateConflictErrorFor documentKind)
        }

    let private validateMatch (context: MatchContext) (profile: LocalProfile) revision document =
        let replayed = replayDocument context profile revision document
        isNull (box replayed.Error) && not (isNull (box replayed.Match))

    // A migrated index is checked for its shape alone. The battles it names are data that
    // exists: each was verified while it was the saved battle, and none is read again.
    let private validateHistory (context: MatchContext) (document: MatchHistoryDocument) =
        document.SchemaVersion = matchHistorySchemaVersion
        && String.Equals(
            document.AuthorityVersion,
            context.Catalogue.Mechanics.ManifestVersion,
            StringComparison.Ordinal
        )
        && not (document.MatchIds |> Seq.exists String.IsNullOrWhiteSpace)
        && (document.MatchIds |> Seq.distinct |> Seq.length) = document.MatchIds.Length

    /// A history written before the index held every archived battle inside one document. Each
    /// battle becomes its own document, as it stands, and the index replaces the document once
    /// the source is backed up exactly. A battle document already there is left as it is.
    let private migrateHistoryLayout
        (context: MatchContext)
        (source: StoredDocument)
        (cancellationToken: CancellationToken)
        : Task<Result<StoredDocument, MatchMigrationOutcome<MatchHistoryDocument>>> =
        let key = context.Keys.MatchHistory
        let documents = context.Documents

        task {
            match MatchMigrationJson.legacyHistoryEntries source.Json with
            | Error reason ->
                return Error(recovery MatchRecoveryDocument.MatchHistory key reason source)
            | Ok(authority, entries) ->
                let identity = $"match-history-layout-3-to-{matchHistorySchemaVersion}"
                let! backedUp = ensureBackup documents key source identity cancellationToken

                if not backedUp then
                    return Error(MatchMigrationOutcome.Failed(stateConflictError ()))
                else
                    for id, json in entries do
                        cancellationToken.ThrowIfCancellationRequested()

                        let! _ =
                            documents.Create(
                                PlayerDocumentKeys.archivedMatch context.Keys id,
                                json,
                                cancellationToken
                            )

                        ()

                    let index =
                        JsonSerializer.Serialize(
                            { SchemaVersion = matchHistorySchemaVersion
                              AuthorityVersion = authority
                              MatchIds = ImmutableArray.CreateRange(entries |> List.map fst) },
                            MatchJson.Options
                        )

                    let! stored = updatePrimary documents key source index cancellationToken

                    match stored with
                    | Some committed -> return Ok committed
                    | None ->
                        return
                            Error(
                                MatchMigrationOutcome.Failed(
                                    stateConflictErrorFor MatchRecoveryDocument.MatchHistory
                                )
                            )
        }

    let resolveMatch
        (context: MatchContext)
        (profile: LocalProfile)
        (source: StoredDocument)
        (cancellationToken: CancellationToken)
        =
        task {
            let authority = context.Catalogue.Mechanics.ManifestVersion

            match prepareMatch authority source.Json with
            | MatchMigrationPreparation.Current document ->
                return MatchMigrationOutcome.Ready { Stored = source; Document = document }
            | MatchMigrationPreparation.RecoveryRequired reason ->
                return recovery MatchRecoveryDocument.ActiveMatch context.Keys.Match reason source
            | MatchMigrationPreparation.Candidate candidate ->
                cancellationToken.ThrowIfCancellationRequested()

                if
                    not (validateMatch context profile (source.Revision + 1L) candidate.Document)
                then
                    return
                        recovery
                            MatchRecoveryDocument.ActiveMatch
                            context.Keys.Match
                            (if candidate.ReboundAuthority then
                                 MatchRecoveryReason.IncompatibleWithCurrentRules
                             else
                                 MatchRecoveryReason.Corrupt)
                            source
                else
                    return!
                        persist
                            MatchRecoveryDocument.ActiveMatch
                            context.Keys.Match
                            context.Documents
                            source
                            candidate
                            cancellationToken
        }

    let resolveHistory
        (context: MatchContext)
        (_profile: LocalProfile)
        (source: StoredDocument)
        (cancellationToken: CancellationToken)
        =
        task {
            let! migrated =
                if MatchMigrationJson.isLegacyHistoryLayout source.Json then
                    migrateHistoryLayout context source cancellationToken
                else
                    Task.FromResult(Ok source)

            match migrated with
            | Error outcome -> return outcome
            | Ok source ->
                let authority = context.Catalogue.Mechanics.ManifestVersion

                match prepareHistory authority source.Json with
                | MatchMigrationPreparation.Current document ->
                    return MatchMigrationOutcome.Ready { Stored = source; Document = document }
                | MatchMigrationPreparation.RecoveryRequired reason ->
                    return
                        recovery
                            MatchRecoveryDocument.MatchHistory
                            context.Keys.MatchHistory
                            reason
                            source
                | MatchMigrationPreparation.Candidate candidate ->
                    cancellationToken.ThrowIfCancellationRequested()

                    if not (validateHistory context candidate.Document) then
                        return
                            recovery
                                MatchRecoveryDocument.MatchHistory
                                context.Keys.MatchHistory
                                (if candidate.ReboundAuthority then
                                     MatchRecoveryReason.IncompatibleWithCurrentRules
                                 else
                                     MatchRecoveryReason.Corrupt)
                                source
                    else
                        return!
                            persist
                                MatchRecoveryDocument.MatchHistory
                                context.Keys.MatchHistory
                                context.Documents
                                source
                                candidate
                                cancellationToken
        }

namespace Blokemon.App

open System
open System.Threading
open Blokemon.App.Contracts
open Blokemon.App.MatchFailures
open Blokemon.App.MatchMigration
open Blokemon.Product

module internal MatchRecovery =

    let private stale () =
        ApiError(
            "match.recovery_stale",
            "The saved battle data changed. Review it again before deleting anything."
        )

    let private unavailable () =
        ApiError(
            "match.recovery_unavailable",
            "This saved battle data is not eligible for deletion. No data changed."
        )

    let private matchesExpectation revision identity (stored: StoredDocument) =
        stored.Revision = revision
        && String.Equals(
            DocumentIdentity.ofText stored.Json,
            identity,
            StringComparison.Ordinal
        )

    let private recover
        (context: MatchContext)
        (profile: LocalProfile)
        documentKind
        key
        expectedRevision
        expectedIdentity
        resolve
        clearCache
        (cancellationToken: CancellationToken)
        =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            let! stored = context.Documents.Read(key, cancellationToken)

            match stored with
            | null -> return Ok()
            | source when
                not (matchesExpectation expectedRevision expectedIdentity source)
                ->
                return Error(stale ())
            | source ->
                let! resolution = resolve context profile source cancellationToken

                match resolution with
                | MatchMigrationOutcome.RecoveryRequired requirement when
                    requirement.Document = documentKind
                    && not (isNull (box (recoveryView requirement)))
                    ->
                    cancellationToken.ThrowIfCancellationRequested()

                    let! deleted =
                        context.Documents.DeleteIfUnchanged(
                            key,
                            source.Revision,
                            source.Json,
                            cancellationToken
                        )

                    match deleted with
                    | :? DocumentDeleteResult.Deleted
                    | :? DocumentDeleteResult.Missing ->
                        if clearCache then
                            context.Cached <- null

                        return Ok()
                    | _ -> return Error(stale ())
                | MatchMigrationOutcome.RecoveryRequired _ -> return Error(unavailable ())
                | MatchMigrationOutcome.Ready _ -> return Error(unavailable ())
                | MatchMigrationOutcome.Failed error -> return Error error
        }

    let abandonSavedMatch
        (context: MatchContext)
        (profile: LocalProfile)
        (request: AbandonSavedMatchRequest)
        (cancellationToken: CancellationToken)
        =
        recover
            context
            profile
            MatchRecoveryDocument.ActiveMatch
            matchKey
            request.ExpectedRevision
            request.ContentIdentity
            resolveMatch
            true
            cancellationToken

    let discardMatchHistory
        (context: MatchContext)
        (profile: LocalProfile)
        (request: DiscardMatchHistoryRequest)
        (cancellationToken: CancellationToken)
        =
        recover
            context
            profile
            MatchRecoveryDocument.MatchHistory
            matchHistoryKey
            request.ExpectedRevision
            request.ContentIdentity
            resolveHistory
            false
            cancellationToken

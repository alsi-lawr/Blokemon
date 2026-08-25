namespace Blokemon.App

open System
open System.Linq
open System.Threading
open Blokemon.App.MatchFailures
open Blokemon.App.MatchStore
open Blokemon.App.MatchViewProjection
open Blokemon.Product

/// What a losing write means. A conflicting document is reloaded and read against the request
/// that lost, so a retried command reports the saved outcome instead of a bare conflict.
module internal MatchConflicts =

    let reconcileStartConflict
        (context: MatchContext)
        (profile: LocalProfile)
        (displayName: string)
        (commandId: Guid)
        (requestFingerprint: string)
        (cancellationToken: CancellationToken)
        =
        let load = load context
        let toView = toView context

        task {
            let! reloaded = load profile cancellationToken

            if not (isNull (box reloaded.Error)) then
                return
                    { View = null
                      Error = reloaded.Error
                      Recovery = null
                      Presentation = null
                      DocumentIdentity = noDocumentProjection }
            else
                match reloaded.Match with
                | null -> return stateConflict ()
                | loaded ->
                    if loaded.Document.StartCommand.ClientCommandId = commandId then
                        if
                            String.Equals(
                                loaded.Document.StartCommand.Fingerprint,
                                requestFingerprint,
                                StringComparison.Ordinal
                            )
                        then
                            return
                                { View = toView loaded displayName
                                  Error = null
                                  Recovery = null
                                  Presentation = null
                                  DocumentIdentity = documentProjection loaded }
                        else
                            return
                                failed
                                    "match.command_conflict"
                                    "This request conflicts with a saved move. Start the battle again."
                    elif
                        loaded.Document.ClientCommands
                        |> Seq.exists (fun receipt -> receipt.ClientCommandId = commandId)
                    then
                        return
                            failed
                                "match.command_conflict"
                                "This request conflicts with a saved move. Start the battle again."
                    else
                        return stateConflict ()
        }

    let reconcileActionConflict
        (context: MatchContext)
        (profile: LocalProfile)
        (displayName: string)
        (commandId: Guid)
        (requestFingerprint: string)
        (cancellationToken: CancellationToken)
        =
        let load = load context
        let toView = toView context

        task {
            let! reloaded = load profile cancellationToken

            if not (isNull (box reloaded.Error)) then
                return
                    { View = null
                      Error = reloaded.Error
                      Recovery = null
                      Presentation = null
                      DocumentIdentity = noDocumentProjection }
            else
                match reloaded.Match with
                | null -> return stateConflict ()
                | loaded ->
                    if loaded.Document.StartCommand.ClientCommandId = commandId then
                        return
                            failed
                                "match.command_conflict"
                                "This request conflicts with the saved battle. Select the move again."
                    else
                        match
                            loaded.Document.ClientCommands.SingleOrDefault(fun candidate ->
                                candidate.ClientCommandId = commandId)
                        with
                        | Null -> return stateConflict ()
                        | NonNull receipt ->
                            if
                                String.Equals(
                                    receipt.Fingerprint,
                                    requestFingerprint,
                                    StringComparison.Ordinal
                                )
                            then
                                return
                                    { View = toView loaded displayName
                                      Error = null
                                      Recovery = null
                                      Presentation = null
                                      DocumentIdentity = documentProjection loaded }
                            else
                                return
                                    failed
                                        "match.command_conflict"
                                        "This request conflicts with a saved move. Select the move again."
        }

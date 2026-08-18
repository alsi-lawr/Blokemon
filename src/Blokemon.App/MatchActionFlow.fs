namespace Blokemon.App

open System
open System.Collections.Generic
open System.Linq
open System.Text.Json
open System.Threading
open Blokemon.App.Contracts
open Blokemon.App.DamagedDocument
open Blokemon.App.MatchCommandTranslation
open Blokemon.App.MatchConflicts
open Blokemon.App.MatchCueProjection
open Blokemon.App.MatchFailures
open Blokemon.App.MatchIdentity
open Blokemon.App.MatchPayloads
open Blokemon.App.MatchReplay
open Blokemon.App.MatchStore
open Blokemon.App.MatchViewProjection
open Blokemon.Product
open Blokemon.Game

/// Applying one player move: the request is settled against the saved receipts, the engine
/// applies it, the computer answers, and the document is written once.
module internal MatchActionFlow =

    let apply
        (context: MatchContext)
        (profile: LocalProfile)
        (displayName: string)
        (routeMatchId: Guid)
        (request: ApplyMatchActionRequest)
        (cancellationToken: CancellationToken)
        =
        let documents = context.Documents
        let engine = context.Engine
        let load = load context
        let toView = toView context
        let toPresentation = toPresentation context
        let advanceCpu = advanceCpu context
        let reconcileActionConflict = reconcileActionConflict context

        task {
            let submittedChoices = orEmpty request.Choices

            if request.CommandId = Guid.Empty then
                return failed "match.command_id" "Select the move again."
            elif
                String.IsNullOrWhiteSpace request.ActionId
                || submittedChoices
                   |> Seq.exists (fun choice -> not (choiceSubmissionIsStructurallyValid choice))
            then
                return failed "match.choice_invalid" "A submitted choice is invalid."
            else

                let! loaded = load profile cancellationToken

                if not (isNull (box loaded.Error)) then
                    return
                        { View = null
                          Error = loaded.Error
                          Presentation = null }
                else

                    match loaded.Match with
                    | null ->
                        return failed "match.required" "Start a battle before you select a move."
                    | current ->

                        let requestPayload = actionPayload routeMatchId request
                        let payloadFingerprint = fingerprint requestPayload

                        if current.Document.StartCommand.ClientCommandId = request.CommandId then
                            return
                                failed
                                    "match.command_conflict"
                                    "This request conflicts with the saved battle. Select the move again."
                        else

                            let receipt =
                                current.Document.ClientCommands.SingleOrDefault(fun candidate ->
                                    candidate.ClientCommandId = request.CommandId)

                            match receipt with
                            | NonNull saved ->
                                if
                                    String.Equals(
                                        saved.Fingerprint,
                                        payloadFingerprint,
                                        StringComparison.Ordinal
                                    )
                                then
                                    return
                                        { View = toView current displayName
                                          Error = null
                                          Presentation = null }
                                else
                                    return
                                        failed
                                            "match.command_conflict"
                                            "This move conflicts with a saved move. Select the move again."
                            | Null ->

                                match Guid.TryParse current.State.Id.Value with
                                | false, _ ->
                                    return
                                        failed
                                            "match.replay_invalid"
                                            "The saved battle is damaged. No data changed."
                                | true, persistedMatchId ->

                                    if persistedMatchId <> routeMatchId then
                                        return
                                            failed "match.wrong_match" "This battle is not active."
                                    elif current.State.Phase = MatchPhase.Complete then
                                        return
                                            failed
                                                "match.complete"
                                                "This battle is complete. Start a new battle."
                                    elif
                                        current.State.Revision.Value <> request.ExpectedRevision
                                    then
                                        return
                                            failed
                                                "match.stale"
                                                "The battle changed. Select the move again."
                                    else

                                        let human = humanPlayer profile

                                        match
                                            engine
                                                .GetLegalActions(current.State, human)
                                                .SingleOrDefault(fun candidate ->
                                                    String.Equals(
                                                        candidate.StableKey,
                                                        request.ActionId,
                                                        StringComparison.Ordinal
                                                    ))
                                        with
                                        | null ->
                                            return
                                                failed
                                                    "match.action_illegal"
                                                    "You cannot use that move now."
                                        | action ->

                                            let materialized =
                                                materializeHumanCommand
                                                    action
                                                    current.State
                                                    human
                                                    request.CommandId
                                                    submittedChoices

                                            if not (isNull (box materialized.Error)) then
                                                return
                                                    { View = null
                                                      Error = materialized.Error
                                                      Presentation = null }
                                            else

                                                match materialized.Command with
                                                | null ->
                                                    return
                                                        failed
                                                            "match.action_illegal"
                                                            "You cannot use that move now."
                                                | command ->

                                                    match engine.Apply(current.State, command) with
                                                    | :? CommandOutcome.Rejected as rejected ->
                                                        return
                                                            { View = null
                                                              Error =
                                                                rejection rejected.Rejection.Code
                                                              Presentation = null }
                                                    | outcome ->
                                                        let applied =
                                                            outcome :?> CommandOutcome.Applied

                                                        let commands =
                                                            List<MatchCommand>(
                                                                current.Document.Commands
                                                            )

                                                        commands.Add command

                                                        let events =
                                                            List<MatchEvent>(current.Events)

                                                        events.AddRange applied.Events

                                                        let presentation =
                                                            List<PendingPresentation>(
                                                                [ { State = applied.State
                                                                    Events = applied.Events } ]
                                                            )

                                                        let advanced =
                                                            advanceCpu
                                                                applied.State
                                                                commands
                                                                events
                                                                presentation

                                                        if not (isNull (box advanced.Error)) then
                                                            return
                                                                { View = null
                                                                  Error = advanced.Error
                                                                  Presentation = null }
                                                        else

                                                            let clientCommands =
                                                                List<MatchClientCommandReceipt>(
                                                                    current.Document.ClientCommands
                                                                )

                                                            clientCommands.Add
                                                                { ClientCommandId =
                                                                    request.CommandId
                                                                  Fingerprint = payloadFingerprint
                                                                  RequestPayload = requestPayload
                                                                  AppliedCommand = command.Id
                                                                  ResultRevision =
                                                                    advanced.State.Revision }

                                                            let document =
                                                                { current.Document with
                                                                    Commands =
                                                                        FrozenList<MatchCommand>
                                                                            .Create
                                                                            commands
                                                                    ClientCommands =
                                                                        FrozenList<
                                                                            MatchClientCommandReceipt
                                                                         >.Create
                                                                            clientCommands }

                                                            let! write =
                                                                documents.Update(
                                                                    matchKey,
                                                                    current.DocumentRevision,
                                                                    JsonSerializer.Serialize(
                                                                        document,
                                                                        MatchJson.Options
                                                                    ),
                                                                    cancellationToken
                                                                )

                                                            match write with
                                                            | :? DocumentWriteResult.Written as written ->
                                                                let committed =
                                                                    { DocumentRevision =
                                                                        written.Revision
                                                                      Document = document
                                                                      State = advanced.State
                                                                      Events =
                                                                        FrozenList<MatchEvent>
                                                                            .Create
                                                                            events }

                                                                context.Cached <- committed

                                                                return
                                                                    { View =
                                                                        toView committed displayName
                                                                      Error = null
                                                                      Presentation =
                                                                        toPresentation
                                                                            document
                                                                            displayName
                                                                            presentation }
                                                            | _ ->
                                                                return!
                                                                    reconcileActionConflict
                                                                        profile
                                                                        displayName
                                                                        request.CommandId
                                                                        payloadFingerprint
                                                                        cancellationToken
        }

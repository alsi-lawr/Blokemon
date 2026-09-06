namespace Blokemon.App

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Text.Json
open System.Threading
open Blokemon.App.Contracts
open Blokemon.App.MatchCueProjection
open Blokemon.App.MatchFailures
open Blokemon.App.MatchStore
open Blokemon.App.MatchViewProjection
open Blokemon.Product
open Blokemon.Game

/// One decision of the computer's turn: the decider names a candidate, the engine issues the
/// command for it, and the document is written with that one command. The client asks again
/// while the computer still has the turn, so each pause is one decision long and every move is
/// shown as it is made.
module internal MatchComputerFlow =

    /// Whether the computer has a move to make, read the way the frame reads the opponent's side
    /// so the client and this flow agree on whose turn it is.
    let computerToAct (engine: MatchEngine) (state: MatchState) =
        state.Phase <> MatchPhase.Complete
        && engine.GetLegalActions(state, cpuPlayer)
           |> Seq.exists (fun action ->
               action.Kind <> LegalActionKind.Resign
               && action.Affordability = ActionAffordability.Payable)

    let private commandsThisTurn (commands: ImmutableArray<MatchCommand>) =
        commands
        |> Seq.rev
        |> Seq.takeWhile (fun command -> command.Actor = cpuPlayer)
        |> Seq.length

    let advance
        (context: MatchContext)
        (profile: LocalProfile)
        (displayName: string)
        (routeMatchId: Guid)
        (request: AdvanceComputerRequest)
        (cancellationToken: CancellationToken)
        =
        let documents = context.Documents
        let engine = context.Engine
        let load = load context
        let toView = toView context
        let toPresentation = toPresentation context

        let unchanged (current: LoadedMatch) : MatchProjectionResult =
            { View = toView current displayName
              Error = null
              Recovery = null
              Presentation = null
              DocumentIdentity = documentProjection current }

        task {
            let! loaded = load profile cancellationToken

            if not (isNull (box loaded.Error)) then
                return
                    { View = null
                      Error = loaded.Error
                      Recovery = null
                      Presentation = null
                      DocumentIdentity = noDocumentProjection }
            else

                match loaded.Match with
                | null ->
                    return failed "match.required" "Start a battle before the computer can move."
                | current ->

                    match Guid.TryParse current.State.Id.Value with
                    | false, _ ->
                        return
                            failed
                                "match.replay_invalid"
                                "The saved battle is damaged. No data changed."
                    | true, persistedMatchId ->

                        if persistedMatchId <> routeMatchId then
                            return failed "match.wrong_match" "This battle is not active."
                        elif current.State.Phase = MatchPhase.Complete then
                            return
                                failed
                                    "match.complete"
                                    "This battle is complete. Start a new battle."
                        elif current.State.Revision.Value <> request.ExpectedRevision then
                            return failed "match.stale" "The battle changed."
                        elif not (computerToAct engine current.State) then
                            // Nothing to decide and nothing written: the player has the turn.
                            return unchanged current
                        elif
                            commandsThisTurn current.Document.Commands >= maximumCpuCommandsPerTurn
                        then
                            return
                                failed "match.cpu_limit" "The computer could not complete its turn."
                        else

                            let! candidate =
                                context.Decider.Decide(
                                    current.Document,
                                    current.State,
                                    cancellationToken
                                )

                            match candidate with
                            | null -> return unchanged current
                            | candidate ->

                                match
                                    engine.TryMaterializeCpuCommand(
                                        current.State,
                                        cpuPlayer,
                                        { CpuCandidateId.Value = candidate }
                                    )
                                with
                                | ValueNone ->
                                    return
                                        failed
                                            "match.cpu_rejected"
                                            "The computer made an invalid move."
                                | ValueSome command ->

                                    match engine.Apply(current.State, command) with
                                    | CommandOutcome.Rejected _ ->
                                        return
                                            failed
                                                "match.cpu_rejected"
                                                "The computer made an invalid move."
                                    | CommandOutcome.Applied(appliedState, appliedEvents) ->

                                        match
                                            MatchCpuPolicy.tryAdvance current.Document.CpuPolicy
                                        with
                                        | None ->
                                            let error = invalidReplayError ()
                                            return failed error.Code error.Message
                                        | Some advancedPolicy ->

                                            let commands =
                                                List<MatchCommand>(current.Document.Commands)

                                            commands.Add command
                                            let events = List<MatchEvent>(current.Events)
                                            events.AddRange appliedEvents

                                            let document =
                                                { current.Document with
                                                    CpuPolicy = advancedPolicy
                                                    Commands = ImmutableArray.CreateRange commands }

                                            let documentJson =
                                                JsonSerializer.Serialize(
                                                    document,
                                                    MatchJson.Options
                                                )

                                            let! write =
                                                documents.Update(
                                                    context.Keys.Match,
                                                    current.DocumentRevision,
                                                    documentJson,
                                                    cancellationToken
                                                )

                                            match write with
                                            | :? DocumentWriteResult.Written as written ->
                                                let committed =
                                                    { DocumentRevision = written.Revision
                                                      DocumentContentIdentity =
                                                        DocumentIdentity.ofText documentJson
                                                      Document = document
                                                      State = appliedState
                                                      Events = ImmutableArray.CreateRange events }

                                                context.Cached <- committed

                                                return
                                                    { View = toView committed displayName
                                                      Error = null
                                                      Recovery = null
                                                      Presentation =
                                                        toPresentation
                                                            document
                                                            displayName
                                                            (attackInTheAir
                                                                current.State
                                                                current.Events)
                                                            [ { State = appliedState
                                                                Events = appliedEvents } ]
                                                      DocumentIdentity =
                                                        documentProjection committed }
                                            | _ -> return stateConflict ()
        }

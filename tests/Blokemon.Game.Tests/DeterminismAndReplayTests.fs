namespace Blokemon.Game.Tests

open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Game
open Blokemon.Cpu
open FsUnit
open TUnit.Core

type DeterminismAndReplayTests() =

    [<Test>]
    member _.``the same seed and the same commands should reproduce the same events and the same state``
        ()
        =
        let engine = MatchScenario.Engine()
        let request = MatchScenario.StartRequest()

        let firstState, firstEvents =
            match engine.Start request with
            | MatchStartOutcome.Started(state, events) -> state, events
            | MatchStartOutcome.Rejected _ -> failwith "The start was rejected."

        let repeatedState, repeatedEvents =
            match engine.Start request with
            | MatchStartOutcome.Started(state, events) -> state, events
            | MatchStartOutcome.Rejected _ -> failwith "The start was rejected."

        repeatedState |> should equal firstState
        repeatedEvents |> should equal firstEvents

        let commands = List<MatchCommand>()
        let mutable state = firstState

        for player in [ MatchScenario.FirstPlayer; MatchScenario.SecondPlayer ] do
            let command =
                engine.GetLegalActions(state, player)
                |> Seq.find (fun action -> action.Kind = LegalActionKind.ChooseOpening)
                |> fun action -> action.Command

            commands.Add command
            state <- MatchScenario.Applied(engine.Apply(state, command))

        let endRound =
            MatchScenario.Command
                state
                "end-round"
                state.ActivePlayer
                ImmutableArray<_>.Empty
                MatchAction.EndRound

        commands.Add endRound
        state <- MatchScenario.Applied(engine.Apply(state, endRound))

        // Replay is no longer an engine primitive, so the same guarantee is stated the way callers
        // now have to state it: start again from the request and apply the recorded log in order.
        let mutable replayed = MatchScenario.Started(engine.Start request)

        for command in commands do
            replayed <- MatchScenario.Applied(engine.Apply(replayed, command))

        replayed |> should equal state

    [<Test>]
    member _.``the deterministic policy should select the same legal action every time``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.Started(engine.Start(MatchScenario.StartRequest()))
        let cpu = DeterministicCpu()

        let first = cpu.Choose(engine, state, MatchScenario.FirstPlayer)
        let repeated = cpu.Choose(engine, state, MatchScenario.FirstPlayer)

        repeated |> should equal first

        match first with
        | CpuDecision.Selected _ -> ()
        | CpuDecision.NoLegalAction -> failwith "Expected a legal action to be available."

    [<Test>]
    member _.``the last event of a commit should carry exactly the state the command produced``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.Started(engine.Start(MatchScenario.StartRequest()))

        let command =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.find (fun action -> action.Kind = LegalActionKind.ChooseOpening)
            |> fun action -> action.Command

        let applied, events =
            match engine.Apply(state, command) with
            | CommandOutcome.Applied(applied, events) -> applied, events
            | CommandOutcome.Rejected _ -> failwith "The command was rejected."

        let last = events[events.Length - 1]
        last.Kind |> should equal MatchEventKind.StateCommitted
        last.CommittedState |> should equal (ValueSome applied)

        events
        |> Seq.take (events.Length - 1)
        |> Seq.forall (fun recorded -> recorded.CommittedState.IsNone)
        |> should be True

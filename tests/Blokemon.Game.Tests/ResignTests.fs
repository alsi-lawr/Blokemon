namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Game
open Blokemon.Cpu
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private ResignFixtures =

    let resignCommand (state: MatchState) (actor: PlayerId) =
        MatchScenario.Command
            state
            $"resign:{actor.Value}"
            actor
            ImmutableArray<_>.Empty
            MatchAction.Resign

    let private baseState () =
        MatchScenario.BattleState "BLK-039" "BLK-001" [ "VIM-DODGY" ] 11UL

    let private pendingEffectChoiceState () =
        let state = baseState ()
        let command = resignCommand state MatchScenario.FirstPlayer

        { state with
            Phase = MatchPhase.AwaitingEffectChoice
            PendingEffect =
                ValueSome
                    { Command = command
                      Source = CardInstanceId "attacker"
                      Effect = EffectId "BLK-012-B01"
                      Chooser = MatchScenario.SecondPlayer
                      Requirements = ImmutableArray<_>.Empty
                      BeerMatResults = ImmutableArray<_>.Empty
                      AttackStarted = true } }

    let private pendingReplacementState () =
        let state = baseState ()

        let bench =
            MatchScenario.PlainCard "bench" "BLK-004" MatchScenario.SecondPlayer CardZone.Booth 0

        { MatchScenario.WithCards state [ bench ] with
            Phase = MatchPhase.AwaitingReplacement
            ReplacementPlayer = ValueSome MatchScenario.SecondPlayer }

    let livePhases () =
        let suddenDeath =
            { baseState () with
                SuddenDeathCount = 1 }

        [ "own turn", baseState ()
          "sudden death", suddenDeath
          "pending effect choice", pendingEffectChoiceState ()
          "pending replacement", pendingReplacementState () ]

    let bothPlayers = [ MatchScenario.FirstPlayer; MatchScenario.SecondPlayer ]

type ResignTests() =

    [<Test>]
    member _.``resigning should be legal for both players in every live phase``() =
        let engine = MatchScenario.Engine()

        for _, state in livePhases () do
            for actor in bothPlayers do
                engine.GetLegalActions(state, actor)
                |> Seq.filter (fun action -> action.Kind = LegalActionKind.Resign)
                |> Seq.length
                |> should equal 1

    [<Test>]
    member _.``resigning should complete the match for the opponent and abandon every pending requirement``
        ()
        =
        let engine = MatchScenario.Engine()

        for name, state in livePhases () do
            for actor in bothPlayers do
                let applied, events =
                    match engine.Apply(state, resignCommand state actor) with
                    | CommandOutcome.Applied(applied, events) -> applied, events
                    | CommandOutcome.Rejected(_, rejection) ->
                        failwith $"{name}: resigning was rejected with {rejection.Code}."

                let opponent = state.Other actor

                applied.Winner |> should equal (ValueSome opponent)
                applied.Phase |> should equal MatchPhase.Complete
                applied.PendingEffect.IsNone |> should be True
                applied.PendingKnockout.IsNone |> should be True
                applied.PendingBarChits.Length |> should equal 0
                applied.ReplacementPlayer.IsNone |> should be True
                applied.PendingRoundEnd |> should be False
                applied.SuddenDeathCount |> should equal state.SuddenDeathCount

                events
                |> Seq.filter (fun matchEvent ->
                    matchEvent.Kind = MatchEventKind.MatchWon
                    && matchEvent.Actor = ValueSome opponent)
                |> Seq.length
                |> should equal 1

    [<Test>]
    member _.``resigning should leave no further legal action and no acceptable command``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-003" "BLK-001" [ "VIM-BLAZED" ] 5UL

        let resigned =
            MatchScenario.Applied(
                engine.Apply(state, resignCommand state MatchScenario.FirstPlayer)
            )

        let endRound =
            MatchScenario.Command
                resigned
                "after-resign"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                MatchAction.EndRound

        let rejectedState, rejection =
            MatchScenario.Rejected(engine.Apply(resigned, endRound))

        rejection.Code |> should equal CommandRejectionCode.MatchComplete
        rejectedState |> should equal resigned

        engine.GetLegalActions(resigned, MatchScenario.FirstPlayer).Length
        |> should equal 0

        engine.GetLegalActions(resigned, MatchScenario.SecondPlayer).Length
        |> should equal 0

    [<Test>]
    member _.``resigning a completed match should be refused like any other command``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-003" "BLK-001" [ "VIM-BLAZED" ] 5UL

        let resigned =
            MatchScenario.Applied(
                engine.Apply(state, resignCommand state MatchScenario.FirstPlayer)
            )

        let rejectedState, rejection =
            MatchScenario.Rejected(
                engine.Apply(resigned, resignCommand resigned MatchScenario.SecondPlayer)
            )

        rejection.Code |> should equal CommandRejectionCode.MatchComplete
        rejectedState |> should equal resigned
        rejectedState.Winner |> should equal (ValueSome MatchScenario.SecondPlayer)

    [<Test>]
    member _.``the deterministic policy should never resign and should still act in every live phase``
        ()
        =
        let engine = MatchScenario.Engine()
        let cpu = DeterministicCpu()

        for _, state in livePhases () do
            for actor in bothPlayers do
                let decision = cpu.Choose(engine, state, actor)

                let available =
                    engine.GetLegalActions(state, actor)
                    |> Seq.filter (fun action -> action.Kind <> LegalActionKind.Resign)
                    |> Seq.toArray

                let resigned =
                    match decision with
                    | CpuDecision.Selected action -> action.Kind = LegalActionKind.Resign
                    | CpuDecision.NoLegalAction -> false

                let acted =
                    match decision with
                    | CpuDecision.Selected _ -> true
                    | CpuDecision.NoLegalAction -> false

                resigned |> should be False
                acted |> should equal (available.Length > 0)

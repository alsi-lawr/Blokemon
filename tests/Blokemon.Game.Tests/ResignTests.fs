namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Game
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

    let private suddenDeathState () =
        let state = MatchScenario.BattleState "BLK-003" "BLK-001" [ "VIM-BLAZED" ] 11UL

        { state with
            SuddenDeathCount = 1
            Players =
                ImmutableArray.CreateRange(
                    state.Players |> Seq.map (fun player -> { player with BarChitsRemaining = 1 })
                ) }

    let private pendingEffectChoiceState () =
        let engine = MatchScenario.Engine()
        let initial = MatchScenario.BattleState "BLK-012" "BLK-001" [ "VIM-BLAZED" ] 81UL

        let bench =
            MatchScenario.PlainCard
                "defender-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let initial = MatchScenario.WithCards initial [ bench ]

        let requested =
            MatchScenario.Applied(
                engine.Apply(initial, MatchScenario.AttackCommand initial "BLK-012-B01")
            )

        requested.Phase |> should equal MatchPhase.AwaitingEffectChoice
        requested.PendingEffect.IsSome |> should be True
        requested

    let private pendingKnockoutTriggerState () =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-001"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                103UL

        let triggerSource =
            MatchScenario.PlainCard
                "trigger-source"
                "BLK-026"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let movableVim =
            MatchScenario.AttachedCard
                "movable-vim"
                "VIM-BEER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let prize =
            MatchScenario.PlainCard "prize" "VIM-LAIRY" MatchScenario.FirstPlayer CardZone.BarChit 0

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.Create(CardInstanceId "movable-vim") }

        let state =
            MatchScenario.WithCards state [ defender; triggerSource; movableVim; prize ]

        let state = MatchScenario.WithBarChits state MatchScenario.FirstPlayer 1

        let attacked =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
            )

        attacked.Phase |> should equal MatchPhase.AwaitingTriggerChoice
        attacked.PendingKnockout.IsSome |> should be True
        attacked

    let private pendingBarChitTriggerState () =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-001"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                0UL

        let triggeredPrize =
            MatchScenario.PlainCard
                "triggered-prize"
                "BLK-113"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                0

        let extraPrize =
            MatchScenario.PlainCard
                "extra-prize"
                "VIM-LAIRY"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                1

        let defenderBench =
            MatchScenario.PlainCard
                "defender-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state =
            MatchScenario.WithCards state [ triggeredPrize; extraPrize; defenderBench ]

        let state = MatchScenario.WithBarChits state MatchScenario.FirstPlayer 2

        let attacked =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
            )

        attacked.Phase |> should equal MatchPhase.AwaitingTriggerChoice
        attacked.PendingBarChits.Length |> should be (greaterThan 0)
        attacked

    let private pendingReplacementState () =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState
                "BLK-026"
                "BLK-151"
                [ "VIM-BEER"; "VIM-BEER"; "VIM-SOBER" ]
                97UL

        let firstPrize =
            MatchScenario.PlainCard
                "prize-1"
                "VIM-LAIRY"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                0

        let secondPrize =
            MatchScenario.PlainCard
                "prize-2"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                1

        let defenderBench =
            MatchScenario.PlainCard
                "defender-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state = MatchScenario.WithCards state [ firstPrize; secondPrize; defenderBench ]

        let attacked =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-026-B01")
            )

        attacked.Phase |> should equal MatchPhase.AwaitingReplacement

        attacked.ReplacementPlayer
        |> should equal (ValueSome MatchScenario.SecondPlayer)

        attacked

    let livePhases () =
        [ "own turn", MatchScenario.BattleState "BLK-003" "BLK-001" [ "VIM-BLAZED" ] 5UL
          "sudden death", suddenDeathState ()
          "pending effect choice", pendingEffectChoiceState ()
          "pending knockout trigger", pendingKnockoutTriggerState ()
          "pending bar chit trigger", pendingBarChitTriggerState ()
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

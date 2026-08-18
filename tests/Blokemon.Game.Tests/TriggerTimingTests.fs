namespace Blokemon.Game.Tests

open Blokemon.Game
open FsUnit
open TUnit.Core

type TriggerTimingTests() =

    [<Test>]
    member _.``a rule-box knockout should take both remaining bar chits and win the match``() =
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

        let state = MatchScenario.WithCards state [ firstPrize; secondPrize ]
        let state = MatchScenario.WithBarChits state MatchScenario.FirstPlayer 2

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-026-B01")
            )

        (applied.Card firstPrize.Id).Zone |> should equal CardZone.Mitt
        (applied.Card secondPrize.Id).Zone |> should equal CardZone.Mitt

        (applied.Player MatchScenario.FirstPlayer).BarChitsRemaining |> should equal 0

        applied.Winner |> should equal (ValueSome MatchScenario.FirstPlayer)

    [<Test>]
    member _.``a would-be-knocked-out trigger should resolve before the knockout``() =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState
                "BLK-076"
                "BLK-068"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                0UL

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-076-B02")
            )

        let defender = applied.Card(CardInstanceId "defender")
        defender.Zone |> should equal CardZone.Oche
        defender.Damage |> should equal 170

    [<Test>]
    member _.``a damaged-active trigger should place its counters on the attacker before knockouts``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-076" "BLK-107" [ "VIM-LAIRY" ] 107UL

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-076-B01")
            )

        (applied.Card(CardInstanceId "attacker")).Damage |> should equal 30

    [<Test>]
    member _.``a taken-face-down trigger should wait for the bar chit's owner and the policy should complete it``
        ()
        =
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
        attacked.PendingBarChits.Count |> should equal 1

        let cpu = DeterministicCpu()

        let decision =
            MatchScenario.Chosen(cpu.Choose(engine, attacked, MatchScenario.FirstPlayer))

        let resolved = MatchScenario.Applied(engine.Apply(attacked, decision.Command))

        decision.Kind |> should equal LegalActionKind.ResolveBarChitTrigger
        (resolved.Card triggeredPrize.Id).Zone |> should equal CardZone.Booth
        (resolved.Card extraPrize.Id).Zone |> should equal CardZone.Mitt
        resolved.Winner |> should equal (ValueSome MatchScenario.FirstPlayer)

    [<Test>]
    member _.``a knockout vim move should wait for its own owner before the knockout completes``() =
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
                Attachments = FrozenList<CardInstanceId>.Create movableVim.Id }

        let state =
            MatchScenario.WithCards state [ defender; triggerSource; movableVim; prize ]

        let state = MatchScenario.WithBarChits state MatchScenario.FirstPlayer 1

        let attacked =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
            )

        attacked.Phase |> should equal MatchPhase.AwaitingTriggerChoice
        attacked.PendingKnockout.IsSome |> should be True

        (attacked.Card movableVim.Id).AttachedTo
        |> should equal (ValueSome(CardInstanceId "defender"))

        (attacked.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Oche

        let cpu = DeterministicCpu()

        let decision =
            MatchScenario.Chosen(cpu.Choose(engine, attacked, MatchScenario.SecondPlayer))

        let resolved = MatchScenario.Applied(engine.Apply(attacked, decision.Command))

        decision.Kind |> should equal LegalActionKind.ResolveKnockoutTrigger

        (resolved.Card movableVim.Id).AttachedTo
        |> should equal (ValueSome triggerSource.Id)

        (resolved.Card(CardInstanceId "defender")).Zone
        |> should equal CardZone.EmptiesTray

        (resolved.Card prize.Id).Zone |> should equal CardZone.Mitt

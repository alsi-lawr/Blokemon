namespace Blokemon.Game.Tests

open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private DeferredChoiceFixtures =

    let selected (decision: CpuDecision) =
        match decision with
        | CpuDecision.Selected action -> action
        | CpuDecision.NoLegalAction -> failwith "Expected a legal action to be available."

type DeferredChoiceTests() =

    [<Test>]
    member _.``an opponent-chosen discard should wait until the opponent picks their own mitt cards``
        ()
        =
        let engine = MatchScenario.Engine()

        let initial =
            MatchScenario.BattleState
                "BLK-024"
                "BLK-150"
                [ "VIM-DODGY"; "VIM-DODGY"; "VIM-DODGY" ]
                79UL

        let first =
            MatchScenario.PlainCard
                "other-mitt-1"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let second =
            MatchScenario.PlainCard
                "other-mitt-2"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let initial = MatchScenario.WithCards initial [ first; second ]

        let requested =
            MatchScenario.Applied(
                engine.Apply(initial, MatchScenario.AttackCommand initial "BLK-024-B02")
            )

        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne
        let cpu = DeterministicCpu()
        let decision = selected (cpu.Choose(engine, requested, MatchScenario.SecondPlayer))
        let resolved = MatchScenario.Applied(engine.Apply(requested, decision.Command))

        requested.Phase |> should equal MatchPhase.AwaitingEffectChoice
        requirement.Chooser |> should equal MatchScenario.SecondPlayer
        requirement.Minimum |> should equal 2
        (resolved.Card first.Id).Zone |> should equal CardZone.EmptiesTray
        (resolved.Card second.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``an opponent-chosen switch should wait until the opponent chooses and should refuse the wrong chooser``
        ()
        =
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
        (requested.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Oche

        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let wrongChooser =
            MatchScenario.Command
                requested
                "wrong-chooser"
                MatchScenario.FirstPlayer
                (FrozenList<EffectChoice>
                    .Create(
                        EffectChoice.Cards(
                            requirement.Id,
                            FrozenList<CardInstanceId>.Create(CardInstanceId "defender-bench")
                        )
                    ))
                MatchAction.ResolveEffectChoice

        let rejectedState, rejection =
            MatchScenario.Rejected(engine.Apply(requested, wrongChooser))

        rejection.Code |> should equal CommandRejectionCode.WrongChooser
        rejectedState |> should equal requested

        let cpu = DeterministicCpu()
        let decision = selected (cpu.Choose(engine, requested, MatchScenario.SecondPlayer))
        let resolved = MatchScenario.Applied(engine.Apply(requested, decision.Command))

        decision.Kind |> should equal LegalActionKind.ResolveEffectChoice
        resolved.PendingEffect.IsNone |> should be True

        (resolved.Card(CardInstanceId "defender-bench")).Zone
        |> should equal CardZone.Oche

        (resolved.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Booth

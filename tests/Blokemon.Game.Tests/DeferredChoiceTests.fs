namespace Blokemon.Game.Tests

open System.Collections.Immutable
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
    member _.``an opponent-chosen switch should wait until the opponent chooses and should refuse the wrong chooser``
        ()
        =
        let engine = MatchScenario.Engine()
        let initial = MatchScenario.BattleState "BLK-012" "BLK-001" [ "VIM-DODGY" ] 81UL

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
                (ImmutableArray.Create(
                    EffectChoice.Cards(
                        requirement.Id,
                        ImmutableArray.Create(CardInstanceId "defender-bench")
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

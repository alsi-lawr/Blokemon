namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private InterpreterBranchingFixtures =

    let seedFor (firstBadge: bool) =
        let rec search (seed: uint64) =
            if seed >= 100UL then
                0UL
            else
                let random = BlokemonSeededRandom seed

                if (random.NextInt 2 = 1) = firstBadge then
                    seed
                else
                    search (seed + 1UL)

        search 0UL

type InterpreterBranchingTests() =

    [<Test>]
    member _.``the current authority should contain no effect the interpreter cannot run``() =
        let audit = BlokemonInterpreter(MatchScenario.Authority).AuditAuthority()

        audit.IsInventoryComplete |> should be True
        audit.EffectCount |> should equal 298
        audit.InstructionCount |> should equal 629

    [<Test>]
    member _.``a beer-mat conditional should take its badge and blank branches deterministically``
        ()
        =
        let badgeSeed = seedFor true
        let blankSeed = seedFor false
        let engine = MatchScenario.Engine()

        let badgeState =
            MatchScenario.BattleState "BLK-056" "BLK-001" [ "VIM-LAIRY" ] badgeSeed

        let blankState =
            MatchScenario.BattleState "BLK-056" "BLK-001" [ "VIM-LAIRY" ] blankSeed

        let badgeResult =
            MatchScenario.Applied(
                engine.Apply(badgeState, MatchScenario.AttackCommand badgeState "BLK-056-B01")
            )

        let blankResult =
            MatchScenario.Applied(
                engine.Apply(blankState, MatchScenario.AttackCommand blankState "BLK-056-B01")
            )

        (badgeResult.Card(CardInstanceId "defender")).Damage |> should equal 40
        (badgeResult.Card(CardInstanceId "attacker")).Damage |> should equal 0
        (blankResult.Card(CardInstanceId "defender")).Damage |> should equal 20
        (blankResult.Card(CardInstanceId "attacker")).Damage |> should equal 20

    [<Test>]
    member _.``optional card movement should require the branch to be chosen explicitly``() =
        let state = MatchScenario.BattleState "BLK-008" "BLK-003" [ "VIM-SOBER" ] 41UL
        let engine = MatchScenario.Engine()

        let rejectedState, missing =
            MatchScenario.Rejected(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-008-B01")
            )

        let optional =
            missing.ChoiceRequirements
            |> Seq.find (fun requirement -> requirement.Kind = ChoiceRequirementKind.Optional)

        let declined =
            MatchScenario.AttackCommandWith
                state
                "BLK-008-B01"
                (ImmutableArray.Create(EffectChoice.Optional(optional.Id, false)))

        let accepted = engine.Apply(state, declined)

        missing.Code |> should equal CommandRejectionCode.ChoiceRequired
        obj.ReferenceEquals(rejectedState, state) |> should be True

        match accepted with
        | CommandOutcome.Applied _ -> ()
        | CommandOutcome.Rejected(_, rejection) ->
            failwith $"Expected the declined branch to be accepted, received {rejection.Code}."

    [<Test>]
    member _.``a beer-mat sibling effect should not swap on the blank side``() =
        let blankSeed = seedFor false

        let state = MatchScenario.BattleState "BLK-052" "BLK-001" [ "VIM-SOBER" ] blankSeed

        let boothCard =
            MatchScenario.PlainCard
                "other-booth"
                "BLK-002"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state = MatchScenario.WithCards state [ boothCard ]
        let engine = MatchScenario.Engine()

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-052-B01")
            )

        applied.PendingEffect.IsNone |> should be True
        (applied.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Oche
        (applied.Card boothCard.Id).Zone |> should equal CardZone.Booth

    [<Test>]
    member _.``a required choice should be a legal action the deterministic policy can answer``() =
        let state =
            MatchScenario.BattleState "BLK-052" "BLK-001" [ "VIM-SOBER" ] (seedFor true)

        let boothCard =
            MatchScenario.PlainCard
                "other-booth"
                "BLK-002"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state = MatchScenario.WithCards state [ boothCard ]
        let engine = MatchScenario.Engine()

        let legalAttack =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.filter (fun action -> action.Kind = LegalActionKind.Attack)
            |> Seq.exactlyOne

        let cpu = DeterministicCpu()

        let attackDecision =
            match cpu.Choose(engine, state, MatchScenario.FirstPlayer) with
            | CpuDecision.Selected action -> action
            | CpuDecision.NoLegalAction -> failwith "Expected an attack to be available."

        let requested = MatchScenario.Applied(engine.Apply(state, attackDecision.Command))
        let pending = requested.PendingEffect.Value

        let wrongChooser =
            MatchScenario.Command
                requested
                "wrong-branch-chooser"
                MatchScenario.SecondPlayer
                (ImmutableArray.Create(
                    EffectChoice.Cards(
                        (pending.Requirements |> Seq.exactlyOne).Id,
                        ImmutableArray.Create boothCard.Id
                    )
                ))
                MatchAction.ResolveEffectChoice

        let rejectedState, rejection =
            MatchScenario.Rejected(engine.Apply(requested, wrongChooser))

        let choiceDecision =
            match cpu.Choose(engine, requested, MatchScenario.FirstPlayer) with
            | CpuDecision.Selected action -> action
            | CpuDecision.NoLegalAction -> failwith "Expected the choice to be available."

        let resolved =
            MatchScenario.Applied(engine.Apply(requested, choiceDecision.Command))

        legalAttack.ChoiceRequirements.Length |> should equal 0
        legalAttack.Command.Choices.Length |> should equal 0
        attackDecision.Kind |> should equal LegalActionKind.Attack
        requested.Phase |> should equal MatchPhase.AwaitingEffectChoice

        (pending.Requirements |> Seq.exactlyOne).Chooser
        |> should equal MatchScenario.FirstPlayer

        rejection.Code |> should equal CommandRejectionCode.WrongChooser
        rejectedState |> should equal requested
        choiceDecision.Kind |> should equal LegalActionKind.ResolveEffectChoice
        resolved.PendingEffect.IsNone |> should be True
        (resolved.Card boothCard.Id).Zone |> should equal CardZone.Oche

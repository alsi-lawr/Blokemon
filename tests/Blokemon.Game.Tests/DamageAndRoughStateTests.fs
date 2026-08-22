namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private DamageScenarios =

    let matchingAttackDamageWithAndWithoutProtectiveGoggles (defenderId: string) =
        let control =
            MatchScenario.BattleState "BLK-001" defenderId [ "VIM-BLAZED"; "VIM-SOBER" ] 17UL

        let attackDamage (state: MatchState) =
            let applied =
                MatchScenario.Applied(
                    MatchScenario
                        .Engine()
                        .Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
                )

            (applied.Card(CardInstanceId "defender")).Damage

        let defender = control.Card(CardInstanceId "defender")

        let protectiveGoggles =
            MatchScenario.AttachedCard
                "protective-goggles"
                "KIT-013"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                defender.Id

        let protectedState =
            MatchScenario.WithCards
                control
                [ { defender with
                      Attachments = ImmutableArray.Create protectiveGoggles.Id }
                  protectiveGoggles ]

        attackDamage protectedState, attackDamage control

type DamageAndRoughStateTests() =

    [<Test>]
    member _.``protective goggles should remove a regular defender's soft spot``() =
        let protectedDamage, controlDamage =
            matchingAttackDamageWithAndWithoutProtectiveGoggles "BLK-027"

        protectedDamage |> should be (lessThan controlDamage)
        controlDamage |> should equal (protectedDamage * 2)

    [<Test>]
    member _.``protective goggles should leave a seasoned defender's soft spot intact``() =
        let protectedDamage, controlDamage =
            matchingAttackDamageWithAndWithoutProtectiveGoggles "BLK-028"

        protectedDamage |> should equal controlDamage

    [<Test>]
    member _.``attack damage should apply the soft spot before the defender's own reduction``() =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-028" [ "VIM-BLAZED"; "VIM-SOBER" ] 17UL

        let defender = state.Card(CardInstanceId "defender")

        let barKit =
            MatchScenario.AttachedCard
                "bar-kit"
                "KIT-014"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                defender.Id

        let state =
            MatchScenario.WithCards
                state
                [ { defender with
                      Attachments = ImmutableArray.Create barKit.Id }
                  barKit ]

        let engine = MatchScenario.Engine()

        let result =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        (result.Card(CardInstanceId "defender")).Damage |> should equal 10

        (result.CardsIn(MatchScenario.FirstPlayer, CardZone.Oche) |> Seq.exactlyOne).Damage
        |> should equal 0

    [<Test>]
    member _.``the checkup should resolve marker damage before recovery and clear an expired legless``
        ()
        =
        let roughStates =
            ImmutableArray.Create(
                MatchScenario.RoughState BlokemonRoughState.DodgyPint 2,
                MatchScenario.RoughState BlokemonRoughState.Singed 2,
                MatchScenario.RoughState BlokemonRoughState.Legless 1
            )

        let state =
            MatchScenario.BattleStateWith
                "BLK-003"
                "BLK-001"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                23UL
                roughStates
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        let engine = MatchScenario.Engine()

        let command =
            MatchScenario.Command
                state
                "checkup"
                MatchScenario.FirstPlayer
                ImmutableArray<_>.Empty
                MatchAction.EndRound

        let applied, events =
            match engine.Apply(state, command) with
            | CommandOutcome.Applied(applied, events) -> applied, events
            | CommandOutcome.Rejected _ -> failwith "The command was rejected."

        let checkedCard = applied.Card(CardInstanceId "attacker")

        let damageEvents =
            events
            |> Seq.filter (fun matchEvent ->
                matchEvent.Kind = MatchEventKind.DamagePlaced
                && matchEvent.SourceCard.IsNone
                && Seq.contains (CardInstanceId "attacker") matchEvent.TargetCards)

        checkedCard.Damage |> should equal 30

        checkedCard.RoughStates
        |> Seq.exists (fun entry -> entry.State = BlokemonRoughState.DodgyPint)
        |> should be True

        checkedCard.RoughStates
        |> Seq.exists (fun entry -> entry.State = BlokemonRoughState.Legless)
        |> should be False

        damageEvents
        |> Seq.map (fun matchEvent -> matchEvent.Amount)
        |> Seq.sort
        |> Seq.toList
        |> should equal [ 10; 20 ]

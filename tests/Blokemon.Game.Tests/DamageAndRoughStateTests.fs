namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

type DamageAndRoughStateTests() =

    [<Test>]
    /// Advanced Rulebook v1, pp. 5 and 16-18: Poison places one damage counter after each
    /// player's turn, while Paralysis ends after the afflicted player's next turn.
    member _.``the checkup should place Poison damage and clear expired Paralysis``() =
        let roughStates =
            ImmutableArray.Create(
                MatchScenario.RoughState BlokemonRoughState.DodgyPint 2,
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

        checkedCard.Damage |> should equal 10

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
        |> should equal [ 10 ]

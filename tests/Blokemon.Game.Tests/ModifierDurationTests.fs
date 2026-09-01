namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Game
open FsUnit
open TUnit.Core

type ModifierDurationTests() =

    [<Test>]
    member _.``a next-round damage modifier should apply only to its own source on its owner's next round``
        ()
        =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState "BLK-123" "BLK-003" [ "VIM-BLAZED"; "VIM-DODGY" ] 211UL

        let state =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-123-B01")
            )

        let endOpponentRound =
            MatchScenario.Command
                state
                "end-opponent-round"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                MatchAction.EndRound

        let state = MatchScenario.Applied(engine.Apply(state, endOpponentRound))

        let state =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-123-B02")
            )

        (state.Card(CardInstanceId "defender")).Damage |> should equal 60

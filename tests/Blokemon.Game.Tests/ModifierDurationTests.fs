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
            MatchScenario.BattleState
                "BLK-076"
                "BLK-003"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                211UL

        let state =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-076-B01")
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
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-076-B02")
            )

        (state.Card(CardInstanceId "defender")).Damage |> should equal 350

    [<Test>]
    member _.``a continuous global taxi modifier should be recomputed from its declarative source``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-003" [] 223UL

        let source =
            MatchScenario.PlainCard
                "free-taxi-source"
                "BLK-149"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let state =
            { state with
                Cards =
                    ImmutableArray.CreateRange(
                        Seq.append state.Cards [ source ] |> Seq.sortBy (fun card -> card.Id)
                    ) }

        let command =
            MatchScenario.Command
                state
                "free-taxi"
                MatchScenario.FirstPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Taxi(source.Id, ImmutableArray<_>.Empty))

        let applied = MatchScenario.Applied(engine.Apply(state, command))

        (applied.Card source.Id).Zone |> should equal CardZone.Oche
        (applied.Card(CardInstanceId "attacker")).Zone |> should equal CardZone.Booth

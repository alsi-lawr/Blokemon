namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Game
open FsUnit
open TUnit.Core

type TopStackDistributionTests() =

    [<Test>]
    member _.``a promotion trigger should attach only the selected basic vim from the top-stack window``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-043" "BLK-001" [] 59UL

        let retained = state.Cards |> Seq.filter (fun card -> card.Id.Value <> "first-draw")

        let promotion =
            MatchScenario.PlainCard "promotion" "BLK-044" MatchScenario.FirstPlayer CardZone.Mitt -1

        let topGrass =
            MatchScenario.PlainCard
                "top-grass"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Stack
                0

        let topBloke =
            MatchScenario.PlainCard "top-bloke" "BLK-004" MatchScenario.FirstPlayer CardZone.Stack 1

        let topWater =
            MatchScenario.PlainCard
                "top-water"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Stack
                2

        let state =
            { state with
                Cards =
                    ImmutableArray.CreateRange(
                        Seq.append retained [ promotion; topGrass; topBloke; topWater ]
                        |> Seq.sortBy (fun card -> card.Id)
                    ) }

        let choices =
            ImmutableArray.Create(
                EffectChoice.Optional(EffectChoiceId "BLK-044-T01:root/0:optional", true),
                EffectChoice.Attachments(
                    EffectChoiceId "BLK-044-T01:root/0/then/3:attachments",
                    ImmutableArray.Create
                        { Vim = CardInstanceId "top-grass"
                          Bloke = CardInstanceId "promotion" }
                )
            )

        let command =
            MatchScenario.Command
                state
                "promote-with-top-stack-vim"
                MatchScenario.FirstPlayer
                choices
                (MatchAction.Promote(promotion.Id, CardInstanceId "attacker"))

        let applied = MatchScenario.Applied(engine.Apply(state, command))

        (applied.Card topGrass.Id).AttachedTo |> should equal (ValueSome promotion.Id)
        (applied.Card topWater.Id).Zone |> should equal CardZone.Stack
        (applied.Card topWater.Id).AttachedTo.IsNone |> should be True
        (applied.Card topBloke.Id).Zone |> should equal CardZone.Stack

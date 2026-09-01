namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private VintageAttackScenarios =

    let firstBlankSeed () =
        let rec find seed =
            let random = BlokemonSeededRandom seed
            if random.NextInt 2 = 0 then seed else find (seed + 1UL)

        find 0UL

    let attackAction effect (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.find (fun action ->
            match action.Command.Action with
            | MatchAction.Attack(_, attack) -> attack = EffectId effect
            | _ -> false)

type VintageAttackSemanticsTests() =

    [<Test>]
    member _.``rampage should add damage counters to its damage and confuse Tauros only on tails``
        ()
        =
        let original =
            MatchScenario.BattleState
                "BLK-128"
                "BLK-003"
                [ "VIM-DODGY"; "VIM-SOBER" ]
                (firstBlankSeed ())

        let state =
            MatchScenario.WithCards
                original
                [ { original.Card(CardInstanceId "attacker") with
                      Damage = 20 } ]

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-128-B02")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 40

        (applied.Card(CardInstanceId "attacker")).RoughStates
        |> Seq.map (fun entry -> entry.State)
        |> should contain BlokemonRoughState.Muddled

    [<Test>]
    member _.``hurricane should return the Defending Pokemon's whole evolution pile and attachments``
        ()
        =
        let original =
            MatchScenario.BattleState "BLK-018" "BLK-002" [ "VIM-DODGY"; "VIM-SOBER" ] 1502UL

        let underlying =
            MatchScenario.AttachedCard
                "defender-basic"
                "BLK-001"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let energy =
            MatchScenario.AttachedCard
                "defender-energy"
                "VIM-BLAZED"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let defender =
            { original.Card(CardInstanceId "defender") with
                UnderlyingCards = ImmutableArray.Create underlying.Id
                Attachments = ImmutableArray.Create energy.Id }

        let state = MatchScenario.WithCards original [ defender; underlying; energy ]

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-018-B02")
            )

        [ defender.Id; underlying.Id; energy.Id ]
        |> List.map (fun card -> (applied.Card card).Zone)
        |> should equal [ CardZone.Mitt; CardZone.Mitt; CardZone.Mitt ]

        applied.CardsIn(MatchScenario.SecondPlayer, CardZone.Oche) |> should be Empty

    [<Test>]
    member _.``dream Eater should fail against an awake Pokemon and deal fifty to an Asleep one``
        ()
        =
        let awake =
            MatchScenario.BattleState "BLK-093" "BLK-003" [ "VIM-GEEKED"; "VIM-GEEKED" ] 1503UL

        MatchScenario.Engine().Apply(awake, MatchScenario.AttackCommand awake "BLK-093-B02")
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.EffectUnavailable

        let asleep =
            MatchScenario.WithCards
                awake
                [ { awake.Card(CardInstanceId "defender") with
                      RoughStates =
                          ImmutableArray.Create(
                              MatchScenario.RoughState BlokemonRoughState.NoddedOff 1
                          ) } ]

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(asleep, MatchScenario.AttackCommand asleep "BLK-093-B02")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 50

    [<Test>]
    member _.``wildfire should discard the chosen Fire Energy and the same number from the opponent's Deck``
        ()
        =
        let original =
            MatchScenario.BattleState
                "BLK-146"
                "BLK-003"
                [ "VIM-CURRY"; "VIM-CURRY"; "VIM-CURRY" ]
                1504UL

        let extraDeck =
            [ MatchScenario.PlainCard
                  "opponent-deck-1"
                  "VIM-SOBER"
                  MatchScenario.SecondPlayer
                  CardZone.Stack
                  1
              MatchScenario.PlainCard
                  "opponent-deck-2"
                  "VIM-SOBER"
                  MatchScenario.SecondPlayer
                  CardZone.Stack
                  2 ]

        let state = MatchScenario.WithCards original extraDeck
        let action = attackAction "BLK-146-B01" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    EffectChoice.Cards(
                                        requirement.Id,
                                        ImmutableArray.Create(
                                            CardInstanceId "vim-0",
                                            CardInstanceId "vim-1"
                                        )
                                    )
                                ) }
                    )
            )

        [ CardInstanceId "vim-0"; CardInstanceId "vim-1" ]
        |> List.map (fun card -> (applied.Card card).Zone)
        |> should equal [ CardZone.EmptiesTray; CardZone.EmptiesTray ]

        [ CardInstanceId "second-draw"; CardInstanceId "opponent-deck-1" ]
        |> List.map (fun card -> (applied.Card card).Zone)
        |> should equal [ CardZone.EmptiesTray; CardZone.EmptiesTray ]

        (applied.Card(CardInstanceId "opponent-deck-2")).Zone
        |> should equal CardZone.Mitt

    [<Test>]
    member _.``devolution Beam should remove only the highest Evolution card and keep damage and Energy``
        ()
        =
        let original =
            MatchScenario.BattleState "BLK-151" "BLK-002" [ "VIM-GEEKED"; "VIM-GEEKED" ] 1505UL

        let underlying =
            MatchScenario.AttachedCard
                "defender-basic"
                "BLK-001"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let energy =
            MatchScenario.AttachedCard
                "defender-energy"
                "VIM-BLAZED"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let defender =
            { original.Card(CardInstanceId "defender") with
                Damage = 20
                UnderlyingCards = ImmutableArray.Create underlying.Id
                Attachments = ImmutableArray.Create energy.Id }

        let state = MatchScenario.WithCards original [ defender; underlying; energy ]
        let action = attackAction "BLK-151-B02" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    EffectChoice.Cards(
                                        requirement.Id,
                                        ImmutableArray.Create defender.Id
                                    )
                                ) }
                    )
            )

        (applied.Card defender.Id).Zone |> should equal CardZone.Mitt
        (applied.Card underlying.Id).Zone |> should equal CardZone.Oche
        (applied.Card underlying.Id).Damage |> should equal 20

        (applied.Card underlying.Id).Attachments
        |> Seq.toList
        |> should equal [ energy.Id ]

        (applied.Card energy.Id).AttachedTo |> should equal (ValueSome underlying.Id)

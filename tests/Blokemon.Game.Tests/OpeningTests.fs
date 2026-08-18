namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

type OpeningTests() =

    [<Test>]
    member _.``the opening player should be drawn before the decks are shuffled and both explicit placements should start the match``
        ()
        =
        let seed = 0x0C4EUL
        let expectedRandom = BlokemonSeededRandom seed

        let expectedOpening =
            if expectedRandom.NextInt 2 = 0 then
                MatchScenario.FirstPlayer
            else
                MatchScenario.SecondPlayer

        let engine = MatchScenario.Engine()

        let mutable state =
            MatchScenario.Started(engine.Start(MatchScenario.StartRequestWithSeed seed))

        state.OpeningPlayer |> should equal expectedOpening

        state.Players
        |> Seq.forall (fun player -> player.BarChitsRemaining = 6)
        |> should be True

        state.Players
        |> Seq.forall (fun player -> (state.CardsIn(player.Id, CardZone.Mitt) |> Seq.length) = 7)
        |> should be True

        for player in [ MatchScenario.FirstPlayer; MatchScenario.SecondPlayer ] do
            let mitt = state.CardsIn(player, CardZone.Mitt) |> Seq.toArray

            let command =
                MatchScenario.Command
                    state
                    $"opening:{player.Value}"
                    player
                    ImmutableArray<_>.Empty
                    (MatchAction.ChooseOpening(
                        mitt[0].Id,
                        ImmutableArray.CreateRange(
                            mitt |> Seq.skip 1 |> Seq.truncate 5 |> Seq.map (fun card -> card.Id)
                        )
                    ))

            state <- MatchScenario.Applied(engine.Apply(state, command))

        state.Phase |> should equal MatchPhase.Playing
        state.ActivePlayer |> should equal expectedOpening

        state.CardsIn(MatchScenario.FirstPlayer, CardZone.Oche)
        |> Seq.length
        |> should equal 1

        state.CardsIn(MatchScenario.SecondPlayer, CardZone.Oche)
        |> Seq.length
        |> should equal 1

        state.CardsIn(MatchScenario.FirstPlayer, CardZone.Booth)
        |> Seq.length
        |> should equal 5

        state.CardsIn(MatchScenario.SecondPlayer, CardZone.Booth)
        |> Seq.length
        |> should equal 5

        (state.Player expectedOpening).RoundsStarted |> should equal 1

        for player in [ MatchScenario.FirstPlayer; MatchScenario.SecondPlayer ] do
            let barChits = state.CardsIn(player, CardZone.BarChit) |> Seq.toArray
            barChits.Length |> should equal 6
            barChits |> Array.forall (fun card -> card.IsFaceDown) |> should be True

            barChits
            |> Array.map (fun card -> card.Id)
            |> Array.distinct
            |> Array.length
            |> should equal 6

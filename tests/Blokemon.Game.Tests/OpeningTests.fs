namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

type OpeningTests() =

    /// A table stopped on the extra draw: the first player is owed two cards with three in the
    /// deck to take them from, and the second player is owed none.
    let ExtraDrawTable () =
        let state = MatchScenario.BattleState "BLK-001" "BLK-003" [] 41UL

        let deck =
            [ for index in 0..2 ->
                  MatchScenario.PlainCard
                      $"bonus-{index}"
                      "VIM-SOBER"
                      MatchScenario.FirstPlayer
                      CardZone.Stack
                      index ]

        { MatchScenario.WithCards state deck with
            Phase = MatchPhase.MulliganBonus
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun player ->
                        if player.Id = MatchScenario.FirstPlayer then
                            { player with
                                MulliganBonusAllowance = 2
                                MulliganBonusChosen = false }
                        else
                            player)
                ) }

    let TakeExtraDraw (state: MatchState) (cards: int) =
        MatchScenario.Applied(
            MatchScenario
                .Engine()
                .Apply(
                    state,
                    MatchScenario.Command
                        state
                        $"bonus:{state.Revision.Value}:{cards}"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        (MatchAction.ChooseMulliganBonus cards)
                )
        )

    let ExtraDrawsOffered (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action -> action.Kind = LegalActionKind.ChooseMulliganBonus)
        |> Seq.length

    let MittSize (state: MatchState) =
        state.CardsIn(MatchScenario.FirstPlayer, CardZone.Mitt) |> Seq.length

    [<Test>]
    member _.``taking one card of the extra draw should leave the rest of it owed``() =
        // The bonus is taken a card at a time, so a table owed two cards offers taking none, one
        // or both, and taking one leaves a table owed one that offers taking none or that one.
        let state = ExtraDrawTable()
        ExtraDrawsOffered state |> should equal 3

        let afterOne = TakeExtraDraw state 1
        MittSize afterOne |> should equal 1
        afterOne.Phase |> should equal MatchPhase.MulliganBonus

        (afterOne.Player MatchScenario.FirstPlayer).MulliganBonusAllowance
        |> should equal 1

        (afterOne.Player MatchScenario.FirstPlayer).MulliganBonusChosen
        |> should be False

        ExtraDrawsOffered afterOne |> should equal 2

        // Taking the last of it closes the draw. The bonus is now the last step of the setup
        // rather than the first, and this table draws Vim, so there is nothing to bench and
        // closing it begins the game.
        let afterBoth = TakeExtraDraw afterOne 1
        MittSize afterBoth |> should equal 2
        afterBoth.Phase |> should equal MatchPhase.Playing
        ExtraDrawsOffered afterBoth |> should equal 0

    [<Test>]
    member _.``taking none of the extra draw should close it with nothing drawn``() =
        // Declining is still one command and still final: the rest of the allowance goes with it.
        let declined = TakeExtraDraw (ExtraDrawTable()) 0

        MittSize declined |> should equal 0
        declined.Phase |> should equal MatchPhase.Playing
        ExtraDrawsOffered declined |> should equal 0

    [<Test>]
    member _.``taking the whole extra draw at once should close it``() =
        let taken = TakeExtraDraw (ExtraDrawTable()) 2

        MittSize taken |> should equal 2
        taken.Phase |> should equal MatchPhase.Playing
        ExtraDrawsOffered taken |> should equal 0

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

type StaggeredOpeningTests() =

    /// A table stopped on the opening placement, where the second player started over once and
    /// the first did not.
    let StaggeredTable () =
        let state = MatchScenario.BattleState "BLK-001" "BLK-003" [] 7UL

        let mitt =
            [ MatchScenario.PlainCard
                  "opening-first"
                  "BLK-001"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  0
              MatchScenario.PlainCard
                  "opening-second"
                  "BLK-001"
                  MatchScenario.SecondPlayer
                  CardZone.Mitt
                  1 ]

        { MatchScenario.WithCards state mitt with
            Phase = MatchPhase.OpeningPlacement
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun player ->
                        { player with
                            OpeningChosen = false
                            MulliganCount = if player.Id = MatchScenario.SecondPlayer then 1 else 0 })
                ) }

    /// A table stopped on the placement that follows the extra draw, holding one Regular Bloke
    /// the draw put in the Mitt.
    let BonusPlacementTable () =
        let state = MatchScenario.BattleState "BLK-001" "BLK-003" [] 11UL

        let bonus =
            MatchScenario.PlainCard
                "bonus-bloke"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                0

        { MatchScenario.WithCards state [ bonus ] with
            Phase = MatchPhase.BonusPlacement
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun player ->
                        if player.Id = MatchScenario.FirstPlayer then
                            { player with
                                BonusDrawn = ImmutableArray.Create(CardInstanceId "bonus-bloke")
                                BonusPlacementChosen = false }
                        else
                            player)
                ) }

    let OpeningsOffered (engine: MatchEngine) state player =
        engine.GetLegalActions(state, player)
        |> Seq.filter (fun action -> action.Kind = LegalActionKind.ChooseOpening)
        |> Seq.length

    [<Test>]
    member _.``a player who started over should set up only once the other has finished``() =
        // The one who did not start over commits first, and the one who did waits - which is what
        // keeps the extra draw from being seen before either Oche is decided.
        let engine = MatchScenario.Engine()
        let table = StaggeredTable()

        OpeningsOffered engine table MatchScenario.FirstPlayer
        |> should be (greaterThan 0)

        OpeningsOffered engine table MatchScenario.SecondPlayer |> should equal 0

        let placed =
            MatchScenario.Applied(
                engine.Apply(
                    table,
                    MatchScenario.Command
                        table
                        "opening:first"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        (MatchAction.ChooseOpening(
                            CardInstanceId "opening-first",
                            ImmutableArray<_>.Empty
                        ))
                )
            )

        OpeningsOffered engine placed MatchScenario.SecondPlayer
        |> should be (greaterThan 0)

    [<Test>]
    member _.``a Bloke drawn with the extra should reach the Booth and leave the Oche standing``() =
        // The bonus is compensation, not a second opening: what it draws may be benched and may
        // never displace the Blokemon already standing.
        let engine = MatchScenario.Engine()
        let table = BonusPlacementTable()

        let ocheBefore =
            table.CardsIn(MatchScenario.FirstPlayer, CardZone.Oche)
            |> Seq.map _.Id
            |> Seq.toArray

        // Resignation is always on offer and is not part of the question being asked here.
        engine.GetLegalActions(table, MatchScenario.FirstPlayer)
        |> Seq.map _.Kind
        |> Seq.filter (fun kind -> kind <> LegalActionKind.Resign)
        |> Seq.distinct
        |> Seq.toArray
        |> should equal [| LegalActionKind.ChooseBonusPlacement |]

        let placed =
            MatchScenario.Applied(
                engine.Apply(
                    table,
                    MatchScenario.Command
                        table
                        "bonus:booth"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        (MatchAction.ChooseBonusPlacement(
                            ImmutableArray.Create(CardInstanceId "bonus-bloke")
                        ))
                )
            )

        (placed.Card(CardInstanceId "bonus-bloke")).Zone |> should equal CardZone.Booth

        placed.CardsIn(MatchScenario.FirstPlayer, CardZone.Oche)
        |> Seq.map _.Id
        |> Seq.toArray
        |> should equal ocheBefore

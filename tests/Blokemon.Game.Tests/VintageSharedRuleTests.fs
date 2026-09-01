namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private VintageSharedRuleScenarios =

    let withFirstPlayerRounds rounds (state: MatchState) =
        { state with
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun player ->
                        if player.Id = MatchScenario.FirstPlayer then
                            { player with RoundsStarted = rounds }
                        else
                            player)
                ) }

    let firstBlankSeed () =
        let rec find seed =
            let random = BlokemonSeededRandom seed
            if random.NextInt 2 = 0 then seed else find (seed + 1UL)

        find 0UL

    let taxiStateWithEnergy energy roughStates =
        let original =
            MatchScenario.BattleStateWith
                "BLK-040"
                "BLK-001"
                energy
                (firstBlankSeed ())
                roughStates
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        let incoming =
            MatchScenario.PlainCard "incoming" "BLK-004" MatchScenario.FirstPlayer CardZone.Booth 0

        MatchScenario.WithCards original [ incoming ], incoming

    let taxiState roughStates =
        taxiStateWithEnergy [ "VIM-DODGY" ] roughStates

    let usePowerActions state =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action -> action.Kind = LegalActionKind.UsePartyTrick)
        |> Seq.map (fun action -> action.Command.Action)
        |> Seq.toList

    let tiedWinWithoutBasics () =
        MatchScenario.BattleState
            "BLK-002"
            "BLK-002"
            [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
            907UL
        |> fun state -> MatchScenario.WithBarChits state MatchScenario.FirstPlayer 0
        |> fun state -> MatchScenario.WithBarChits state MatchScenario.SecondPlayer 0

    let ownedCardCounts (state: MatchState) =
        state.Players
        |> Seq.map (fun player ->
            state.Cards |> Seq.filter (fun card -> card.Owner = player.Id) |> Seq.length)
        |> Seq.toList

type VintageSharedRuleTests() =

    // Advanced Rulebook Version 1, pp. 2, 10, 14: the first player may attack,
    // and one Double Colorless Energy supplies both Colorless requirements.
    [<Test>]
    member _.``the opening player should attack and Double Colorless should supply two Energy``() =
        let original = MatchScenario.BattleState "BLK-013" "BLK-001" [ "VIM-DODGY" ] 901UL

        let state =
            { withFirstPlayerRounds 1 original with
                OpeningPlayer = MatchScenario.FirstPlayer }

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-013-B02")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20

    // Advanced Rulebook Version 1, pp. 3, 11: neither player may evolve on
    // their first turn, and a Pokemon cannot evolve on the turn it was played.
    [<Test>]
    member _.``evolution should wait until after the Pokemon has survived a turn``() =
        let original = MatchScenario.BattleState "BLK-021" "BLK-001" [] 906UL

        let promotion =
            MatchScenario.PlainCard "promotion" "BLK-022" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state rounds enteredAt =
            let current = withFirstPlayerRounds rounds original

            MatchScenario.WithCards
                current
                [ { current.Card(CardInstanceId "attacker") with
                      EnteredAtOwnerRound = enteredAt }
                  promotion ]

        let command current id =
            MatchScenario.Command
                current
                id
                MatchScenario.FirstPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Promote(promotion.Id, CardInstanceId "attacker"))

        let engine = MatchScenario.Engine()
        let firstTurn = state 1 0
        let justPlayed = state 2 2

        engine.Apply(firstTurn, command firstTurn "first-turn-evolution")
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.IneligiblePromotion

        engine.Apply(justPlayed, command justPlayed "same-turn-evolution")
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.IneligiblePromotion

        let eligible = state 2 1

        let evolved =
            MatchScenario.Applied(engine.Apply(eligible, command eligible "later-evolution"))

        (evolved.Card promotion.Id).Zone |> should equal CardZone.Oche

    // Advanced Rulebook Version 1, pp. 10, 12-13: Energy attaches only to a
    // Pokemon in play and a retreat switches only with a Benched Pokemon.
    [<Test>]
    member _.``energy attachment and retreat should reject Trainer targets``() =
        let original = MatchScenario.BattleState "BLK-013" "BLK-001" [] 902UL

        let trainer =
            MatchScenario.PlainCard "trainer" "KIT-032" MatchScenario.FirstPlayer CardZone.Booth 0

        let energy =
            MatchScenario.PlainCard
                "hand-energy"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state = MatchScenario.WithCards original [ trainer; energy ]
        let engine = MatchScenario.Engine()

        engine.Apply(
            state,
            MatchScenario.Command
                state
                "attach-to-trainer"
                MatchScenario.FirstPlayer
                ImmutableArray<_>.Empty
                (MatchAction.AttachVim(energy.Id, trainer.Id))
        )
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.RuleLimitReached

        engine.Apply(
            state,
            MatchScenario.Command
                state
                "retreat-to-trainer"
                MatchScenario.FirstPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Taxi(trainer.Id, ImmutableArray<_>.Empty))
        )
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.WrongZone

    // Advanced Rulebook Version 1, pp. 13, 23: discard Energy cards one at a
    // time until the Retreat Cost is met; Double Colorless can pay a cost of two alone.
    [<Test>]
    member _.``one Double Colorless should pay two Retreat Cost units as one discarded card``() =
        let state, incoming = taxiState ImmutableArray<_>.Empty
        let doubleColorless = CardInstanceId "vim-0"

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        MatchScenario.Command
                            state
                            "double-colorless-retreat"
                            MatchScenario.FirstPlayer
                            ImmutableArray<_>.Empty
                            (MatchAction.Taxi(incoming.Id, ImmutableArray.Create doubleColorless))
                    )
            )

        (applied.Card incoming.Id).Zone |> should equal CardZone.Oche
        (applied.Card doubleColorless).Zone |> should equal CardZone.EmptiesTray

    // Advanced Rulebook Version 1, p. 23: Energy cards are discarded one at a
    // time only until the Retreat Cost is met, so a sufficient Double Colorless
    // is chosen without also discarding a Basic Energy that happens to sort first.
    [<Test>]
    member _.``a generated Retreat should use Double Colorless without a redundant Basic Energy``
        ()
        =
        let state, incoming =
            taxiStateWithEnergy [ "VIM-SOBER"; "VIM-DODGY" ] ImmutableArray<_>.Empty

        let retreat =
            MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.map _.Command.Action
            |> Seq.find (function
                | MatchAction.Taxi(target, _) -> target = incoming.Id
                | _ -> false)

        match retreat with
        | MatchAction.Taxi(_, payment) ->
            payment |> Seq.toList |> should equal [ CardInstanceId "vim-1" ]
        | _ -> failwith "The generated action was not a Retreat."

    // Advanced Rulebook Version 1, pp. 4, 17: a failed Confused attack places
    // two damage counters on the attacker rather than executing the attack.
    [<Test>]
    member _.``a failed Confused attack should deal twenty self-damage and no attack damage``() =
        let state =
            MatchScenario.BattleStateWith
                "BLK-013"
                "BLK-001"
                [ "VIM-DODGY" ]
                (firstBlankSeed ())
                (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.Muddled 2))
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-013-B02")
            )

        (applied.Card(CardInstanceId "attacker")).Damage |> should equal 20
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 0

    // Advanced Rulebook Version 1, pp. 4, 17: a failed Confused retreat discards
    // no Energy and consumes the Pokemon's one retreat attempt for the turn.
    [<Test>]
    member _.``a failed Confused retreat should keep Energy attached and consume the retreat``() =
        let state, incoming =
            taxiState (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.Muddled 2))

        let command id current =
            MatchScenario.Command
                current
                id
                MatchScenario.FirstPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Taxi(incoming.Id, ImmutableArray.Create(CardInstanceId "vim-0")))

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, command "confused-retreat" state)
            )

        (applied.Card incoming.Id).Zone |> should equal CardZone.Booth
        (applied.Card(CardInstanceId "vim-0")).Zone |> should equal CardZone.Attached
        applied.RoundUsage.TaxisUsed |> should equal 1

        MatchScenario.Engine().Apply(applied, command "second-retreat" applied)
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.EffectUnavailable

    // Advanced Rulebook Version 1, pp. 3, 10, 13: Pokemon Powers work from
    // the Bench but not while their Pokemon is Asleep, Confused, or Paralyzed.
    [<Test>]
    member _.``confused should disable a Pokemon Power while Poisoned should not``() =
        let stateWith condition =
            let state =
                MatchScenario.BattleStateWith
                    "BLK-041"
                    "BLK-001"
                    []
                    903UL
                    (ImmutableArray.Create(MatchScenario.RoughState condition 2))
                    ImmutableArray<_>.Empty
                    ImmutableArray<_>.Empty

            let opponentHand =
                MatchScenario.PlainCard
                    "opponent-hand"
                    "VIM-SOBER"
                    MatchScenario.SecondPlayer
                    CardZone.Mitt
                    -1

            MatchScenario.WithCards state [ opponentHand ]

        usePowerActions (stateWith BlokemonRoughState.Muddled) |> should be Empty

        usePowerActions (stateWith BlokemonRoughState.DodgyPint)
        |> should
            contain
            (MatchAction.UsePartyTrick(CardInstanceId "attacker", EffectId "BLK-041-T01"))

    // Advanced Rulebook Version 1, pp. 9-10, 12: there is one Trainer category,
    // any number may be played, and each goes to the discard pile after its text.
    [<Test>]
    member _.``multiple vintage Trainers should resolve and discard in one turn``() =
        let original = MatchScenario.BattleState "BLK-013" "BLK-001" [] 904UL

        let trainers =
            [ MatchScenario.PlainCard
                  "trainer-1"
                  "KIT-031"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "trainer-2"
                  "KIT-032"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1 ]

        let first = MatchScenario.WithCards original trainers
        let engine = MatchScenario.Engine()

        let play current id card =
            MatchScenario.Applied(
                engine.Apply(
                    current,
                    MatchScenario.Command
                        current
                        id
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        (MatchAction.PlayKit(CardInstanceId card, ValueNone))
                )
            )

        let second = play first "trainer-one" "trainer-1"
        let final = play second "trainer-two" "trainer-2"

        (final.Card(CardInstanceId "trainer-1")).Zone
        |> should equal CardZone.EmptiesTray

        (final.Card(CardInstanceId "trainer-2")).Zone
        |> should equal CardZone.EmptiesTray

    // Advanced Rulebook Version 1, p. 24: equal simultaneous wins start an
    // entirely new game with one Prize, not a partial Prize-only reset.
    [<Test>]
    member _.``a tied win should reject incomplete reconstructed decks before redealing``() =
        let state = tiedWinWithoutBasics ()
        let requiredCards = MatchScenario.Authority.BaseRules.Stack.CardCount

        ownedCardCounts state
        |> Seq.forall (fun cardCount -> cardCount < requiredCards)
        |> should be True

        (fun () ->
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-002-B02")
            |> ignore)
        |> should throw typeof<InvalidOperationException>

    [<Test>]
    member _.``a tied win should reject sixty-card decks without a Basic Pokemon before redealing``
        ()
        =
        let incomplete = tiedWinWithoutBasics ()
        let requiredCards = MatchScenario.Authority.BaseRules.Stack.CardCount

        let fillers =
            [ for player in incomplete.Players do
                  let ownedCards =
                      incomplete.Cards
                      |> Seq.filter (fun card -> card.Owner = player.Id)
                      |> Seq.toArray

                  let nextPosition =
                      ownedCards
                      |> Seq.filter (fun card -> card.Zone = CardZone.Stack)
                      |> Seq.fold (fun next card -> max next (card.StackPosition + 1)) 0

                  for index in 0 .. requiredCards - ownedCards.Length - 1 do
                      yield
                          MatchScenario.PlainCard
                              $"non-basic-filler:{player.Id.Value}:{index}"
                              "VIM-SOBER"
                              player.Id
                              CardZone.Stack
                              (nextPosition + index) ]

        let state = MatchScenario.WithCards incomplete fillers

        ownedCardCounts state |> should equal [ requiredCards; requiredCards ]

        (fun () ->
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-002-B02")
            |> ignore)
        |> should throw typeof<InvalidOperationException>

    [<Test>]
    member _.``a tied win should restart every game zone and then set one Prize``() =
        let original =
            MatchScenario.BattleState "BLK-143" "BLK-013" [ "VIM-DODGY"; "VIM-DODGY" ] 905UL

        let attacker =
            { original.Card(CardInstanceId "attacker") with
                Damage = 120 }

        let defender =
            { original.Card(CardInstanceId "defender") with
                Damage = 40 }

        let firstPrize =
            MatchScenario.PlainCard
                "first-prize"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                0

        let secondPrize =
            MatchScenario.PlainCard
                "second-prize"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.BarChit
                0

        let cards = [ yield attacker; yield defender; yield firstPrize; yield secondPrize ]

        let stateWithCards =
            MatchScenario.WithCards original cards |> MatchScenario.WithRestartableDecks

        let stateWithFirstPrize =
            MatchScenario.WithBarChits stateWithCards MatchScenario.FirstPlayer 1

        let state =
            MatchScenario.WithBarChits stateWithFirstPrize MatchScenario.SecondPlayer 1

        let restarted, events =
            MatchScenario.AppliedWith(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-143-B01")
            )

        restarted.Phase |> should equal MatchPhase.OpeningPlacement
        restarted.SuddenDeathCount |> should equal 1
        restarted.Winner.IsNone |> should be True
        restarted.Cards |> Seq.forall (fun card -> card.Damage = 0) |> should be True

        restarted.Cards
        |> Seq.forall (fun card -> card.RoughStates.IsEmpty)
        |> should be True

        restarted.Cards
        |> Seq.exists (fun card -> card.Zone = CardZone.EmptiesTray)
        |> should be False

        restarted.CardsIn(MatchScenario.FirstPlayer, CardZone.Mitt)
        |> Seq.length
        |> should equal 7

        restarted.CardsIn(MatchScenario.SecondPlayer, CardZone.Mitt)
        |> Seq.length
        |> should equal 7

        events
        |> Seq.exists (fun event -> event.Kind = MatchEventKind.SuddenDeathStarted)
        |> should be True

        let chooseOpening (current: MatchState) (actor: PlayerId) id =
            let active =
                current.CardsIn(actor, CardZone.Mitt)
                |> Seq.find (fun card -> card.Kind = CardKind.Bloke)

            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        current,
                        MatchScenario.Command
                            current
                            id
                            actor
                            ImmutableArray<_>.Empty
                            (MatchAction.ChooseOpening(active.Id, ImmutableArray<_>.Empty))
                    )
            )

        let firstPlaced =
            chooseOpening restarted MatchScenario.FirstPlayer "sudden-opening-first"

        let playing =
            chooseOpening firstPlaced MatchScenario.SecondPlayer "sudden-opening-second"

        (playing.Player MatchScenario.FirstPlayer).BarChitsRemaining |> should equal 1
        (playing.Player MatchScenario.SecondPlayer).BarChitsRemaining |> should equal 1

        playing.CardsIn(MatchScenario.FirstPlayer, CardZone.BarChit)
        |> Seq.length
        |> should equal 1

        playing.CardsIn(MatchScenario.SecondPlayer, CardZone.BarChit)
        |> Seq.length
        |> should equal 1

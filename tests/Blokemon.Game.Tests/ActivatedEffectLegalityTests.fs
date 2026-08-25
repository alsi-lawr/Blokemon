namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

/// An activated party trick is only a move while activating it could change something. Two things
/// empty one out: the conditions guarding the branch that does the work - Howard Marks only heals
/// from the Oche - and the work having nothing left to do it to - Big Dave only takes cards back
/// out of an empties tray that has them. Every case here is posed from the printed authority.
type ActivatedEffectLegalityTests() =

    let Howard = "BLK-003" // heals 60, from the Oche only
    let Balaclava = "BLK-041" // shows the other mitt, from the Oche only
    let GolfClubGary = "BLK-053" // finds a Mate kit in the deck
    let TheLads = "BLK-085" // draws one and takes a counter
    let Marathon = "BLK-121" // places two counters and chucks itself
    let Steve = "BLK-132" // becomes another Regular, on its owner's first round only
    let BigDave = "BLK-143" // takes KIT-012 back out of the empties tray
    let Bez = "BLK-151" // draws the mitt back up to three

    let Mate = "KIT-010" // the Mate kit Golf Club Gary looks for
    let BarKit = "KIT-012" // the Bar Kit Big Dave takes back
    let RingRoad = "KIT-006"
    let RingRoadTrade = "KIT-006-R01"

    let First = MatchScenario.FirstPlayer

    /// Every party trick and local house rule the table would offer the first player.
    let Offered (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, First)
        |> Seq.choose (fun action ->
            match action.Command.Action with
            | MatchAction.UsePartyTrick(_, effect) -> Some effect.Value
            | _ -> None)
        |> Seq.toList

    let OfferedActions (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, First)
        |> Seq.filter (fun action -> action.Kind = LegalActionKind.UsePartyTrick)
        |> Seq.toList

    /// The table itself, without any of the bookkeeping a command always moves: the revision, the
    /// random stream, the commands recorded and - the point of the whole exercise - the record of
    /// which effects the round has already spent.
    let Table (state: MatchState) =
        [ for card in state.Cards ->
              card.Id.Value,
              card.Zone,
              card.Damage,
              card.StackPosition,
              [ for attachment in card.Attachments -> attachment.Value ],
              [ for rough in card.RoughStates -> rough.State ] ],
        [ for player in state.Players -> player.Id.Value, player.BarChitsRemaining ],
        [ for effect in state.Effects -> effect.Kind ]

    /// A table posed for the first player: their own bloke at the Oche, one opponent facing it.
    let Posed (own: string) (opponent: string) =
        MatchScenario.BattleState own opponent [] 17UL

    /// Howard Marks and one team-mate, with Howard at the Oche or on the bench and the other one
    /// wherever Howard is not.
    let HowardTable (howardZone: CardZone) (mateDamage: int) =
        let state = Posed Howard "BLK-001"

        let mateZone =
            if howardZone = CardZone.Oche then
                CardZone.Booth
            else
                CardZone.Oche

        let mate = MatchScenario.PlainCard "mate" "BLK-001" First mateZone -1

        MatchScenario.WithCards
            state
            [ { state.Card(CardInstanceId "attacker") with
                  Zone = howardZone }
              { mate with Damage = mateDamage } ]

    let WithFirstMitt (state: MatchState) (mechanicalIds: string list) =
        MatchScenario.WithCards
            state
            [ for index, mechanicalId in List.indexed mechanicalIds ->
                  MatchScenario.PlainCard $"mitt-{index}" mechanicalId First CardZone.Mitt -1 ]

    let WithOtherMitt (state: MatchState) (mechanicalIds: string list) =
        MatchScenario.WithCards
            state
            [ for index, mechanicalId in List.indexed mechanicalIds ->
                  MatchScenario.PlainCard
                      $"other-mitt-{index}"
                      mechanicalId
                      MatchScenario.SecondPlayer
                      CardZone.Mitt
                      -1 ]

    let WithFirstZone (state: MatchState) (zone: CardZone) (mechanicalIds: string list) =
        MatchScenario.WithCards
            state
            [ for index, mechanicalId in List.indexed mechanicalIds ->
                  MatchScenario.PlainCard $"{zone}-{index}" mechanicalId First zone index ]

    let WithRoundsStarted (state: MatchState) (rounds: int) =
        { state with
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun player ->
                        if player.Id = First then
                            { player with RoundsStarted = rounds }
                        else
                            player)
                ) }

    let WithEffectUsed (state: MatchState) (effect: string) =
        { state with
            RoundUsage =
                { state.RoundUsage with
                    EffectsUsed = ImmutableArray.Create(EffectId effect) } }

    let RingRoadTable (withBasicVim: bool) =
        let state = Posed "BLK-001" "BLK-001"

        let cards =
            [ MatchScenario.PlainCard "ring-road" RingRoad First CardZone.Local -1

              if withBasicVim then
                  MatchScenario.PlainCard "ring-road-vim" "VIM-BLAZED" First CardZone.Mitt -1 ]

        MatchScenario.WithCards state cards

    let RingRoadActions (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, First)
        |> Seq.filter (fun action ->
            match action.Command.Action with
            | MatchAction.UsePartyTrick(source, effect) ->
                source = CardInstanceId "ring-road" && effect = EffectId RingRoadTrade
            | _ -> false)
        |> Seq.toList

    /// Doing something means the table moved, or hidden cards were shown: a reveal is information
    /// rather than table state, and Balaclava's trick is nothing but a reveal.
    let ChangedSomething (before: MatchState) (after: MatchState) (events: MatchEvent seq) =
        Table after <> Table before
        || events |> Seq.exists (fun event -> event.Kind = MatchEventKind.CardsRevealed)

    /// The fullest answer each question admits, so what is measured is the trick rather than a
    /// thin answer to it.
    let FullAnswers (requirements: ChoiceRequirement seq) =
        ImmutableArray.CreateRange(
            requirements
            |> Seq.collect (fun requirement ->
                match requirement.Kind with
                | ChoiceRequirementKind.Optional ->
                    Seq.singleton (EffectChoice.Optional(requirement.Id, true))
                | ChoiceRequirementKind.Amount ->
                    Seq.singleton (EffectChoice.Amount(requirement.Id, requirement.Maximum))
                | ChoiceRequirementKind.Cards ->
                    Seq.singleton (
                        EffectChoice.Cards(
                            requirement.Id,
                            ImmutableArray.CreateRange(
                                requirement.EligibleCards |> Seq.truncate requirement.Maximum
                            )
                        )
                    )
                | ChoiceRequirementKind.MechanicalType ->
                    requirement.EligibleMechanicalTypes
                    |> Seq.truncate 1
                    |> Seq.map (fun value -> EffectChoice.MechanicalType(requirement.Id, value))
                | ChoiceRequirementKind.Attack ->
                    requirement.EligibleEffects
                    |> Seq.truncate 1
                    |> Seq.map (fun value -> EffectChoice.Attack(requirement.Id, value))
                | ChoiceRequirementKind.Distribution ->
                    requirement.EligibleCards
                    |> Seq.truncate 1
                    |> Seq.map (fun card ->
                        EffectChoice.Distribution(
                            requirement.Id,
                            ImmutableArray.Create
                                { Card = card
                                  Counters = requirement.Maximum }
                        ))
                | _ -> Seq.empty)
        )

    /// Activating a trick asks its questions after the fact: answering the optional opens the
    /// branch, and whatever that branch needs is asked next. Every round is answered until the
    /// match stops asking.
    let Activate (state: MatchState) (action: LegalAction) =
        let engine = MatchScenario.Engine()
        let events = ResizeArray<MatchEvent>()
        let started, first = MatchScenario.AppliedWith(engine.Apply(state, action.Command))
        events.AddRange first
        let mutable applied = started
        let mutable rounds = 0

        while applied.PendingEffect.IsSome && rounds < 4 do
            let answers = FullAnswers applied.PendingEffect.Value.Requirements

            let resolved, further =
                MatchScenario.AppliedWith(
                    engine.Apply(applied, MatchScenario.ResolveEffectChoiceCommand applied answers)
                )

            applied <- resolved
            events.AddRange further
            rounds <- rounds + 1

        applied, events

    [<Test>]
    member _.``a heal that only works from the Oche should not be offered from the bench although a team-mate is damaged``
        ()
        =
        // The reported case exactly: Howard Marks benched, a damaged bloke of the player's own on
        // the table, the trick offered and doing nothing at all when it was taken.
        Offered(HowardTable CardZone.Booth 30) |> should not' (contain "BLK-003-T01")

    [<Test>]
    member _.``a heal should be offered at the Oche while a team-mate is damaged and should heal when it is taken``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = HowardTable CardZone.Oche 30
        Offered state |> should contain "BLK-003-T01"

        let action =
            OfferedActions state
            |> List.filter (fun action ->
                match action.Command.Action with
                | MatchAction.UsePartyTrick(_, effect) -> effect.Value = "BLK-003-T01"
                | _ -> false)
            |> List.exactlyOne

        // Taking the trick opens its branch and asks which bloke is being healed; the damaged one
        // is the answer, and the heal has to land on it.
        let asked = MatchScenario.Applied(engine.Apply(state, action.Command))
        let requirement = asked.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let healed =
            MatchScenario.Applied(
                engine.Apply(
                    asked,
                    MatchScenario.ResolveEffectChoiceCommand
                        asked
                        (ImmutableArray.Create(
                            EffectChoice.Cards(
                                requirement.Id,
                                ImmutableArray.Create(CardInstanceId "mate")
                            )
                        ))
                )
            )

        (healed.Card(CardInstanceId "mate")).Damage |> should equal 0

    [<Test>]
    member _.``a heal should not be offered at the Oche while nothing of the player's own is damaged``
        ()
        =
        // The conditions pass here: what is missing is anything for the heal to work on, which is
        // the second half of the gate rather than the first.
        Offered(HowardTable CardZone.Oche 0) |> should not' (contain "BLK-003-T01")

    [<Test>]
    member _.``the Ring Road should reject its trade when no Basic Vim can be discarded``() =
        let engine = MatchScenario.Engine()
        let payable = RingRoadTable true
        let unpayable = RingRoadTable false
        let payableCommand = (RingRoadActions payable |> List.exactlyOne).Command

        RingRoadActions unpayable |> should be Empty

        let unchanged, rejection =
            MatchScenario.Rejected(engine.Apply(unpayable, payableCommand))

        rejection.Code |> should equal CommandRejectionCode.EffectUnavailable
        unchanged |> should equal unpayable

    [<Test>]
    member _.``the Ring Road should discard the chosen Basic Vim before drawing one card``() =
        let engine = MatchScenario.Engine()
        let state = RingRoadTable true
        let action = RingRoadActions state |> List.exactlyOne
        let pending = MatchScenario.Applied(engine.Apply(state, action.Command))
        let requirement = pending.PendingEffect.Value.Requirements |> Seq.exactlyOne
        let stackBefore = state.CardsIn(First, CardZone.Stack) |> Seq.length
        let mittBefore = state.CardsIn(First, CardZone.Mitt) |> Seq.length
        let emptiesBefore = state.CardsIn(First, CardZone.EmptiesTray) |> Seq.length

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    pending,
                    MatchScenario.ResolveEffectChoiceCommand
                        pending
                        (ImmutableArray.Create(
                            EffectChoice.Cards(
                                requirement.Id,
                                ImmutableArray.Create(CardInstanceId "ring-road-vim")
                            )
                        ))
                )
            )

        (resolved.Card(CardInstanceId "ring-road-vim")).Zone
        |> should equal CardZone.EmptiesTray

        (resolved.Card(CardInstanceId "first-draw")).Zone |> should equal CardZone.Mitt

        (resolved.CardsIn(First, CardZone.Stack) |> Seq.length)
        |> should equal (stackBefore - 1)

        (resolved.CardsIn(First, CardZone.Mitt) |> Seq.length)
        |> should equal mittBefore

        (resolved.CardsIn(First, CardZone.EmptiesTray) |> Seq.length)
        |> should equal (emptiesBefore + 1)

        resolved.RoundUsage.EffectsUsed |> should contain (EffectId RingRoadTrade)

    [<Test>]
    member _.``taking cards back out of the empties tray should not be offered while the tray holds none of them``
        ()
        =
        // The other reported case: Big Dave offered although the empties tray held nothing he
        // recovers. The tray is empty here, and a Bar Kit in the mitt is not in the tray.
        let state = WithFirstMitt (Posed BigDave "BLK-001") [ BarKit ]

        Offered state |> should not' (contain "BLK-143-T01")

    [<Test>]
    member _.``taking cards back out of the empties tray should be offered while the tray holds one``
        ()
        =
        let state = WithFirstZone (Posed BigDave "BLK-001") CardZone.EmptiesTray [ BarKit ]

        Offered state |> should contain "BLK-143-T01"

    [<Test>]
    member _.``showing the other mitt should not be offered while that mitt is empty``() =
        // A second trick guarded on standing at the Oche, and the only censused body whose whole
        // work is information: with nothing held there is nothing to show.
        let state = Posed Balaclava "BLK-001"
        Offered state |> should not' (contain "BLK-041-T01")

        Offered(WithOtherMitt state [ BarKit ]) |> should contain "BLK-041-T01"

    [<Test>]
    member _.``finding a Mate kit in the deck should not be offered while the deck holds none``() =
        // The deck here holds four cards and none of them is a Mate kit, so the search finds
        // nothing and the reveal and the move both run on that same empty selection. The shuffle
        // in the middle would still reorder four cards, and is still not a reason to activate a
        // search that finds nothing.
        let state =
            WithFirstZone
                (Posed GolfClubGary "BLK-001")
                CardZone.Stack
                [ BarKit; "BLK-001"; "BLK-004" ]

        Offered state |> should not' (contain "BLK-053-T01")

        Offered(WithFirstZone state CardZone.Stack [ Mate ])
        |> should contain "BLK-053-T01"

    [<Test>]
    member _.``drawing the mitt back up to three should not be offered while it already holds three``
        ()
        =
        // The draw counts what the mitt still needs, so a full mitt makes it a draw of nothing.
        let state = Posed Bez "BLK-001"

        Offered(WithFirstMitt state [ BarKit; BarKit; BarKit ])
        |> should not' (contain "BLK-151-T01")

        Offered(WithFirstMitt state [ BarKit ]) |> should contain "BLK-151-T01"

    [<Test>]
    member _.``a trick already used this round should not be offered again``() =
        let state = HowardTable CardZone.Oche 30
        Offered state |> should contain "BLK-003-T01"

        Offered(WithEffectUsed state "BLK-003-T01")
        |> should not' (contain "BLK-003-T01")

    [<Test>]
    member _.``a trick gated on its owner's first round should not be offered after it``() =
        // Not every condition is about where the card is standing: this one is about when.
        let state = WithFirstZone (Posed Steve "BLK-001") CardZone.Stack [ "BLK-001" ]

        Offered(WithRoundsStarted state 2) |> should not' (contain "BLK-132-T01")

        Offered(WithRoundsStarted state 1) |> should contain "BLK-132-T01"

    [<Test>]
    member _.``every trick the table offers should change the table when it is activated``() =
        // One posed table per censused body, each one a table the trick can work on. Whatever is
        // offered there is taken with the answers the engine itself proposes, and every one of
        // them has to leave the table different from how it found it.
        let healTable =
            let posed = HowardTable CardZone.Oche 30

            MatchScenario.WithCards
                posed
                [ { posed.Card(CardInstanceId "attacker") with
                      Damage = 30 } ]

        let steveTable = WithFirstZone (Posed Steve "BLK-001") CardZone.Stack [ "BLK-001" ]

        let tables =
            [ "heal", healTable
              "recover", WithFirstZone (Posed BigDave "BLK-001") CardZone.EmptiesTray [ BarKit ]
              "reveal", WithOtherMitt (Posed Balaclava "BLK-001") [ BarKit ]
              "search", WithFirstZone (Posed GolfClubGary "BLK-001") CardZone.Stack [ Mate ]
              "draw", WithFirstMitt (Posed TheLads "BLK-001") []
              "counters", Posed Marathon "BLK-001"
              "transform", WithRoundsStarted steveTable 1
              "draw-to-three", WithFirstMitt (Posed Bez "BLK-001") [ BarKit ] ]

        let outcomes =
            [ for name, state in tables do
                  for action in OfferedActions state do
                      let effect =
                          match action.Command.Action with
                          | MatchAction.UsePartyTrick(_, effect) -> effect.Value
                          | _ -> "?"

                      let applied, events = Activate state action
                      $"{name}:{effect}", ChangedSomething state applied events ]

        // The set is only a proof while it is not empty: an offer that never happens proves
        // nothing about what an offer costs.
        outcomes |> List.isEmpty |> should be False

        outcomes |> List.filter (fun (_, changed) -> not changed) |> should be Empty

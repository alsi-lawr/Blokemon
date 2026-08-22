namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Game
open FsUnit
open TUnit.Core

type CardFlowTests() =

    // A Booth is in the order its Blokes were put down, and the identities here are chosen to sort
    // the other way: a Booth that answers by identity answers in the order the Deck was written,
    // because a card's identity is its position in the deck list it was built from.
    [<Test>]
    member _.``a booth should stand its blokes in the order they were played``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-003" [] 907UL

        let held =
            [ "zz-bloke"; "aa-bloke"; "mm-bloke" ]
            |> List.map (fun id ->
                MatchScenario.PlainCard id "BLK-004" MatchScenario.FirstPlayer CardZone.Mitt -1)

        let mutable state = MatchScenario.WithCards state held

        for card in held do
            state <-
                MatchScenario.Applied(
                    engine.Apply(
                        state,
                        MatchScenario.Command
                            state
                            $"play:{card.Id.Value}"
                            MatchScenario.FirstPlayer
                            ImmutableArray<_>.Empty
                            (MatchAction.PlayBloke card.Id)
                    )
                )

        state.CardsIn(MatchScenario.FirstPlayer, CardZone.Booth)
        |> Seq.map (fun card -> card.Id.Value)
        |> Seq.toList
        |> should equal [ "zz-bloke"; "aa-bloke"; "mm-bloke" ]

    // Promoting is not putting a Bloke down. It is the same pile of cards grown taller, so it
    // happens where the pile already stood and the Blokes either side of it do not move.
    [<Test>]
    member _.``a promotion should take the place of the bloke it promoted``() =
        let engine = MatchScenario.Engine()

        let standing =
            [ "yy-standing", 0; "bb-standing", 1; "nn-standing", 2 ]
            |> List.map (fun (id, position) ->
                MatchScenario.PlainCard
                    id
                    "BLK-004"
                    MatchScenario.FirstPlayer
                    CardZone.Booth
                    position)

        let promotion =
            MatchScenario.PlainCard
                "pp-promotion"
                "BLK-005"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-001" "BLK-003" [] 907UL)
                (promotion :: standing)

        let action =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.filter (fun candidate ->
                candidate.Kind = LegalActionKind.Promote
                && (match candidate.Command.Action with
                    | MatchAction.Promote(promoted, target) ->
                        promoted = promotion.Id && target = CardInstanceId "bb-standing"
                    | _ -> false))
            |> Seq.exactlyOne

        let applied = MatchScenario.Applied(engine.Apply(state, action.Command))

        applied.CardsIn(MatchScenario.FirstPlayer, CardZone.Booth)
        |> Seq.map (fun card -> card.Id.Value)
        |> Seq.toList
        |> should equal [ "yy-standing"; "pp-promotion"; "nn-standing" ]

    [<Test>]
    member _.``a stack search should offer only regular blokes and move the chosen card to the booth``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-016" "BLK-003" [ "VIM-SOBER" ] 307UL

        let basic =
            MatchScenario.PlainCard
                "search-basic"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Stack
                0

        let evolved =
            MatchScenario.PlainCard
                "search-evolved"
                "BLK-003"
                MatchScenario.FirstPlayer
                CardZone.Stack
                1

        let state =
            { state with
                Cards =
                    ImmutableArray.CreateRange(
                        state.Cards
                        |> Seq.filter (fun card -> card.Id.Value <> "first-draw")
                        |> fun cards -> Seq.append cards [ basic; evolved ]
                        |> Seq.sortBy (fun card -> card.Id)
                    ) }

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-016-B01")
            )

        let optional =
            missing.ChoiceRequirements
            |> Seq.find (fun value -> value.Kind = ChoiceRequirementKind.Optional)

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    MatchScenario.AttackCommandWith
                        state
                        "BLK-016-B01"
                        (ImmutableArray.Create(EffectChoice.Optional(optional.Id, true)))
                )
            )

        let requirement =
            requested.PendingEffect.Value.Requirements
            |> Seq.find (fun value -> value.Kind = ChoiceRequirementKind.Cards)

        let command =
            MatchScenario.ResolveEffectChoiceCommand
                requested
                (ImmutableArray.Create(
                    EffectChoice.Cards(requirement.Id, ImmutableArray.Create basic.Id)
                ))

        let applied = MatchScenario.Applied(engine.Apply(requested, command))

        missing.ChoiceRequirements.Length |> should equal 1
        requirement.EligibleCards |> Seq.toList |> should equal [ basic.Id ]
        (applied.Card basic.Id).Zone |> should equal CardZone.Booth
        (applied.Card evolved.Id).Zone |> should equal CardZone.Stack

    [<Test>]
    member _.``a mate should move and attach using the owners, sources and destinations it names``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-003" [] 311UL

        let kit =
            MatchScenario.PlainCard "supporter" "KIT-010" MatchScenario.FirstPlayer CardZone.Mitt -1

        let ownVim =
            MatchScenario.PlainCard "own-vim" "VIM-SOBER" MatchScenario.FirstPlayer CardZone.Mitt -1

        let otherVim =
            MatchScenario.AttachedCard
                "other-vim"
                "VIM-BLAZED"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.Create otherVim.Id }

        let state = MatchScenario.WithCards state [ defender; kit; ownVim; otherVim ]

        let actions = engine.GetLegalActions(state, MatchScenario.FirstPlayer)

        let isThisKit (action: LegalAction) =
            action.Kind = LegalActionKind.PlayKit
            && (match action.Command.Action with
                | MatchAction.PlayKit(played, _) -> played = kit.Id
                | _ -> false)

        actions |> Seq.exists isThisKit |> should be True
        let action = actions |> Seq.filter isThisKit |> Seq.exactlyOne

        let requested = MatchScenario.Applied(engine.Apply(state, action.Command))
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (ImmutableArray.Create(
                            EffectChoice.Cards(requirement.Id, ImmutableArray.Create ownVim.Id)
                        ))
                )
            )

        let returnedVim = applied.Card otherVim.Id

        returnedVim.Zone |> should equal CardZone.Mitt
        returnedVim.Owner |> should equal MatchScenario.SecondPlayer
        returnedVim.AttachedTo.IsNone |> should be True

        (applied.Card defender.Id).Attachments
        |> Seq.contains otherVim.Id
        |> should be False

        (applied.Card ownVim.Id).Zone |> should equal CardZone.Attached

        (applied.Card ownVim.Id).AttachedTo
        |> should equal (ValueSome(CardInstanceId "attacker"))

        (applied.Card kit.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``get the lads in with booth room should move one regular and shuffle the remaining stack``
        ()
        =
        let booth =
            [ for index in 0..3 ->
                  MatchScenario.PlainCard
                      $"booth-{index}"
                      "BLK-004"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      index ]

        let ineligibleVim =
            MatchScenario.PlainCard
                "first-draw"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Stack
                0

        let firstRegular =
            MatchScenario.PlainCard
                "searched-regular-a"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Stack
                2

        let evolved =
            MatchScenario.PlainCard
                "searched-evolved"
                "BLK-003"
                MatchScenario.FirstPlayer
                CardZone.Stack
                4

        let remainingRegulars =
            [ MatchScenario.PlainCard
                  "searched-regular-b"
                  "BLK-007"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  6
              MatchScenario.PlainCard
                  "searched-regular-c"
                  "BLK-025"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  8 ]

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-128" "BLK-003" [ "VIM-SOBER" ] 929UL)
                (Seq.concat [ booth; [ ineligibleVim; firstRegular; evolved ]; remainingRegulars ])

        let command = MatchScenario.AttackCommand state "BLK-128-B01"

        let applied, events =
            MatchScenario.AppliedWith(MatchScenario.Engine().Apply(state, command))

        let repeatedState, repeatedEvents =
            MatchScenario.AppliedWith(MatchScenario.Engine().Apply(state, command))

        applied.PendingEffect.IsNone |> should be True

        events
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.EffectChoiceRequested)
        |> should be False

        applied.CardsIn(MatchScenario.FirstPlayer, CardZone.Booth)
        |> Seq.length
        |> should equal 5

        (applied.Card firstRegular.Id).Zone |> should equal CardZone.Booth

        remainingRegulars
        |> List.map (fun card -> (applied.Card card.Id).Zone)
        |> should equal [ CardZone.Stack; CardZone.Stack ]

        (applied.Card evolved.Id).Zone |> should equal CardZone.Stack
        (applied.Card ineligibleVim.Id).Zone |> should equal CardZone.Stack

        applied.CardsIn(MatchScenario.FirstPlayer, CardZone.Stack)
        |> Seq.map (fun card -> card.StackPosition)
        |> Seq.sort
        |> Seq.toList
        |> should equal [ 0; 1; 2; 3 ]

        events
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.CardsShuffled)
        |> Seq.length
        |> should equal 1

        applied.ActivePlayer |> should equal MatchScenario.SecondPlayer
        repeatedState |> should equal applied
        repeatedEvents |> should equal events

    [<Test>]
    member _.``get the lads in with a full booth should end the round without searching or shuffling``
        ()
        =
        let booth =
            [ for index in 0..4 ->
                  MatchScenario.PlainCard
                      $"full-booth-{index}"
                      "BLK-004"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      index ]

        let stack =
            [ MatchScenario.PlainCard
                  "first-draw"
                  "BLK-004"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  3
              MatchScenario.PlainCard
                  "full-search-regular"
                  "BLK-007"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  9
              MatchScenario.PlainCard
                  "full-search-ineligible"
                  "BLK-003"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  17 ]

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-128" "BLK-003" [ "VIM-SOBER" ] 937UL)
                (Seq.append booth stack)

        let command = MatchScenario.AttackCommand state "BLK-128-B01"

        let applied, events =
            MatchScenario.AppliedWith(MatchScenario.Engine().Apply(state, command))

        let repeatedState, repeatedEvents =
            MatchScenario.AppliedWith(MatchScenario.Engine().Apply(state, command))

        applied.PendingEffect.IsNone |> should be True
        applied.ActivePlayer |> should equal MatchScenario.SecondPlayer

        events
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.AttackDeclared)
        |> Seq.length
        |> should equal 1

        events
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.RoundEnded)
        |> Seq.length
        |> should equal 1

        events
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.CardsShuffled)
        |> should be False

        let stackIds = stack |> List.map (fun card -> card.Id)

        events
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.CardMoved)
        |> Seq.collect (fun matchEvent -> matchEvent.TargetCards)
        |> Seq.exists (fun card -> List.contains card stackIds)
        |> should be False

        booth
        |> List.map (fun card ->
            let current = applied.Card card.Id
            current.Zone, current.StackPosition)
        |> should equal (booth |> List.map (fun card -> card.Zone, card.StackPosition))

        stack
        |> List.map (fun card ->
            let current = applied.Card card.Id
            current.Zone, current.StackPosition)
        |> should equal (stack |> List.map (fun card -> card.Zone, card.StackPosition))

        applied.Random |> should equal state.Random
        repeatedState |> should equal applied
        repeatedEvents |> should equal events

    [<Test>]
    member _.``pintman should attach exactly one searched beer vim and shuffle the remaining stack``
        ()
        =
        let firstBeer =
            MatchScenario.PlainCard
                "searched-beer-a"
                "VIM-BEER"
                MatchScenario.FirstPlayer
                CardZone.Stack
                0

        let secondBeer =
            MatchScenario.PlainCard
                "searched-beer-b"
                "VIM-BEER"
                MatchScenario.FirstPlayer
                CardZone.Stack
                1

        let sober =
            MatchScenario.PlainCard
                "first-draw"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Stack
                2

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-025" "BLK-003" [ "VIM-BEER" ] 941UL)
                [ firstBeer; secondBeer; sober ]

        let command = MatchScenario.AttackCommand state "BLK-025-B01"

        let applied, events =
            MatchScenario.AppliedWith(MatchScenario.Engine().Apply(state, command))

        let repeatedState, repeatedEvents =
            MatchScenario.AppliedWith(MatchScenario.Engine().Apply(state, command))

        (applied.Card firstBeer.Id).Zone |> should equal CardZone.Attached

        (applied.Card firstBeer.Id).AttachedTo
        |> should equal (ValueSome(CardInstanceId "attacker"))

        (applied.Card secondBeer.Id).Zone |> should equal CardZone.Stack
        (applied.Card sober.Id).Zone |> should equal CardZone.Stack

        (applied.Card(CardInstanceId "attacker")).Attachments
        |> Seq.contains firstBeer.Id
        |> should be True

        (applied.Card(CardInstanceId "attacker")).Attachments.Length |> should equal 2

        events
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.CardsShuffled)
        |> Seq.length
        |> should equal 1

        applied.CardsIn(MatchScenario.FirstPlayer, CardZone.Stack)
        |> Seq.map (fun card -> card.StackPosition)
        |> Seq.sort
        |> Seq.toList
        |> should equal [ 0; 1 ]

        repeatedState |> should equal applied
        repeatedEvents |> should equal events

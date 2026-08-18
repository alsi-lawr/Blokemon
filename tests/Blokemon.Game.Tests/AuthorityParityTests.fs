namespace Blokemon.Game.Tests

open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private AuthorityParityFixtures =

    let addCards (state: MatchState) (added: CardState seq) =
        { state with
            Cards =
                FrozenList<CardState>
                    .Create(Seq.append state.Cards added |> Seq.sortBy (fun card -> card.Id)) }

    let seedForBadge () =
        let rec search (seed: uint64) =
            if seed >= 100UL then
                failwith "No badge-side seed was found."
            elif BlokemonSeededRandom(seed).NextInt 2 = 1 then
                seed
            else
                search (seed + 1UL)

        search 0UL

    let endRound (state: MatchState) (id: string) (actor: PlayerId) =
        MatchScenario.Command state id actor FrozenList.empty MatchAction.EndRound

    let attackAction (engine: MatchEngine) (state: MatchState) (actor: PlayerId) (effect: string) =
        engine.GetLegalActions(state, actor)
        |> Seq.filter (fun candidate ->
            candidate.Kind = LegalActionKind.Attack
            && (match candidate.Command.Action with
                | MatchAction.Attack(_, attackId) -> attackId = EffectId effect
                | _ -> false))
        |> Seq.exactlyOne

    let playKitAction (engine: MatchEngine) (state: MatchState) (kit: CardInstanceId) =
        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action ->
            action.Kind = LegalActionKind.PlayKit
            && (match action.Command.Action with
                | MatchAction.PlayKit(played, _) -> played = kit
                | _ -> false))
        |> Seq.exactlyOne

    let counterKnockoutState (defenderId: string) (includeDonni: bool) =
        let state =
            MatchScenario.BattleState "BLK-122" defenderId [ "VIM-SOBER" ] (seedForBadge ())

        let knockedOutBeer =
            MatchScenario.AttachedCard
                "knocked-out-beer"
                "VIM-BEER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let defender =
            { state.Card(CardInstanceId "defender") with
                Damage = 80
                Attachments = FrozenList<CardInstanceId>.Create knockedOutBeer.Id }

        let donni =
            if includeDonni then
                [ MatchScenario.PlainCard
                      "donni"
                      "BLK-026"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
            else
                []

        MatchScenario.WithCards state (defender :: knockedOutBeer :: donni)

    let counterAttack (engine: MatchEngine) (state: MatchState) =
        (attackAction engine state MatchScenario.FirstPlayer "BLK-122-B01").Command

    let fareAbilityState (attachedVim: string seq) =
        addCards
            (MatchScenario.BattleState "BLK-144" "BLK-150" attachedVim 757UL)
            [ MatchScenario.PlainCard
                  "own-booth"
                  "BLK-004"
                  MatchScenario.FirstPlayer
                  CardZone.Booth
                  -1
              MatchScenario.PlainCard
                  "mitt-vim"
                  "VIM-SOBER"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1 ]

    let ownBoothTaxi (actions: LegalAction seq) =
        actions
        |> Seq.filter (fun action ->
            action.Kind = LegalActionKind.Taxi
            && (match action.Command.Action with
                | MatchAction.Taxi(boothBloke, _) -> boothBloke = CardInstanceId "own-booth"
                | _ -> false))
        |> Seq.exactlyOne
        |> fun action ->
            match action.Command.Action with
            | MatchAction.Taxi(_, vimToChuck) -> vimToChuck
            | _ -> failwith "Expected a taxi action."

    /// The manifest with KIT-001's first house rule replaced by a program that would return a Kit
    /// from the Empties Tray to the Stack, which the printed discard-recovery lock must block.
    let authorityWithItemDiscardRecovery () =
        let item =
            MatchScenario.Authority.Kits |> Array.find (fun card -> card.Id = "KIT-001")

        let recovery =
            { Opcode = BlokemonOpcode.MoveCards
              Amount = 1
              ValueSource = BlokemonValueSource.Fixed
              Targets = [| BlokemonTarget.OwnEmptiesTray |]
              Selection = BlokemonSelection.Chosen
              TargetCount = 1
              Predicates = Array.empty
              MechanicalTypes = Array.empty
              RoughStates = Array.empty
              RelatedIds = Array.empty
              Then = Array.empty
              Otherwise = Array.empty
              Sources = [| BlokemonTarget.OwnEmptiesTray |]
              Destination = BlokemonEffectDestination.OwnStack
              CardFilter =
                { Categories = [| BlokemonCardCategory.Kit |]
                  Ranks = Array.empty
                  KitKinds = Array.empty
                  BasicVimOnly = false
                  DifferentMechanicalTypes = false
                  ExcludedRelatedIds = Array.empty }
              SourceTopCount = 0 }

        let changed =
            { item with
                HouseRules =
                    [| { item.HouseRules[0] with
                           Program = [| recovery |] }
                       item.HouseRules[1] |] }

        { MatchScenario.Authority with
            Kits =
                MatchScenario.Authority.Kits
                |> Array.map (fun card -> if card.Id = changed.Id then changed else card) }

type AuthorityParityTests() =

    [<Test>]
    member _.``the second opener should be able to use its promotion ability in its first round``
        ()
        =
        let promotion =
            MatchScenario.PlainCard "promotion" "BLK-022" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state = MatchScenario.BattleState "BLK-021" "BLK-150" [] 701UL

        let state =
            { state with
                Players =
                    FrozenList<PlayerState>
                        .Create(
                            state.Players
                            |> Seq.map (fun player ->
                                if player.Id = MatchScenario.FirstPlayer then
                                    { player with RoundsStarted = 1 }
                                else
                                    player)
                        ) }

        let state = addCards state [ promotion ]
        let engine = MatchScenario.Engine()

        let action =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.filter (fun candidate ->
                candidate.Kind = LegalActionKind.Promote
                && (match candidate.Command.Action with
                    | MatchAction.Promote(promotionCard, promoted) ->
                        promotionCard = promotion.Id && promoted = CardInstanceId "attacker"
                    | _ -> false))
            |> Seq.exactlyOne

        let applied = MatchScenario.Applied(engine.Apply(state, action.Command))

        (applied.Card promotion.Id).Zone |> should equal CardZone.Oche

    [<Test>]
    member _.``a recoil knockout should not trigger the send-home reaction``() =
        let donni =
            MatchScenario.PlainCard "donni" "BLK-026" MatchScenario.FirstPlayer CardZone.Booth -1

        let state =
            addCards
                (MatchScenario.BattleState "BLK-081" "BLK-076" [ "VIM-BEER"; "VIM-SOBER" ] 703UL)
                [ donni ]

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-081-B02")
            )

        applied.PendingKnockout.IsNone |> should be True

        (applied.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.EmptiesTray

        (applied.Card donni.Id).Zone |> should equal CardZone.Booth

    [<Test>]
    member _.``an item lock should not block an otherwise legal supporter``() =
        let item =
            MatchScenario.PlainCard "item" "KIT-001" MatchScenario.SecondPlayer CardZone.Mitt -1

        let supporter =
            MatchScenario.PlainCard
                "supporter"
                "KIT-005"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let state =
            addCards
                (MatchScenario.BattleState "BLK-049" "BLK-150" [ "VIM-BLAZED" ] 709UL)
                [ item; supporter ]

        let attacked =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-049-B01")
            )

        let actions =
            MatchScenario.Engine().GetLegalActions(attacked, MatchScenario.SecondPlayer)

        let playsKit (kit: CardInstanceId) =
            actions
            |> Seq.exists (fun action ->
                action.Kind = LegalActionKind.PlayKit
                && (match action.Command.Action with
                    | MatchAction.PlayKit(played, _) -> played = kit
                    | _ -> false))

        playsKit item.Id |> should be False
        playsKit supporter.Id |> should be True

    [<Test>]
    member _.``a chosen soft spot should persist until the defender leaves the oche``() =
        let state =
            addCards
                (MatchScenario.BattleState "BLK-137" "BLK-076" [ "VIM-SOBER" ] 719UL)
                [ MatchScenario.PlainCard
                      "first-stack-2"
                      "VIM-SOBER"
                      MatchScenario.FirstPlayer
                      CardZone.Stack
                      1
                  MatchScenario.PlainCard
                      "second-stack-2"
                      "VIM-SOBER"
                      MatchScenario.SecondPlayer
                      CardZone.Stack
                      1 ]

        let engine = MatchScenario.Engine()

        let attacked =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    (attackAction engine state MatchScenario.FirstPlayer "BLK-137-B01").Command
                )
            )

        let opponentEnded =
            MatchScenario.Applied(
                engine.Apply(
                    attacked,
                    endRound attacked "end-opponent-round" MatchScenario.SecondPlayer
                )
            )

        let ownerEnded =
            MatchScenario.Applied(
                engine.Apply(
                    opponentEnded,
                    endRound opponentEnded "end-owner-round" MatchScenario.FirstPlayer
                )
            )

        ownerEnded.Effects
        |> Seq.exists (fun effect ->
            effect.SourceEffect = EffectId "BLK-137-B01"
            && effect.TargetCard = ValueSome(CardInstanceId "defender"))
        |> should be True

    [<Test>]
    member _.``a demotion should leave only the lower stage carrying the battle state``() =
        let lowerStage =
            MatchScenario.AttachedCard
                "lower-stage"
                "BLK-150"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let opposingVim =
            MatchScenario.AttachedCard
                "opposing-vim"
                "VIM-BEER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let state =
            MatchScenario.BattleState "BLK-142" "BLK-002" [ "VIM-SOBER"; "VIM-SOBER" ] 727UL

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = FrozenList<CardInstanceId>.Create opposingVim.Id
                UnderlyingCards = FrozenList<CardInstanceId>.Create lowerStage.Id }

        let state =
            { MatchScenario.WithCards state [ defender; lowerStage; opposingVim ] with
                Effects =
                    FrozenList<TemporaryEffect>
                        .Create(
                            { SourceEffect = EffectId "BLK-137-B01"
                              SourceCard = CardInstanceId "attacker"
                              Owner = MatchScenario.FirstPlayer
                              TargetCard = ValueSome(CardInstanceId "defender")
                              Kind = TemporaryEffectKind.ModifySoftSpot
                              Amount = 2
                              MechanicalTypes =
                                FrozenList<BlokemonMechanicalType>.Create
                                    BlokemonMechanicalType.Grass
                              RoughStates = FrozenList.empty
                              RelatedCards = FrozenList.empty
                              Conditions = FrozenList.empty
                              Duration = EffectDuration.WhileTargetInPlay
                              AppliesFromRound = state.RoundNumber
                              ExpiresAfterRound = state.RoundNumber }
                        ) }

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-142-B02")
            )

        let returned = applied.Card(CardInstanceId "defender")
        let active = applied.Card lowerStage.Id

        returned.Zone |> should equal CardZone.Mitt
        returned.Damage |> should equal 0
        returned.Attachments.Count |> should equal 0
        returned.UnderlyingCards.Count |> should equal 0
        active.Zone |> should equal CardZone.Oche
        active.Damage |> should equal 100
        active.Attachments |> Seq.toList |> should equal [ opposingVim.Id ]

        (applied.Card opposingVim.Id).AttachedTo |> should equal (ValueSome active.Id)

        applied.Effects
        |> Seq.exists (fun effect ->
            effect.TargetCard = ValueSome returned.Id
            && effect.Duration = EffectDuration.WhileTargetInPlay)
        |> should be False

    [<Test>]
    member _.``placed counters should not queue the send-home reaction as attack damage``() =
        let state = counterKnockoutState "BLK-110" true
        let engine = MatchScenario.Engine()

        let applied = MatchScenario.Applied(engine.Apply(state, counterAttack engine state))

        applied.PendingKnockout.IsNone |> should be True
        (applied.Card(CardInstanceId "donni")).Zone |> should equal CardZone.Booth

    [<Test>]
    member _.``placed counters should not trigger attack-damage retaliation``() =
        let state = counterKnockoutState "BLK-110" false
        let engine = MatchScenario.Engine()

        let applied = MatchScenario.Applied(engine.Apply(state, counterAttack engine state))

        (applied.Card(CardInstanceId "attacker")).Zone |> should equal CardZone.Oche

    [<Test>]
    member _.``repeated kits should keep one continuous effect for each attached copy``() =
        let secondTarget =
            MatchScenario.PlainCard
                "second-target"
                "BLK-036"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let firstTool =
            MatchScenario.AttachedCard
                "first-tool"
                "KIT-014"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let secondTool =
            MatchScenario.AttachedCard
                "second-tool"
                "KIT-014"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                secondTarget.Id

        let state = MatchScenario.BattleState "BLK-001" "BLK-002" [] 733UL

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = FrozenList<CardInstanceId>.Create firstTool.Id }

        let state =
            MatchScenario.WithCards
                state
                [ defender
                  { secondTarget with
                      Attachments = FrozenList<CardInstanceId>.Create secondTool.Id }
                  firstTool
                  secondTool ]

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(state, endRound state "refresh-repeated-tools" MatchScenario.FirstPlayer)
            )

        applied.Effects
        |> Seq.filter (fun effect -> effect.SourceEffect = EffectId "KIT-014-R01")
        |> Seq.map (fun effect -> effect.SourceCard)
        |> Seq.sortBy (fun card -> card.Value)
        |> Seq.toList
        |> should
            equal
            (List.sortBy (fun (card: CardInstanceId) -> card.Value) [ firstTool.Id; secondTool.Id ])

    [<Test>]
    member _.``the talent scout should shuffle only the cards that remain in the stack``() =
        let talentScout =
            MatchScenario.PlainCard
                "talent-scout"
                "KIT-005"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let selected =
            MatchScenario.PlainCard "selected" "BLK-004" MatchScenario.FirstPlayer CardZone.Stack 0

        let remaining =
            [ 1..4 ]
            |> List.map (fun index ->
                MatchScenario.PlainCard
                    $"remaining-{index}"
                    "VIM-SOBER"
                    MatchScenario.FirstPlayer
                    CardZone.Stack
                    index)

        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 739UL

        let state =
            { state with
                Cards =
                    FrozenList<CardState>
                        .Create(
                            state.Cards
                            |> Seq.filter (fun card -> card.Id.Value <> "first-draw")
                            |> Seq.append (talentScout :: selected :: remaining)
                            |> Seq.sortBy (fun card -> card.Id)
                        ) }

        let engine = MatchScenario.Engine()
        let play = playKitAction engine state talentScout.Id
        let requested = MatchScenario.Applied(engine.Apply(state, play.Command))
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (FrozenList<EffectChoice>
                            .Create(
                                EffectChoice.Cards(
                                    requirement.Id,
                                    FrozenList<CardInstanceId>.Create selected.Id
                                )
                            ))
                )
            )

        (resolved.Card selected.Id).Zone |> should equal CardZone.Mitt

        resolved.Random.ConsumptionIndex - requested.Random.ConsumptionIndex
        |> should equal 3

    [<Test>]
    member _.``a copied attack should request its own card choice``() =
        let firstBench =
            MatchScenario.PlainCard
                "first-bench"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let secondBench =
            MatchScenario.PlainCard
                "second-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let otherBench =
            MatchScenario.PlainCard
                "other-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state =
            addCards
                (MatchScenario.BattleState
                    "BLK-151"
                    "BLK-106"
                    [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                    743UL)
                [ firstBench; secondBench; otherBench ]

        let engine = MatchScenario.Engine()
        let action = attackAction engine state MatchScenario.FirstPlayer "BLK-151-B01"
        let requested = MatchScenario.Applied(engine.Apply(state, action.Command))
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (FrozenList<EffectChoice>
                            .Create(
                                EffectChoice.Cards(
                                    requirement.Id,
                                    FrozenList<CardInstanceId>.Create secondBench.Id
                                )
                            ))
                )
            )

        requirement.EligibleCards
        |> Seq.sortBy (fun card -> card.Value)
        |> Seq.toList
        |> should
            equal
            (List.sortBy
                (fun (card: CardInstanceId) -> card.Value)
                [ firstBench.Id; secondBench.Id ])

        (resolved.Card secondBench.Id).Zone |> should equal CardZone.Oche

        (resolved.Card(CardInstanceId "attacker")).Zone |> should equal CardZone.Booth

    [<Test>]
    member _.``a forced switch should apply damage before removing the outgoing defender's effects``
        ()
        =
        let otherBench =
            MatchScenario.PlainCard
                "other-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state =
            addCards
                (MatchScenario.BattleState "BLK-012" "BLK-009" [ "VIM-BLAZED" ] 751UL)
                [ otherBench ]

        let engine = MatchScenario.Engine()
        let attack = attackAction engine state MatchScenario.FirstPlayer "BLK-012-B01"
        let requested = MatchScenario.Applied(engine.Apply(state, attack.Command))
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommandBy
                        requested
                        (FrozenList<EffectChoice>
                            .Create(
                                EffectChoice.Cards(
                                    requirement.Id,
                                    FrozenList<CardInstanceId>.Create otherBench.Id
                                )
                            ))
                        MatchScenario.SecondPlayer
                )
            )

        (applied.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Booth
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 30

    [<Test>]
    member _.``a typed fare ability should require matching vim and should not attach from the mitt``
        ()
        =
        let matching = fareAbilityState [ "VIM-SOBER" ]
        let nonMatching = fareAbilityState [ "VIM-BEER"; "VIM-BEER" ]

        let freeTaxi =
            ownBoothTaxi (
                MatchScenario.Engine().GetLegalActions(matching, MatchScenario.FirstPlayer)
            )

        let paidTaxi =
            ownBoothTaxi (
                MatchScenario.Engine().GetLegalActions(nonMatching, MatchScenario.FirstPlayer)
            )

        freeTaxi.Count |> should equal 0
        paidTaxi.Count |> should equal 2

        (matching.CardsIn(MatchScenario.FirstPlayer, CardZone.Mitt)
         |> Seq.filter (fun card -> card.Id = CardInstanceId "mitt-vim")
         |> Seq.exactlyOne)
            .Zone
        |> should equal CardZone.Mitt

    [<Test>]
    member _.``delayed counters should follow the original defender to the booth``() =
        let replacement =
            MatchScenario.PlainCard
                "other-booth"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let firstVim =
            MatchScenario.AttachedCard
                "other-vim-1"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let secondVim =
            MatchScenario.AttachedCard
                "other-vim-2"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let state =
            MatchScenario.BattleState "BLK-071" "BLK-024" [ "VIM-BLAZED"; "VIM-SOBER" ] 761UL

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = FrozenList<CardInstanceId>.Create(firstVim.Id, secondVim.Id) }

        let state =
            MatchScenario.WithCards state [ defender; replacement; firstVim; secondVim ]

        let engine = MatchScenario.Engine()

        let attacked =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-071-B02")
            )

        let taxied =
            MatchScenario.Applied(
                engine.Apply(
                    attacked,
                    MatchScenario.Command
                        attacked
                        "taxi-delayed-target"
                        MatchScenario.SecondPlayer
                        FrozenList.empty
                        (MatchAction.Taxi(
                            replacement.Id,
                            FrozenList<CardInstanceId>.Create(firstVim.Id, secondVim.Id)
                        ))
                )
            )

        let _, endedEvents =
            MatchScenario.AppliedWith(
                engine.Apply(taxied, endRound taxied "end-delayed-round" MatchScenario.SecondPlayer)
            )

        endedEvents
        |> Seq.exists (fun matchEvent ->
            matchEvent.Kind = MatchEventKind.DamagePlaced
            && Seq.contains (CardInstanceId "defender") matchEvent.TargetCards
            && matchEvent.Amount = 120)
        |> should be True

    [<Test>]
    member _.``an attached kit should reduce attack damage aimed at a boothed target``() =
        let boothTarget =
            MatchScenario.PlainCard
                "booth-target"
                "BLK-036"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let tool =
            MatchScenario.AttachedCard
                "booth-tool"
                "KIT-014"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                boothTarget.Id

        let state =
            addCards
                (MatchScenario.BattleState "BLK-042" "BLK-150" [ "VIM-SOBER" ] 769UL)
                [ { boothTarget with
                      Attachments = FrozenList<CardInstanceId>.Create tool.Id }
                  tool ]

        let engine = MatchScenario.Engine()
        let action = attackAction engine state MatchScenario.FirstPlayer "BLK-042-B01"

        let applied = MatchScenario.Applied(engine.Apply(state, action.Command))

        (applied.Card boothTarget.Id).Damage |> should equal 10

    [<Test>]
    member _.``a discard-recovery lock should block an opposing item from returning a kit to the stack``
        ()
        =
        let engine = MatchEngine(authorityWithItemDiscardRecovery ())

        let item =
            MatchScenario.PlainCard
                "recovery-item"
                "KIT-001"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let discardedTrainer =
            MatchScenario.PlainCard
                "discarded-trainer"
                "KIT-012"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state =
            addCards
                (MatchScenario.BattleState "BLK-001" "BLK-027" [] 773UL)
                [ item; discardedTrainer ]

        let action = playKitAction engine state item.Id

        let applied = MatchScenario.Applied(engine.Apply(state, action.Command))

        (applied.Card discardedTrainer.Id).Zone |> should equal CardZone.EmptiesTray

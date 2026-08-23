namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private TriggerConformanceFixtures =

    let private continuousPrograms =
        Set.ofList
            [ "BLK-009-T01"
              "BLK-014-T01"
              "BLK-021-T01"
              "BLK-027-T01"
              "BLK-034-T01"
              "BLK-104-T01"
              "BLK-122-T01"
              "BLK-139-T01"
              "BLK-141-T01"
              "BLK-144-T01"
              "BLK-145-T01"
              "BLK-146-T01"
              "BLK-149-T01"
              "KIT-001-T01"
              "KIT-002-T01"
              "KIT-003-T01" ]

    let private unconditionalContinuousPrograms =
        Set.ofList
            [ "BLK-009-T01"
              "BLK-014-T01"
              "BLK-027-T01"
              "BLK-141-T01"
              "BLK-149-T01"
              "KIT-001-T01"
              "KIT-003-T01" ]

    let private promotionPrograms =
        Set.ofList [ "BLK-044-T01"; "BLK-045-T01"; "BLK-093-T01"; "BLK-097-T01"; "BLK-130-T01" ]

    let isContinuousProgram programId =
        Set.contains programId continuousPrograms

    let isPromotionProgram programId =
        Set.contains programId promotionPrograms

    let private ownerId (programId: string) =
        programId.Substring(0, programId.Length - 4)

    let private setRoundsStarted rounds (state: MatchState) =
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

    let private continuousSourceZone programId positive =
        match programId with
        | "BLK-122-T01" -> CardZone.Oche
        | "BLK-139-T01"
        | "KIT-002-T01" -> if positive then CardZone.Oche else CardZone.Booth
        | _ when not positive && Set.contains programId unconditionalContinuousPrograms ->
            CardZone.Mitt
        | _ -> CardZone.Booth

    let private continuousVim programId positive =
        if not positive then
            match programId with
            | "BLK-122-T01" -> [ "VIM-SOBER" ]
            | _ -> []
        else
            match programId with
            | "BLK-144-T01" -> [ "VIM-SOBER" ]
            | "BLK-145-T01" -> [ "VIM-BEER" ]
            | "BLK-146-T01" -> [ "VIM-CURRY" ]
            | _ -> []

    let private continuousState programId positive =
        let state =
            MatchScenario.BattleState
                (ownerId programId)
                "BLK-150"
                (continuousVim programId positive)
                1009UL

        let zone = continuousSourceZone programId positive

        let source =
            { state.Card(CardInstanceId "attacker") with
                Zone = zone }

        let state =
            if zone = CardZone.Oche then
                MatchScenario.WithCards state [ source ]
            else
                MatchScenario.WithCards
                    state
                    [ source
                      MatchScenario.PlainCard
                          "replacement-active"
                          "BLK-001"
                          MatchScenario.FirstPlayer
                          CardZone.Oche
                          -1 ]

        match programId, positive with
        | "BLK-021-T01", true -> setRoundsStarted 1 state
        | "BLK-021-T01", false -> setRoundsStarted 2 state
        | "BLK-034-T01", true ->
            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "named-bloke"
                      "BLK-031"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1 ]
        | "BLK-104-T01", true ->
            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "named-bloke"
                      "BLK-105"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1 ]
        | _ -> state

    let private refresh (engine: MatchEngine) (state: MatchState) id =
        MatchScenario.AppliedWith(
            engine.Apply(
                state,
                MatchScenario.Command
                    state
                    id
                    MatchScenario.FirstPlayer
                    ImmutableArray<_>.Empty
                    MatchAction.EndRound
            )
        )

    let private sourceEffects (state: MatchState) =
        state.Effects
        |> Seq.filter (fun effect -> effect.SourceCard = CardInstanceId "attacker")
        |> Seq.map _.SourceEffect
        |> Seq.distinct
        |> Seq.toList

    let assertContinuousTrigger programId =
        let engine = MatchScenario.Engine()

        let positive, _ =
            refresh engine (continuousState programId true) $"refresh-positive:{programId}"

        let negative, _ =
            refresh engine (continuousState programId false) $"refresh-negative:{programId}"

        sourceEffects positive |> should equal [ EffectId programId ]
        sourceEffects negative |> should be Empty

    type private PromotionSentinel =
        | AttachedStackVim of ImmutableArray<CardInstanceId>
        | ReturnedMate of CardInstanceId
        | NoddedOffDefender
        | ChuckedStack of ImmutableArray<CardInstanceId>

    let private promotionSource programId =
        match programId with
        | "BLK-044-T01" -> "BLK-043"
        | "BLK-045-T01" -> "BLK-044"
        | "BLK-093-T01" -> "BLK-092"
        | "BLK-097-T01" -> "BLK-096"
        | "BLK-130-T01" -> "BLK-129"
        | _ -> raise (ArgumentOutOfRangeException(nameof programId, programId))

    let private stackCards count =
        let ids =
            [| for index in 0 .. count - 1 -> CardInstanceId $"promotion-stack-{index}" |]

        let cards =
            ids
            |> Seq.mapi (fun index id ->
                MatchScenario.PlainCard
                    id.Value
                    "VIM-SOBER"
                    MatchScenario.FirstPlayer
                    CardZone.Stack
                    index)

        ids, cards

    let private promotionState programId =
        let state =
            MatchScenario.BattleState (promotionSource programId) "BLK-150" [] 1013UL

        let promotion =
            MatchScenario.PlainCard
                "promotion"
                (ownerId programId)
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        match programId with
        | "BLK-044-T01" ->
            let ids, cards = stackCards 3

            MatchScenario.WithCards
                state
                (Seq.append
                    [ promotion ]
                    (Seq.append
                        cards
                        [ { state.Card(CardInstanceId "first-draw") with
                              Zone = CardZone.EmptiesTray } ])),
            AttachedStackVim(ImmutableArray.CreateRange ids)
        | "BLK-045-T01" ->
            let ids, cards = stackCards 8

            MatchScenario.WithCards
                state
                (Seq.append
                    [ promotion ]
                    (Seq.append
                        cards
                        [ { state.Card(CardInstanceId "first-draw") with
                              Zone = CardZone.EmptiesTray } ])),
            AttachedStackVim(ImmutableArray.CreateRange ids)
        | "BLK-093-T01" ->
            let mate =
                MatchScenario.PlainCard
                    "returned-mate"
                    "KIT-005"
                    MatchScenario.SecondPlayer
                    CardZone.EmptiesTray
                    -1

            MatchScenario.WithCards state [ promotion; mate ], ReturnedMate mate.Id
        | "BLK-097-T01" -> MatchScenario.WithCards state [ promotion ], NoddedOffDefender
        | "BLK-130-T01" ->
            let ids, cards = stackCards 5

            MatchScenario.WithCards
                state
                (Seq.append
                    [ promotion ]
                    (Seq.append
                        cards
                        [ { state.Card(CardInstanceId "first-draw") with
                              Zone = CardZone.EmptiesTray } ])),
            ChuckedStack(ImmutableArray.CreateRange ids)
        | _ -> raise (ArgumentOutOfRangeException(nameof programId, programId))

    let private promotionAction (engine: MatchEngine) (state: MatchState) =
        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action ->
            action.Kind = LegalActionKind.Promote
            && (match action.Command.Action with
                | MatchAction.Promote(promotion, target) ->
                    promotion = CardInstanceId "promotion" && target = CardInstanceId "attacker"
                | _ -> false))
        |> Seq.exactlyOne

    let private attachEveryEligibleVimToPromotion (action: LegalAction) =
        let choices =
            action.Command.Choices
            |> Seq.map (fun choice ->
                match choice with
                | EffectChoice.Attachments(id, _) ->
                    let requirement =
                        action.ChoiceRequirements |> Seq.find (fun value -> value.Id = id)

                    EffectChoice.Attachments(
                        id,
                        ImmutableArray.CreateRange(
                            requirement.EligibleCards
                            |> Seq.map (fun vim ->
                                { Vim = vim
                                  Bloke = CardInstanceId "promotion" })
                        )
                    )
                | _ -> choice)

        { action.Command with
            Choices = ImmutableArray.CreateRange choices }

    let private assertPromotionSentinel fired sentinel (state: MatchState) =
        match sentinel with
        | AttachedStackVim ids ->
            ids
            |> Seq.map (fun id -> (state.Card id).Zone, (state.Card id).AttachedTo)
            |> Seq.toList
            |> should
                equal
                (List.replicate
                    ids.Length
                    (if fired then
                         CardZone.Attached, ValueSome(CardInstanceId "promotion")
                     else
                         CardZone.Stack, ValueNone))
        | ReturnedMate mate ->
            (state.Card mate).Zone
            |> should equal (if fired then CardZone.Mitt else CardZone.EmptiesTray)
        | NoddedOffDefender ->
            (state.Card(CardInstanceId "defender")).RoughStates
            |> Seq.map _.State
            |> Seq.toList
            |> should equal (if fired then [ BlokemonRoughState.NoddedOff ] else [])
        | ChuckedStack ids ->
            ids
            |> Seq.map (fun id -> (state.Card id).Zone)
            |> Seq.toList
            |> should
                equal
                (List.replicate ids.Length (if fired then CardZone.EmptiesTray else CardZone.Stack))

    let assertPromotionTrigger programId =
        let engine = MatchScenario.Engine()
        let state, sentinel = promotionState programId
        let action = promotionAction engine state
        let command = attachEveryEligibleVimToPromotion action
        let positive, _ = MatchScenario.AppliedWith(engine.Apply(state, command))

        let negative, _ = refresh engine state $"non-promotion:{programId}"

        (positive.Card(CardInstanceId "promotion")).Zone |> should equal CardZone.Oche
        (negative.Card(CardInstanceId "promotion")).Zone |> should equal CardZone.Mitt
        assertPromotionSentinel true sentinel positive
        assertPromotionSentinel false sentinel negative

    let private countEffectEvents kind programId (events: MatchEvent seq) =
        events
        |> Seq.filter (fun matchEvent ->
            matchEvent.Kind = kind && matchEvent.Effect = ValueSome(EffectId programId))
        |> Seq.length

    let private applyAttack (engine: MatchEngine) state effect =
        MatchScenario.AppliedWith(engine.Apply(state, MatchScenario.AttackCommand state effect))

    let private assertKnockoutVimMove () =
        let engine = MatchScenario.Engine()

        let positive =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-001"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                103UL

        let source =
            MatchScenario.PlainCard
                "trigger-source"
                "BLK-026"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let movableVim =
            MatchScenario.AttachedCard
                "movable-vim"
                "VIM-BEER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let defender =
            { positive.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.Create movableVim.Id }

        let prize =
            MatchScenario.PlainCard "prize" "VIM-LAIRY" MatchScenario.FirstPlayer CardZone.BarChit 0

        let positive =
            MatchScenario.WithCards positive [ source; movableVim; defender; prize ]

        let positive = MatchScenario.WithBarChits positive MatchScenario.FirstPlayer 1
        let attacked, attackEvents = applyAttack engine positive "BLK-003-B01"

        let resolution =
            engine.GetLegalActions(attacked, MatchScenario.SecondPlayer)
            |> Seq.filter (fun action ->
                action.Kind = LegalActionKind.ResolveKnockoutTrigger
                && action.Command.Action = MatchAction.ResolveKnockoutTrigger(
                    ValueSome movableVim.Id
                ))
            |> Seq.exactlyOne

        let resolved, resolutionEvents =
            MatchScenario.AppliedWith(engine.Apply(attacked, resolution.Command))

        countEffectEvents MatchEventKind.TriggerQueued "BLK-026-T01" attackEvents
        |> should equal 1

        countEffectEvents MatchEventKind.TriggerResolved "BLK-026-T01" resolutionEvents
        |> should equal 1

        (resolved.Card movableVim.Id).AttachedTo |> should equal (ValueSome source.Id)

        let negativeSource =
            MatchScenario.PlainCard
                "trigger-source"
                "BLK-026"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let negative =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-081" "BLK-076" [ "VIM-BEER"; "VIM-SOBER" ] 703UL)
                [ negativeSource ]

        let rejectedContext, negativeEvents = applyAttack engine negative "BLK-081-B02"

        countEffectEvents MatchEventKind.TriggerQueued "BLK-026-T01" negativeEvents
        |> should equal 0

        rejectedContext.PendingKnockout.IsNone |> should be True

        (rejectedContext.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.EmptiesTray

        (rejectedContext.Card negativeSource.Id).Zone |> should equal CardZone.Booth

    let private assertRecoveryBeforeSendHome () =
        let engine = MatchScenario.Engine()

        let positive =
            MatchScenario.BattleState
                "BLK-076"
                "BLK-068"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                0UL

        let recovered, positiveEvents = applyAttack engine positive "BLK-076-B02"
        let recoveredDefender = recovered.Card(CardInstanceId "defender")

        countEffectEvents MatchEventKind.TriggerResolved "BLK-068-T01" positiveEvents
        |> should equal 1

        recoveredDefender.Zone |> should equal CardZone.Oche
        recoveredDefender.Damage |> should equal 170

        let negative = MatchScenario.BattleState "BLK-081" "BLK-068" [ "VIM-BEER" ] 0UL

        let nonlethal, negativeEvents = applyAttack engine negative "BLK-081-B01"
        let nonlethalDefender = nonlethal.Card(CardInstanceId "defender")

        countEffectEvents MatchEventKind.TriggerResolved "BLK-068-T01" negativeEvents
        |> should equal 0

        nonlethalDefender.Zone |> should equal CardZone.Oche
        nonlethalDefender.Damage |> should equal 10

    let private assertDamageRetaliation () =
        let engine = MatchScenario.Engine()
        let positive = MatchScenario.BattleState "BLK-076" "BLK-107" [ "VIM-LAIRY" ] 107UL
        let retaliated, positiveEvents = applyAttack engine positive "BLK-076-B01"

        countEffectEvents MatchEventKind.TriggerResolved "BLK-107-T01" positiveEvents
        |> should equal 1

        (retaliated.Card(CardInstanceId "attacker")).Damage |> should equal 30

        let negative = MatchScenario.BattleState "BLK-040" "BLK-107" [ "VIM-SOBER" ] 941UL
        let zeroDamage, negativeEvents = applyAttack engine negative "BLK-040-B01"
        let untouchedAttacker = zeroDamage.Card(CardInstanceId "attacker")
        let affectedDefender = zeroDamage.Card(CardInstanceId "defender")

        countEffectEvents MatchEventKind.TriggerResolved "BLK-107-T01" negativeEvents
        |> should equal 0

        untouchedAttacker.Damage |> should equal 0
        affectedDefender.Damage |> should equal 0

        affectedDefender.RoughStates
        |> Seq.map _.State
        |> Seq.toList
        |> should equal [ BlokemonRoughState.NoddedOff ]

    let private assertSendHomeRetaliation () =
        let engine = MatchScenario.Engine()

        let positive =
            MatchScenario.BattleState
                "BLK-076"
                "BLK-110"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                0UL

        let retaliated, positiveEvents = applyAttack engine positive "BLK-076-B02"

        countEffectEvents MatchEventKind.TriggerResolved "BLK-110-T01" positiveEvents
        |> should equal 1

        (retaliated.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.EmptiesTray

        (retaliated.Card(CardInstanceId "defender")).Zone
        |> should equal CardZone.EmptiesTray

        let negative = MatchScenario.BattleState "BLK-122" "BLK-110" [ "VIM-SOBER" ] 0UL

        let defender =
            { negative.Card(CardInstanceId "defender") with
                Damage = 80 }

        let negative = MatchScenario.WithCards negative [ defender ]

        let counterAttack =
            engine.GetLegalActions(negative, MatchScenario.FirstPlayer)
            |> Seq.filter (fun action ->
                action.Kind = LegalActionKind.Attack
                && (match action.Command.Action with
                    | MatchAction.Attack(_, effect) -> effect = EffectId "BLK-122-B01"
                    | _ -> false))
            |> Seq.exactlyOne

        let placedCounters, negativeEvents =
            MatchScenario.AppliedWith(engine.Apply(negative, counterAttack.Command))

        countEffectEvents MatchEventKind.TriggerResolved "BLK-110-T01" negativeEvents
        |> should equal 0

        negativeEvents
        |> Seq.filter (fun matchEvent ->
            matchEvent.Kind = MatchEventKind.DamagePlaced
            && matchEvent.DamageKind = ValueSome DamageKind.PlacedCounter
            && Seq.contains (CardInstanceId "defender") matchEvent.TargetCards)
        |> Seq.map _.Amount
        |> Seq.toList
        |> should equal [ 30 ]

        (placedCounters.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.Oche

        (placedCounters.Card(CardInstanceId "defender")).Zone
        |> should equal CardZone.EmptiesTray

    let private barChitState boothCount =
        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-001"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                0UL

        let triggeredPrize =
            MatchScenario.PlainCard
                "triggered-prize"
                "BLK-113"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                0

        let extraPrize =
            MatchScenario.PlainCard
                "extra-prize"
                "VIM-LAIRY"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                1

        let defenderBench =
            MatchScenario.PlainCard
                "defender-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let booth =
            [ for index in 0 .. boothCount - 1 ->
                  MatchScenario.PlainCard
                      $"full-booth-{index}"
                      "BLK-004"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      index ]

        let state =
            MatchScenario.WithCards
                state
                (Seq.concat [ [ triggeredPrize; extraPrize; defenderBench ]; booth ])

        MatchScenario.WithBarChits state MatchScenario.FirstPlayer 2, triggeredPrize, extraPrize

    let private assertBarChitReaction () =
        let engine = MatchScenario.Engine()
        let positive, triggeredPrize, extraPrize = barChitState 0
        let attacked, attackEvents = applyAttack engine positive "BLK-003-B01"

        countEffectEvents MatchEventKind.TriggerQueued "BLK-113-T01" attackEvents
        |> should equal 1

        attacked.PendingBarChits.Length |> should equal 1

        let resolution =
            engine.GetLegalActions(attacked, MatchScenario.FirstPlayer)
            |> Seq.filter (fun action ->
                action.Kind = LegalActionKind.ResolveBarChitTrigger
                && action.Command.Action = MatchAction.ResolveBarChitTrigger true)
            |> Seq.exactlyOne

        let resolved, resolutionEvents =
            MatchScenario.AppliedWith(engine.Apply(attacked, resolution.Command))

        countEffectEvents MatchEventKind.TriggerResolved "BLK-113-T01" resolutionEvents
        |> should equal 1

        (resolved.Card triggeredPrize.Id).Zone |> should equal CardZone.Booth
        (resolved.Card extraPrize.Id).Zone |> should equal CardZone.Mitt
        (resolved.Player MatchScenario.FirstPlayer).BarChitsRemaining |> should equal 0

        let negative, fullBoothPrize, untouchedPrize = barChitState 5
        let notQueued, negativeEvents = applyAttack engine negative "BLK-003-B01"

        countEffectEvents MatchEventKind.TriggerQueued "BLK-113-T01" negativeEvents
        |> should equal 0

        countEffectEvents MatchEventKind.BeerMatTossed "BLK-113-T01" negativeEvents
        |> should equal 0

        notQueued.PendingBarChits.Length |> should equal 0
        (notQueued.Card fullBoothPrize.Id).Zone |> should equal CardZone.Mitt
        (notQueued.Card untouchedPrize.Id).Zone |> should equal CardZone.BarChit
        (notQueued.Player MatchScenario.FirstPlayer).BarChitsRemaining |> should equal 1

    let assertReactiveTrigger programId =
        match programId with
        | "BLK-026-T01" -> assertKnockoutVimMove ()
        | "BLK-068-T01" -> assertRecoveryBeforeSendHome ()
        | "BLK-107-T01" -> assertDamageRetaliation ()
        | "BLK-110-T01" -> assertSendHomeRetaliation ()
        | "BLK-113-T01" -> assertBarChitReaction ()
        | _ -> raise (ArgumentOutOfRangeException(nameof programId, programId))

type TriggerConformanceTests() =

    [<Test>]
    [<Arguments("BLK-009-T01")>]
    [<Arguments("BLK-014-T01")>]
    [<Arguments("BLK-021-T01")>]
    [<Arguments("BLK-027-T01")>]
    [<Arguments("BLK-034-T01")>]
    [<Arguments("BLK-104-T01")>]
    [<Arguments("BLK-122-T01")>]
    [<Arguments("BLK-139-T01")>]
    [<Arguments("BLK-141-T01")>]
    [<Arguments("BLK-144-T01")>]
    [<Arguments("BLK-145-T01")>]
    [<Arguments("BLK-146-T01")>]
    [<Arguments("BLK-149-T01")>]
    [<Arguments("KIT-001-T01")>]
    [<Arguments("KIT-002-T01")>]
    [<Arguments("KIT-003-T01")>]
    [<Arguments("BLK-044-T01")>]
    [<Arguments("BLK-045-T01")>]
    [<Arguments("BLK-093-T01")>]
    [<Arguments("BLK-097-T01")>]
    [<Arguments("BLK-130-T01")>]
    [<Arguments("BLK-026-T01")>]
    [<Arguments("BLK-068-T01")>]
    [<Arguments("BLK-107-T01")>]
    [<Arguments("BLK-110-T01")>]
    [<Arguments("BLK-113-T01")>]
    member _.``every non-activated authority trigger should fire only in its declared context``
        (programId: string)
        =
        if isContinuousProgram programId then
            assertContinuousTrigger programId
        elif isPromotionProgram programId then
            assertPromotionTrigger programId
        else
            assertReactiveTrigger programId

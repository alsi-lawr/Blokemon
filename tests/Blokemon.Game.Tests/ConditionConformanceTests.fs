namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

module private ConditionConformanceScenarios =

    let private attackerId = CardInstanceId "attacker"
    let private defenderId = CardInstanceId "defender"

    let private applyAttack (engine: MatchEngine) (state: MatchState) (effect: string) =
        MatchScenario.Applied(engine.Apply(state, MatchScenario.AttackCommand state effect))

    let private applyAttackWithEvents (engine: MatchEngine) (state: MatchState) (effect: string) =
        MatchScenario.AppliedWith(engine.Apply(state, MatchScenario.AttackCommand state effect))

    let private attackCommand
        (state: MatchState)
        (id: string)
        (actor: PlayerId)
        (source: CardInstanceId)
        (effect: string)
        choices
        =
        MatchScenario.Command state id actor choices (MatchAction.Attack(source, EffectId effect))

    let private withAttachedVim
        (state: MatchState)
        (targetId: CardInstanceId)
        (prefix: string)
        (mechanicalIds: string seq)
        =
        let target = state.Card targetId

        let vim =
            mechanicalIds
            |> Seq.mapi (fun index mechanicalId ->
                MatchScenario.AttachedCard
                    $"{prefix}-{index}"
                    mechanicalId
                    target.Owner
                    CardZone.Attached
                    -1
                    target.Id)
            |> Seq.toArray

        let attachments =
            ImmutableArray.CreateRange(
                Seq.append target.Attachments (vim |> Seq.map (fun card -> card.Id))
            )

        MatchScenario.WithCards
            state
            ({ target with
                Attachments = attachments }
             :: List.ofArray vim)

    let private withRoundsStarted (state: MatchState) (playerId: PlayerId) roundsStarted =
        { state with
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun player ->
                        if player.Id = playerId then
                            { player with
                                RoundsStarted = roundsStarted }
                        else
                            player)
                ) }

    let private hasAttack (engine: MatchEngine) (state: MatchState) (effect: string) =
        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.exists (fun action ->
            action.Kind = LegalActionKind.Attack
            && (match action.Command.Action with
                | MatchAction.Attack(_, attack) -> attack = EffectId effect
                | _ -> false))

    let private seedForResults (expected: bool list) =
        let rec search seed =
            let random = BlokemonSeededRandom seed

            let actual = expected |> List.map (fun _ -> random.NextInt 2 = 1)

            if actual = expected then seed else search (seed + 1UL)

        search 0UL

    let private hasRoughState roughState (card: CardState) =
        card.RoughStates |> Seq.exists (fun entry -> entry.State = roughState)

    let private verifyAttachedVimCountsAreEqual () =
        let matching =
            MatchScenario.BattleState "BLK-001" "BLK-122" [ "VIM-BLAZED"; "VIM-SOBER" ] 1001UL
            |> fun state ->
                withAttachedVim state defenderId "matching-other-vim" [ "VIM-GEEKED"; "VIM-SOBER" ]

        let different =
            MatchScenario.BattleState "BLK-001" "BLK-122" [ "VIM-BLAZED"; "VIM-SOBER" ] 1003UL
            |> fun state -> withAttachedVim state defenderId "different-other-vim" [ "VIM-GEEKED" ]

        let engine = MatchScenario.Engine()
        let matchingResult = applyAttack engine matching "BLK-001-B01"
        let differentResult = applyAttack engine different "BLK-001-B01"

        (matchingResult.Card defenderId).Damage |> should equal 0
        (differentResult.Card defenderId).Damage |> should equal 20

    let private boothTriggerState boothCount =
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

        let replacement =
            MatchScenario.PlainCard
                "replacement"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let booth =
            [ 0 .. boothCount - 1 ]
            |> List.map (fun index ->
                MatchScenario.PlainCard
                    $"own-booth-{index}"
                    "BLK-004"
                    MatchScenario.FirstPlayer
                    CardZone.Booth
                    -1)

        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-001"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                0UL

        MatchScenario.WithCards state (triggeredPrize :: extraPrize :: replacement :: booth)
        |> fun current -> MatchScenario.WithBarChits current MatchScenario.FirstPlayer 2

    let private verifyBoothHasSpace () =
        let engine = MatchScenario.Engine()
        let withRoom = boothTriggerState 0

        let queued, queuedEvents = applyAttackWithEvents engine withRoom "BLK-003-B01"

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    queued,
                    MatchScenario.Command
                        queued
                        "resolve-roomy-booth-trigger"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        (MatchAction.ResolveBarChitTrigger true)
                )
            )

        let full = boothTriggerState MatchScenario.Authority.BaseRules.Opening.BoothLimit

        let skipped, skippedEvents = applyAttackWithEvents engine full "BLK-003-B01"

        queued.PendingBarChits.Length |> should equal 1

        queuedEvents
        |> Seq.exists (fun matchEvent ->
            matchEvent.Kind = MatchEventKind.TriggerQueued
            && matchEvent.SourceCard = ValueSome(CardInstanceId "triggered-prize"))
        |> should be True

        (resolved.Card(CardInstanceId "triggered-prize")).Zone
        |> should equal CardZone.Booth

        skipped.PendingBarChits.Length |> should equal 0

        skippedEvents
        |> Seq.exists (fun matchEvent ->
            matchEvent.Kind = MatchEventKind.TriggerQueued
            && matchEvent.SourceCard = ValueSome(CardInstanceId "triggered-prize"))
        |> should be False

        (skipped.Card(CardInstanceId "triggered-prize")).Zone
        |> should equal CardZone.Mitt

    let private verifyFirstBeerMatIsBlankSide () =
        let blankFirst =
            MatchScenario.BattleState
                "BLK-073"
                "BLK-003"
                [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                (seedForResults [ false ])

        let badgeFirst =
            MatchScenario.BattleState
                "BLK-073"
                "BLK-003"
                [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                (seedForResults [ true; false ])

        let engine = MatchScenario.Engine()
        let blankResult, blankEvents = applyAttackWithEvents engine blankFirst "BLK-073-B02"
        let badgeResult, badgeEvents = applyAttackWithEvents engine badgeFirst "BLK-073-B02"

        blankEvents
        |> Seq.choose (fun matchEvent ->
            if matchEvent.Kind = MatchEventKind.BeerMatTossed then
                matchEvent.BadgeSide |> ValueOption.toOption
            else
                None)
        |> Seq.toList
        |> should equal [ false ]

        badgeEvents
        |> Seq.choose (fun matchEvent ->
            if matchEvent.Kind = MatchEventKind.BeerMatTossed then
                matchEvent.BadgeSide |> ValueOption.toOption
            else
                None)
        |> Seq.toList
        |> should equal [ true; false ]

        blankResult.Card defenderId
        |> hasRoughState BlokemonRoughState.Muddled
        |> should be True

        badgeResult.Card defenderId
        |> hasRoughState BlokemonRoughState.Muddled
        |> should be False

    let private verifyMittCountsAreEqual () =
        let matchingMitts =
            MatchScenario.BattleState "BLK-038" "BLK-006" [ "VIM-CURRY"; "VIM-SOBER" ] 1007UL

        let unequal =
            MatchScenario.WithCards
                matchingMitts
                [ MatchScenario.PlainCard
                      "extra-own-mitt"
                      "VIM-CURRY"
                      MatchScenario.FirstPlayer
                      CardZone.Mitt
                      -1 ]

        let engine = MatchScenario.Engine()
        let equalResult = applyAttack engine matchingMitts "BLK-038-B02"
        let unequalResult = applyAttack engine unequal "BLK-038-B02"

        (equalResult.Card defenderId).Damage |> should equal 220
        (unequalResult.Card defenderId).Damage |> should equal 80

    let private verifyNamedBlokeInBooth () =
        let absent = MatchScenario.BattleState "BLK-125" "BLK-006" [ "VIM-BEER" ] 1013UL

        let present =
            MatchScenario.WithCards
                absent
                [ MatchScenario.PlainCard
                      "mr-vesta"
                      "BLK-126"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1 ]

        let engine = MatchScenario.Engine()
        let presentResult = applyAttack engine present "BLK-125-B01"
        let absentResult = applyAttack engine absent "BLK-125-B01"

        (presentResult.Card defenderId).Damage |> should equal 50
        (absentResult.Card defenderId).Damage |> should equal 10

    let private verifyNamedBlokeInPlay () =
        let absent = MatchScenario.BattleState "BLK-034" "BLK-150" [] 1019UL

        let present =
            MatchScenario.WithCards
                absent
                [ MatchScenario.PlainCard
                      "mlm-queen"
                      "BLK-031"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1 ]

        let engine = MatchScenario.Engine()
        hasAttack engine present "BLK-034-B01" |> should be True
        hasAttack engine absent "BLK-034-B01" |> should be False

    let private verifyOpenedSecond () =
        let promotion =
            MatchScenario.PlainCard "promotion" "BLK-022" MatchScenario.FirstPlayer CardZone.Mitt -1

        let openedSecond =
            MatchScenario.BattleState "BLK-021" "BLK-150" [] 1021UL
            |> fun state -> withRoundsStarted state MatchScenario.FirstPlayer 1
            |> fun state -> MatchScenario.WithCards state [ promotion ]

        let openedFirst =
            { openedSecond with
                OpeningPlayer = MatchScenario.FirstPlayer }

        let canPromote (state: MatchState) =
            MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.exists (fun action ->
                action.Kind = LegalActionKind.Promote
                && (match action.Command.Action with
                    | MatchAction.Promote(selected, target) ->
                        selected = promotion.Id && target = attackerId
                    | _ -> false))

        canPromote openedSecond |> should be True
        canPromote openedFirst |> should be False

    let private optionalState () =
        let eligible =
            MatchScenario.PlainCard
                "eligible-empties-vim"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state =
            MatchScenario.BattleState "BLK-008" "BLK-003" [ "VIM-SOBER" ] 1027UL
            |> fun current -> MatchScenario.WithCards current [ eligible ]

        state, eligible

    let private verifyOptional () =
        let engine = MatchScenario.Engine()
        let declinedState, declinedEligible = optionalState ()

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(declinedState, MatchScenario.AttackCommand declinedState "BLK-008-B01")
            )

        let requirements = missing.ChoiceRequirements |> Seq.toArray

        let optional =
            requirements
            |> Array.find (fun value -> value.Kind = ChoiceRequirementKind.Optional)

        let declined =
            MatchScenario.Applied(
                engine.Apply(
                    declinedState,
                    MatchScenario.AttackCommandWith
                        declinedState
                        "BLK-008-B01"
                        (ImmutableArray.Create(EffectChoice.Optional(optional.Id, false)))
                )
            )

        let acceptedState, acceptedEligible = optionalState ()

        let acceptedOptional =
            MatchScenario.Applied(
                engine.Apply(
                    acceptedState,
                    MatchScenario.AttackCommandWith
                        acceptedState
                        "BLK-008-B01"
                        (ImmutableArray.Create(EffectChoice.Optional(optional.Id, true)))
                )
            )

        let cards = acceptedOptional.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let accepted =
            MatchScenario.Applied(
                engine.Apply(
                    acceptedOptional,
                    MatchScenario.ResolveEffectChoiceCommand
                        acceptedOptional
                        (ImmutableArray.Create(
                            EffectChoice.Cards(cards.Id, ImmutableArray.Create acceptedEligible.Id)
                        ))
                )
            )

        missing.Code |> should equal CommandRejectionCode.ChoiceRequired

        requirements
        |> Array.map (fun value -> value.Kind)
        |> should equal [| ChoiceRequirementKind.Optional |]

        acceptedOptional.Phase |> should equal MatchPhase.AwaitingEffectChoice
        cards.Kind |> should equal ChoiceRequirementKind.Cards
        cards.EligibleCards |> Seq.toList |> should contain declinedEligible.Id
        (declined.Card declinedEligible.Id).Zone |> should equal CardZone.EmptiesTray
        (accepted.Card acceptedEligible.Id).Zone |> should equal CardZone.Mitt

    let private verifyOtherBoothExists () =
        let absent =
            MatchScenario.BattleState "BLK-012" "BLK-150" [ "VIM-SOBER"; "VIM-SOBER" ] 1031UL

        let otherBooth =
            MatchScenario.PlainCard
                "other-booth"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let present = MatchScenario.WithCards absent [ otherBooth ]
        let engine = MatchScenario.Engine()

        let requested =
            MatchScenario.Applied(
                engine.Apply(present, MatchScenario.AttackCommand present "BLK-012-B02")
            )

        let cards = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let presentResult =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (ImmutableArray.Create(
                            EffectChoice.Cards(cards.Id, ImmutableArray.Create otherBooth.Id)
                        ))
                )
            )

        let absentResult = applyAttack engine absent "BLK-012-B02"

        requested.Phase |> should equal MatchPhase.AwaitingEffectChoice
        (presentResult.Card attackerId).Zone |> should equal CardZone.Stack
        (presentResult.Card otherBooth.Id).Zone |> should equal CardZone.Stack
        (absentResult.Card attackerId).Zone |> should equal CardZone.Oche

    let private verifyOtherOcheHasDamage () =
        let undamaged =
            MatchScenario.BattleState
                "BLK-028"
                "BLK-006"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                1033UL

        let damaged =
            let defender = undamaged.Card defenderId
            MatchScenario.WithCards undamaged [ { defender with Damage = 10 } ]

        let engine = MatchScenario.Engine()
        let damagedResult = applyAttack engine damaged "BLK-028-B02"
        let undamagedResult = applyAttack engine undamaged "BLK-028-B02"

        (damagedResult.Card defenderId).Damage |> should equal 190
        (undamagedResult.Card defenderId).Damage |> should equal 80

    let private verifyOtherOcheHasMechanicalType () =
        let matching = MatchScenario.BattleState "BLK-010" "BLK-001" [ "VIM-BLAZED" ] 1039UL

        let different =
            MatchScenario.BattleState "BLK-010" "BLK-004" [ "VIM-BLAZED" ] 1049UL

        let engine = MatchScenario.Engine()
        let matchingResult = applyAttack engine matching "BLK-010-B01"
        let differentResult = applyAttack engine different "BLK-010-B01"

        (matchingResult.Card defenderId).Damage |> should equal 40
        (differentResult.Card defenderId).Damage |> should equal 10

    let private verifyOtherOcheHasRoughState () =
        let replacement =
            MatchScenario.PlainCard
                "other-replacement"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let ordinary =
            MatchScenario.BattleState
                "BLK-124"
                "BLK-003"
                [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                1051UL
            |> fun state -> MatchScenario.WithCards state [ replacement ]

        let noddedOff =
            let defender = ordinary.Card defenderId

            MatchScenario.WithCards
                ordinary
                [ { defender with
                      RoughStates =
                          ImmutableArray.Create(
                              MatchScenario.RoughState BlokemonRoughState.NoddedOff 1
                          ) } ]

        let engine = MatchScenario.Engine()
        let noddedOffResult = applyAttack engine noddedOff "BLK-124-B01"
        let ordinaryResult = applyAttack engine ordinary "BLK-124-B01"

        (noddedOffResult.Card defenderId).Zone |> should equal CardZone.EmptiesTray
        (ordinaryResult.Card defenderId).Zone |> should equal CardZone.Oche

    let private verifyOtherOcheIsBigHitter () =
        let bigHitter =
            MatchScenario.BattleState
                "BLK-134"
                "BLK-003"
                [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                1061UL

        let ordinary =
            MatchScenario.BattleState
                "BLK-134"
                "BLK-002"
                [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                1063UL

        let engine = MatchScenario.Engine()
        let bigHitterResult = applyAttack engine bigHitter "BLK-134-B02"
        let ordinaryResult = applyAttack engine ordinary "BLK-134-B02"

        (bigHitterResult.Card defenderId).Damage |> should equal 180
        (ordinaryResult.Card defenderId).Damage |> should equal 90

    let private verifyOtherOcheIsPromoted () =
        let promoted =
            MatchScenario.BattleState "BLK-142" "BLK-002" [ "VIM-SOBER"; "VIM-SOBER" ] 1069UL

        let lowerStage =
            MatchScenario.AttachedCard
                "lower-stage"
                "BLK-001"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                defenderId

        let promotedDefender =
            { promoted.Card defenderId with
                UnderlyingCards = ImmutableArray.Create lowerStage.Id }

        let promoted = MatchScenario.WithCards promoted [ promotedDefender; lowerStage ]

        let unpromoted =
            MatchScenario.BattleState "BLK-142" "BLK-003" [ "VIM-SOBER"; "VIM-SOBER" ] 1087UL

        let engine = MatchScenario.Engine()
        let promotedResult = applyAttack engine promoted "BLK-142-B02"
        let unpromotedResult = applyAttack engine unpromoted "BLK-142-B02"

        (promotedResult.Card defenderId).Zone |> should equal CardZone.Mitt
        (promotedResult.Card lowerStage.Id).Zone |> should equal CardZone.EmptiesTray
        (promotedResult.Card lowerStage.Id).Damage |> should equal 100
        (unpromotedResult.Card defenderId).Zone |> should equal CardZone.Oche
        (unpromotedResult.Card defenderId).Damage |> should equal 100

    let private knockoutBonusState defenderMechanicalId =
        let replacement =
            MatchScenario.PlainCard
                "other-replacement"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let barChits =
            [ 0..5 ]
            |> List.map (fun index ->
                MatchScenario.PlainCard
                    $"bonus-bar-chit-{index}"
                    "VIM-SOBER"
                    MatchScenario.FirstPlayer
                    CardZone.BarChit
                    index)

        MatchScenario.BattleState
            "BLK-036"
            defenderMechanicalId
            [ "VIM-GEEKED"; "VIM-GEEKED"; "VIM-GEEKED" ]
            1091UL
        |> fun state -> MatchScenario.WithCards state (replacement :: barChits)

    let private verifyOtherSentHomeByThisAttackDamage () =
        let sentHome = knockoutBonusState "BLK-067"
        let stayed = knockoutBonusState "BLK-150"
        let engine = MatchScenario.Engine()
        let sentHomeResult = applyAttack engine sentHome "BLK-036-B02"
        let stayedResult = applyAttack engine stayed "BLK-036-B02"

        (sentHomeResult.Card defenderId).Zone |> should equal CardZone.EmptiesTray

        (sentHomeResult.Player MatchScenario.FirstPlayer).BarChitsRemaining
        |> should equal 4

        (stayedResult.Card defenderId).Zone |> should equal CardZone.Oche

        (stayedResult.Player MatchScenario.FirstPlayer).BarChitsRemaining
        |> should equal 6

    let private verifyOwnBarChitCountIsGreater () =
        let state =
            MatchScenario.BattleState
                "BLK-127"
                "BLK-003"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                1093UL

        let greater =
            state
            |> fun current -> MatchScenario.WithBarChits current MatchScenario.FirstPlayer 6
            |> fun current -> MatchScenario.WithBarChits current MatchScenario.SecondPlayer 5

        let notGreater =
            state
            |> fun current -> MatchScenario.WithBarChits current MatchScenario.FirstPlayer 5
            |> fun current -> MatchScenario.WithBarChits current MatchScenario.SecondPlayer 6

        let engine = MatchScenario.Engine()
        let greaterResult = applyAttack engine greater "BLK-127-B02"
        let notGreaterResult = applyAttack engine notGreater "BLK-127-B02"

        (greaterResult.Card defenderId).Damage |> should equal 180
        (notGreaterResult.Card defenderId).Damage |> should equal 90

    let private verifyOwnMittIsEmpty () =
        let empty =
            MatchScenario.BattleState "BLK-015" "BLK-006" [ "VIM-SOBER"; "VIM-SOBER" ] 1097UL

        let occupied =
            MatchScenario.WithCards
                empty
                [ MatchScenario.PlainCard
                      "own-mitt-card"
                      "VIM-BLAZED"
                      MatchScenario.FirstPlayer
                      CardZone.Mitt
                      -1 ]

        let engine = MatchScenario.Engine()
        let emptyResult = applyAttack engine empty "BLK-015-B01"
        let occupiedResult = applyAttack engine occupied "BLK-015-B01"

        (emptyResult.Card defenderId).Damage |> should equal 160
        (occupiedResult.Card defenderId).Damage |> should equal 30

    let private verifyPromotedFromMittThisRound () =
        let promoted =
            MatchScenario.BattleState
                "BLK-080"
                "BLK-003"
                [ "VIM-GEEKED"; "VIM-SOBER"; "VIM-SOBER" ]
                1103UL

        let lowerStage =
            MatchScenario.AttachedCard
                "own-lower-stage"
                "BLK-079"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                attackerId

        let currentRound =
            { promoted.Card attackerId with
                UnderlyingCards = ImmutableArray.Create lowerStage.Id
                LastPromotedRound = promoted.RoundNumber }

        let previousRound =
            { currentRound with
                LastPromotedRound = promoted.RoundNumber - 1 }

        let currentState = MatchScenario.WithCards promoted [ currentRound; lowerStage ]
        let previousState = MatchScenario.WithCards promoted [ previousRound; lowerStage ]
        let engine = MatchScenario.Engine()
        let currentResult = applyAttack engine currentState "BLK-080-B02"
        let previousResult = applyAttack engine previousState "BLK-080-B02"

        (currentResult.Card defenderId).Damage |> should equal 0
        (previousResult.Card defenderId).Damage |> should equal 160

    let private verifySelfHasDamage () =
        let undamaged = MatchScenario.BattleState "BLK-006" "BLK-006" [ "VIM-CURRY" ] 1109UL

        let damaged =
            let attacker = undamaged.Card attackerId
            MatchScenario.WithCards undamaged [ { attacker with Damage = 10 } ]

        let engine = MatchScenario.Engine()
        let damagedResult = applyAttack engine damaged "BLK-006-B01"
        let undamagedResult = applyAttack engine undamaged "BLK-006-B01"

        (damagedResult.Card defenderId).Damage |> should equal 160
        (undamagedResult.Card defenderId).Damage |> should equal 60

    let private verifySelfHasRoughState () =
        let ordinary =
            MatchScenario.BattleState
                "BLK-057"
                "BLK-003"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                (seedForResults [ true ])

        let muddled =
            let attacker = ordinary.Card attackerId

            MatchScenario.WithCards
                ordinary
                [ { attacker with
                      RoughStates =
                          ImmutableArray.Create(
                              MatchScenario.RoughState BlokemonRoughState.Muddled 1
                          ) } ]

        let engine = MatchScenario.Engine()
        let muddledResult = applyAttack engine muddled "BLK-057-B02"
        let ordinaryResult = applyAttack engine ordinary "BLK-057-B02"

        (muddledResult.Card defenderId).Damage |> should equal 150
        (ordinaryResult.Card defenderId).Damage |> should equal 0

    let private fareAbilityState attachedVim =
        let state = MatchScenario.BattleState "BLK-144" "BLK-150" attachedVim 1123UL

        let booth =
            MatchScenario.PlainCard
                "own-booth"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let mitt =
            [ 0..2 ]
            |> List.map (fun index ->
                MatchScenario.PlainCard
                    $"taxi-vim-{index}"
                    "VIM-SOBER"
                    MatchScenario.FirstPlayer
                    CardZone.Mitt
                    -1)

        MatchScenario.WithCards state (booth :: mitt)

    let private taxiAction (state: MatchState) targetId =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action ->
            action.Kind = LegalActionKind.Taxi
            && (match action.Command.Action with
                | MatchAction.Taxi(target, _) -> target = targetId
                | _ -> false))
        |> Seq.exactlyOne

    let private taxiVimCount (state: MatchState) targetId =
        taxiAction state targetId
        |> fun action ->
            match action.Command.Action with
            | MatchAction.Taxi(_, vim) -> vim.Length
            | _ -> failwith "Expected a taxi action."

    let private verifySelfHasVim () =
        let hasVim = fareAbilityState [ "VIM-SOBER" ]
        let noVim = fareAbilityState []
        let hasVimAction = taxiAction hasVim (CardInstanceId "own-booth")
        let noVimAction = taxiAction noVim (CardInstanceId "own-booth")

        hasVimAction.Affordability |> should equal ActionAffordability.Payable

        noVimAction.Affordability
        |> should equal (ActionAffordability.ShortOfTaxiFare 2)

        taxiVimCount hasVim (CardInstanceId "own-booth") |> should equal 0

    let private poolCueState sourceZone =
        let state = MatchScenario.BattleState "BLK-105" "BLK-150" [ "VIM-LAIRY" ] 1129UL

        match sourceZone with
        | CardZone.Booth ->
            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "pool-cue"
                      "BLK-104"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1 ]
        | CardZone.Oche ->
            let lastOrders =
                { state.Card attackerId with
                    Zone = CardZone.Booth }

            let poolCue =
                MatchScenario.PlainCard
                    "pool-cue"
                    "BLK-104"
                    MatchScenario.FirstPlayer
                    CardZone.Oche
                    -1

            MatchScenario.WithCards state [ lastOrders; poolCue ]
        | other -> failwith $"Unsupported Pool Cue zone {other}."

    let private verifySelfIsInBooth () =
        let applyEndRound state =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        MatchScenario.Command
                            state
                            "refresh-pool-cue"
                            MatchScenario.FirstPlayer
                            ImmutableArray<_>.Empty
                            MatchAction.EndRound
                    )
            )

        let boothed = applyEndRound (poolCueState CardZone.Booth)
        let active = applyEndRound (poolCueState CardZone.Oche)

        let hasBoost (state: MatchState) =
            state.Effects
            |> Seq.exists (fun effect ->
                effect.SourceEffect = EffectId "BLK-104-T01"
                && effect.SourceCard = CardInstanceId "pool-cue"
                && effect.Kind = TemporaryEffectKind.ScaleNextAttackDamage)

        hasBoost boothed |> should be True
        hasBoost active |> should be False

    let private sourceRankState defenderMechanicalId defenderVim =
        MatchScenario.BattleState "BLK-031" defenderMechanicalId [ "VIM-DODGY"; "VIM-SOBER" ] 1151UL
        |> fun state -> withAttachedVim state defenderId "reply-vim" defenderVim

    let private sourceRankReply (engine: MatchEngine) (state: MatchState) (replyEffect: string) =
        let protectedState = applyAttack engine state "BLK-031-B01"

        MatchScenario.Applied(
            engine.Apply(
                protectedState,
                attackCommand
                    protectedState
                    $"reply:{replyEffect}"
                    MatchScenario.SecondPlayer
                    defenderId
                    replyEffect
                    ImmutableArray<_>.Empty
            )
        )

    let private verifySourceIsRegular () =
        let regular = sourceRankState "BLK-124" [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]

        let seasoned = sourceRankState "BLK-134" [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]

        let engine = MatchScenario.Engine()
        let regularReply = sourceRankReply engine regular "BLK-124-B02"
        let seasonedReply = sourceRankReply engine seasoned "BLK-134-B02"

        regularReply.ActivePlayer |> should equal MatchScenario.FirstPlayer
        seasonedReply.ActivePlayer |> should equal MatchScenario.FirstPlayer
        (regularReply.Card attackerId).Damage |> should equal 0
        (seasonedReply.Card attackerId).Damage |> should equal 90

    let private targetDamageState damage =
        let boothTarget =
            { MatchScenario.PlainCard
                  "damaged-booth-target"
                  "BLK-006"
                  MatchScenario.SecondPlayer
                  CardZone.Booth
                  -1 with
                Damage = damage }

        let state =
            MatchScenario.BattleState
                "BLK-145"
                "BLK-003"
                [ "VIM-BEER"; "VIM-BEER"; "VIM-BEER" ]
                1153UL
            |> fun current -> MatchScenario.WithCards current [ boothTarget ]

        state, boothTarget

    let private applyTargetDamageAttack
        (engine: MatchEngine)
        (state: MatchState)
        (target: CardState)
        =
        match engine.Apply(state, MatchScenario.AttackCommand state "BLK-145-B01") with
        | CommandOutcome.Applied(applied, _) -> applied
        | CommandOutcome.Rejected(_, rejection) ->
            rejection.Code |> should equal CommandRejectionCode.ChoiceRequired
            let requirement = rejection.ChoiceRequirements |> Seq.exactlyOne

            MatchScenario.Applied(
                engine.Apply(
                    state,
                    MatchScenario.AttackCommandWith
                        state
                        "BLK-145-B01"
                        (ImmutableArray.Create(
                            EffectChoice.Cards(requirement.Id, ImmutableArray.Create target.Id)
                        ))
                )
            )

    let private verifyTargetHasDamage () =
        let damaged, damagedTarget = targetDamageState 10
        let undamaged, undamagedTarget = targetDamageState 0
        let engine = MatchScenario.Engine()
        let damagedResult = applyTargetDamageAttack engine damaged damagedTarget
        let undamagedResult = applyTargetDamageAttack engine undamaged undamagedTarget

        (damagedResult.Card defenderId).Damage |> should equal 120
        (undamagedResult.Card defenderId).Damage |> should equal 120
        (damagedResult.Card damagedTarget.Id).Damage |> should equal 100
        (undamagedResult.Card undamagedTarget.Id).Damage |> should equal 0

    let private kitTaxiState targetMechanicalId =
        let incoming =
            MatchScenario.PlainCard
                "kit-taxi-incoming"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let state =
            MatchScenario.BattleState
                targetMechanicalId
                "BLK-150"
                [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                1163UL

        let tool =
            MatchScenario.AttachedCard
                "fare-tool"
                "KIT-004"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                attackerId

        let outgoing = state.Card attackerId

        let outgoing =
            { outgoing with
                Attachments =
                    ImmutableArray.CreateRange(Seq.append outgoing.Attachments [ tool.Id ]) }

        MatchScenario.WithCards state [ outgoing; incoming; tool ]

    let private verifyTargetIsLandlord () =
        let landlord = kitTaxiState "BLK-012"
        let regular = kitTaxiState "BLK-004"

        taxiVimCount landlord (CardInstanceId "kit-taxi-incoming") |> should equal 0
        taxiVimCount regular (CardInstanceId "kit-taxi-incoming") |> should equal 1

    let private kitReductionState defenderMechanicalId =
        let state =
            MatchScenario.BattleState
                "BLK-001"
                defenderMechanicalId
                [ "VIM-BLAZED"; "VIM-SOBER" ]
                1171UL

        let defender = state.Card defenderId

        let tool =
            MatchScenario.AttachedCard
                "reduction-tool"
                "KIT-014"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                defender.Id

        MatchScenario.WithCards
            state
            [ { defender with
                  Attachments = ImmutableArray.Create tool.Id }
              tool ]

    let private verifyTargetIsSeasoned () =
        let seasoned = kitReductionState "BLK-028"
        let regular = kitReductionState "BLK-027"
        let engine = MatchScenario.Engine()
        let seasonedResult = applyAttack engine seasoned "BLK-001-B01"
        let regularResult = applyAttack engine regular "BLK-001-B01"

        (seasonedResult.Card defenderId).Damage |> should equal 10
        (regularResult.Card defenderId).Damage |> should equal 40

    let verify conditionName =
        match conditionName with
        | "AttachedVimCountsAreEqual" -> verifyAttachedVimCountsAreEqual ()
        | "BoothHasSpace" -> verifyBoothHasSpace ()
        | "FirstBeerMatIsBlankSide" -> verifyFirstBeerMatIsBlankSide ()
        | "MittCountsAreEqual" -> verifyMittCountsAreEqual ()
        | "NamedBlokeInBooth" -> verifyNamedBlokeInBooth ()
        | "NamedBlokeInPlay" -> verifyNamedBlokeInPlay ()
        | "OpenedSecond" -> verifyOpenedSecond ()
        | "Optional" -> verifyOptional ()
        | "OtherBoothExists" -> verifyOtherBoothExists ()
        | "OtherOcheHasDamage" -> verifyOtherOcheHasDamage ()
        | "OtherOcheHasMechanicalType" -> verifyOtherOcheHasMechanicalType ()
        | "OtherOcheHasRoughState" -> verifyOtherOcheHasRoughState ()
        | "OtherOcheIsBigHitter" -> verifyOtherOcheIsBigHitter ()
        | "OtherOcheIsPromoted" -> verifyOtherOcheIsPromoted ()
        | "OtherSentHomeByThisAttackDamage" -> verifyOtherSentHomeByThisAttackDamage ()
        | "OwnBarChitCountIsGreater" -> verifyOwnBarChitCountIsGreater ()
        | "OwnMittIsEmpty" -> verifyOwnMittIsEmpty ()
        | "PromotedFromMittThisRound" -> verifyPromotedFromMittThisRound ()
        | "SelfHasDamage" -> verifySelfHasDamage ()
        | "SelfHasRoughState" -> verifySelfHasRoughState ()
        | "SelfHasVim" -> verifySelfHasVim ()
        | "SelfIsInBooth" -> verifySelfIsInBooth ()
        | "SourceIsRegular" -> verifySourceIsRegular ()
        | "TargetHasDamage" -> verifyTargetHasDamage ()
        | "TargetIsLandlord" -> verifyTargetIsLandlord ()
        | "TargetIsSeasoned" -> verifyTargetIsSeasoned ()
        | other -> failwith $"Unhandled authority condition {other}."

type ConditionConformanceTests() =

    [<Test>]
    [<Arguments("AttachedVimCountsAreEqual")>]
    [<Arguments("BoothHasSpace")>]
    [<Arguments("FirstBeerMatIsBlankSide")>]
    [<Arguments("MittCountsAreEqual")>]
    [<Arguments("NamedBlokeInBooth")>]
    [<Arguments("NamedBlokeInPlay")>]
    [<Arguments("OpenedSecond")>]
    [<Arguments("Optional")>]
    [<Arguments("OtherBoothExists")>]
    [<Arguments("OtherOcheHasDamage")>]
    [<Arguments("OtherOcheHasMechanicalType")>]
    [<Arguments("OtherOcheHasRoughState")>]
    [<Arguments("OtherOcheIsBigHitter")>]
    [<Arguments("OtherOcheIsPromoted")>]
    [<Arguments("OtherSentHomeByThisAttackDamage")>]
    [<Arguments("OwnBarChitCountIsGreater")>]
    [<Arguments("OwnMittIsEmpty")>]
    [<Arguments("PromotedFromMittThisRound")>]
    [<Arguments("SelfHasDamage")>]
    [<Arguments("SelfHasRoughState")>]
    [<Arguments("SelfHasVim")>]
    [<Arguments("SelfIsInBooth")>]
    [<Arguments("SourceIsRegular")>]
    [<Arguments("TargetHasDamage")>]
    [<Arguments("TargetIsLandlord")>]
    [<Arguments("TargetIsSeasoned")>]
    member _.``every authority condition should select its true branch and reject its false branch``
        (conditionName: string)
        =
        ConditionConformanceScenarios.verify conditionName

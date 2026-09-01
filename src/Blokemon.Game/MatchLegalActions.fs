namespace Blokemon.Game

open System
open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchLegalChoices
open Blokemon.Game.MatchPlayingActions

/// Every action a player could plausibly submit in the current phase. The engine filters the list
/// down to the ones that are actually accepted; this module only has to propose.
module internal MatchLegalActions =

    let mulliganBonusActions (state: MatchState) (actor: PlayerId) =
        let player = state.Player actor

        if player.MulliganBonusChosen || player.MulliganBonusAllowance = 0 then
            Seq.empty
        else
            seq {
                for count in 0 .. player.MulliganBonusAllowance ->
                    { simple
                          LegalActionKind.ChooseMulliganBonus
                          state
                          actor
                          $"bonus:{actor.Value}:{count}"
                          (MatchAction.ChooseMulliganBonus count) with
                        StableKey = $"bonus:%03d{count}" }
            }

    let bonusPlacementActions (catalog: AuthorityCatalog) (state: MatchState) (actor: PlayerId) =
        let benchable = MatchRules.bonusBenchable catalog state actor

        if (state.Player actor).BonusPlacementChosen then
            Seq.empty
        else
            let room =
                catalog.Manifest.BaseRules.Opening.BoothLimit
                - (state.CardsIn(actor, CardZone.Booth) |> Seq.length)

            let requirement =
                ChoiceRequirement.create
                    (EffectChoiceId "bonus:booth")
                    ChoiceRequirementKind.Cards
                    actor
                    0
                    (min room benchable.Length)
                    (ImmutableArray.CreateRange(benchable |> Seq.map _.Id))
                    ImmutableArray<_>.Empty
                    ImmutableArray<_>.Empty
                    ValueNone

            Seq.singleton (
                legal
                    LegalActionKind.ChooseBonusPlacement
                    state
                    actor
                    $"bonusbooth:{actor.Value}"
                    "bonusbooth"
                    (ImmutableArray.Create requirement)
                    ImmutableArray<_>.Empty
                    (MatchAction.ChooseBonusPlacement ImmutableArray<_>.Empty)
            )

    let openingActions (catalog: AuthorityCatalog) (state: MatchState) (actor: PlayerId) =
        if
            (state.Player actor).OpeningChosen
            || not (MatchRules.mayPlaceOpening state actor)
        then
            Seq.empty
        else
            let regulars =
                state.CardsIn(actor, CardZone.Mitt)
                |> Seq.filter (fun card ->
                    card.Kind = CardKind.Bloke && catalog.IsRegular card.MechanicalId)
                |> Seq.toArray

            regulars
            |> Seq.map (fun oche ->
                let booth =
                    regulars
                    |> Seq.filter (fun card -> card.Id <> oche.Id)
                    |> Seq.map (fun card -> card.Id)

                let requirement =
                    ChoiceRequirement.create
                        (EffectChoiceId "opening:booth")
                        ChoiceRequirementKind.Cards
                        actor
                        0
                        (min catalog.Manifest.BaseRules.Opening.BoothLimit (regulars.Length - 1))
                        (ImmutableArray.CreateRange booth)
                        ImmutableArray<_>.Empty
                        ImmutableArray<_>.Empty
                        ValueNone

                legal
                    LegalActionKind.ChooseOpening
                    state
                    actor
                    $"opening:{actor.Value}:{oche.Id.Value}"
                    $"opening:{oche.Id.Value}"
                    (ImmutableArray.Create requirement)
                    ImmutableArray<_>.Empty
                    (MatchAction.ChooseOpening(oche.Id, ImmutableArray<_>.Empty)))

    let replacementActions (catalog: AuthorityCatalog) (state: MatchState) (actor: PlayerId) =
        if state.ReplacementPlayer <> ValueSome actor then
            Seq.empty
        else
            state.CardsIn(actor, CardZone.Booth)
            |> Seq.filter catalog.CountsAsPokemon
            |> Seq.map (fun card ->
                simple
                    LegalActionKind.ChooseReplacement
                    state
                    actor
                    $"replacement:{card.Id.Value}"
                    (MatchAction.ChooseReplacement card.Id))

    let effectChoiceActions (state: MatchState) (actor: PlayerId) =
        match state.PendingEffect with
        | ValueSome pending when pending.Chooser = actor ->
            Seq.singleton (
                legal
                    LegalActionKind.ResolveEffectChoice
                    state
                    actor
                    $"choice:{pending.Command.Id.Value}"
                    $"choice:{pending.Command.Id.Value}"
                    pending.Requirements
                    (stableChoices pending.Requirements)
                    MatchAction.ResolveEffectChoice
            )
        | _ -> Seq.empty

    let private knockoutTriggerActions (state: MatchState) (actor: PlayerId) =
        match state.PendingKnockout with
        | ValueSome pending when pending.Chooser = actor ->
            Seq.append (Seq.singleton ValueNone) (pending.EligibleVim |> Seq.map ValueSome)
            |> Seq.map (fun vim ->
                let suffix =
                    match vim with
                    | ValueSome value -> value.Value
                    | ValueNone -> "decline"

                let stableKey =
                    match vim with
                    | ValueSome value -> $"trigger:0:{value.Value}"
                    | ValueNone -> "trigger:1:decline"

                { simple
                      LegalActionKind.ResolveKnockoutTrigger
                      state
                      actor
                      $"trigger:{pending.TriggerEffect.Value}:{suffix}"
                      (MatchAction.ResolveKnockoutTrigger vim) with
                    StableKey = stableKey })
        | _ -> Seq.empty

    let private barChitTriggerActions (state: MatchState) (actor: PlayerId) =
        match state.PendingBarChits |> Seq.tryHead with
        | Some pending when pending.Player = actor ->
            [ true; false ]
            |> Seq.map (fun putOntoBooth ->
                let zone = if putOntoBooth then "booth" else "mitt"

                { simple
                      LegalActionKind.ResolveBarChitTrigger
                      state
                      actor
                      $"bar-chit:{pending.Card.Value}:{zone}"
                      (MatchAction.ResolveBarChitTrigger putOntoBooth) with
                    StableKey =
                        (if putOntoBooth then
                             "bar-chit:0:booth"
                         else
                             "bar-chit:1:mitt") })
        | _ -> Seq.empty

    let triggerChoiceActions (state: MatchState) (actor: PlayerId) =
        if state.PendingKnockout.IsSome then
            knockoutTriggerActions state actor
        else
            barChitTriggerActions state actor

    let resignAction (state: MatchState) (actor: PlayerId) =
        { simple LegalActionKind.Resign state actor $"resign:{actor.Value}" MatchAction.Resign with
            StableKey = "resign" }

    let proposed
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (state: MatchState)
        (actor: PlayerId)
        =
        match state.Phase with
        | MatchPhase.MulliganBonus -> mulliganBonusActions state actor
        | MatchPhase.OpeningPlacement -> openingActions catalog state actor
        | MatchPhase.BonusPlacement -> bonusPlacementActions catalog state actor
        | MatchPhase.Playing -> playingActions catalog interpreter state actor
        | MatchPhase.AwaitingEffectChoice -> effectChoiceActions state actor
        | MatchPhase.AwaitingTriggerChoice -> triggerChoiceActions state actor
        | MatchPhase.AwaitingReplacement -> replacementActions catalog state actor
        | MatchPhase.Complete -> Seq.empty
        | other -> failwithf "Unhandled match phase %A." other

    let private indexed (values: seq<'value>) =
        seq {
            let mutable index = 0L

            for value in values do
                yield index, value
                index <- index + 1L
        }

    let materializeWithIndex (state: MatchState) (action: LegalAction) =
        let variants =
            match action.Command.Action with
            | MatchAction.ChooseOpening(oche, _) ->
                action.ChoiceRequirements
                |> Seq.tryHead
                |> Option.map (fun requirement ->
                    cardSets
                        requirement.Minimum
                        requirement.Maximum
                        requirement.PreserveCardOrder
                        requirement.EligibleCards
                    |> Seq.map (fun booth ->
                        MatchAction.ChooseOpening(oche, booth), ImmutableArray<_>.Empty))
                |> Option.defaultValue Seq.empty
            | MatchAction.ChooseBonusPlacement _ ->
                action.ChoiceRequirements
                |> Seq.tryHead
                |> Option.map (fun requirement ->
                    cardSets
                        requirement.Minimum
                        requirement.Maximum
                        requirement.PreserveCardOrder
                        requirement.EligibleCards
                    |> Seq.map (fun booth ->
                        MatchAction.ChooseBonusPlacement booth, ImmutableArray<_>.Empty))
                |> Option.defaultValue Seq.empty
            | MatchAction.Taxi(booth, _) when action.Affordability = ActionAffordability.Payable ->
                let attachments =
                    state.Oche action.Command.Actor
                    |> ValueOption.map _.Attachments
                    |> ValueOption.defaultValue ImmutableArray<_>.Empty

                cardSets 0 attachments.Length false attachments
                |> Seq.map (fun payment ->
                    MatchAction.Taxi(booth, payment), ImmutableArray<_>.Empty)
            | current ->
                choiceCombinations action.ChoiceRequirements action.Command.Actor
                |> Seq.map (fun choices -> current, choices)

        variants
        |> indexed
        |> Seq.map (fun (index, (matchAction, choices)) ->
            index,
            { action with
                StableKey = $"{action.StableKey}:choice:%018d{index}"
                Command =
                    { action.Command with
                        Id = CommandId $"{action.Command.Id.Value}:choice:%018d{index}"
                        Choices = choices
                        Action = matchAction } })

    let tryMaterializeAt state action choiceIndex =
        materializeWithIndex state action
        |> Seq.tryFind (fun (index, _) -> index = choiceIndex)
        |> Option.map snd

    let order (actions: LegalAction seq) =
        actions
        |> Seq.sortWith (fun left right ->
            let byKind = compare left.Kind right.Kind

            if byKind <> 0 then
                byKind
            else
                String.CompareOrdinal(left.StableKey, right.StableKey))

    let cpuOrder (actions: LegalAction seq) =
        actions
        |> Seq.sortWith (fun left right ->
            let priority (action: LegalAction) =
                if action.Kind = LegalActionKind.EndRound then 0 else 1

            let byPriority = compare (priority left) (priority right)

            if byPriority <> 0 then
                byPriority
            else
                let byKind = compare left.Kind right.Kind

                if byKind <> 0 then
                    byKind
                else
                    String.CompareOrdinal(left.StableKey, right.StableKey))

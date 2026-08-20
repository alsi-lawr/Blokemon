namespace Blokemon.App

open System
open System.Linq
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.MatchCardProjection
open Blokemon.App.MatchFailures
open Blokemon.App.MatchIdentity
open Blokemon.App.MatchLabels
open Blokemon.Game

/// The MatchView the client draws: each legal action with its choice requirements, the attack
/// row, both sides of the table, and the frame that holds them.
module internal MatchViewProjection =

    let requirementView
        (catalogue: BlokemonCatalogue)
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (requirement: ChoiceRequirement)
        =
        let cardInstance = cardInstance catalogue

        // A candidate is offered to the chooser precisely because the effect entitles them to see
        // it: an effect that asks you to pick a card from your opponent's hand reveals that hand
        // to you first. So eligibility is the entitlement, and the candidates are not filtered
        // again for visibility - only for whether this viewer is the chooser at all.
        let exposeOptions = requirement.Chooser = human

        let dependsOnOptional: string | null =
            match requirement.DependsOnOptional with
            | ValueSome parent -> parent.Value
            | ValueNone -> null

        MatchChoiceRequirementView(
            requirement.Id.Value,
            choiceKind requirement.Kind,
            requirementLabel requirement,
            MatchChooserView(
                requirement.Chooser.Value,
                playerName requirement.Chooser human displayName,
                requirement.Chooser = human
            ),
            requirement.Minimum,
            requirement.Maximum,
            (if exposeOptions then
                 requirement.EligibleCards
                 |> Seq.map (cardInstance state human displayName)
                 |> Seq.toArray
             else
                 Array.empty),
            (if exposeOptions then
                 requirement.EligibleMechanicalTypes
                 |> Seq.map (fun cardType ->
                     MatchMechanicalTypeOptionView(
                         cardType.ToString(),
                         humanize (cardType.ToString())
                     ))
                 |> Seq.toArray
             else
                 Array.empty),
            (if exposeOptions then
                 requirement.EligibleEffects
                 |> Seq.map (fun effect ->
                     MatchEffectOptionView(effect.Value, catalogue.EffectName effect.Value))
                 |> Seq.toArray
             else
                 Array.empty),
            dependsOnOptional,
            (if exposeOptions then
                 requirement.EligibleTargets
                 |> Seq.map (cardInstance state human displayName)
                 |> Seq.toArray
             else
                 Array.empty),
            requirement.RequireDifferentMechanicalTypes,
            (if exposeOptions then
                 requirement.EligibleCardTypes
                 |> Seq.map (fun card ->
                     MatchCardTypesView(
                         card.Card.Value,
                         card.Types
                         |> Seq.map (fun cardType -> humanize (cardType.ToString()))
                         |> Seq.toArray
                     ))
                 |> Seq.toArray
             else
                 Array.empty)
        )

    let actionView
        (catalogue: BlokemonCatalogue)
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (action: LegalAction)
        =
        let actionLabel = actionLabel catalogue
        let requirementView = requirementView catalogue

        let subject = actionSubject action.Command

        MatchActionView(
            action.StableKey,
            actionKind action.Kind,
            actionLabel state action.Command,
            (match action.Kind with
             | LegalActionKind.ChooseOpening
             | LegalActionKind.Attack
             | LegalActionKind.ResolveEffectChoice
             | LegalActionKind.ResolveKnockoutTrigger
             | LegalActionKind.ResolveBarChitTrigger -> true
             | _ -> false),
            subject.Source,
            subject.Target,
            subject.Effect,
            action.ChoiceRequirements
            |> Seq.map (requirementView state human displayName)
            |> Seq.toArray,
            actionDisabledReason action
        )

    let attacks
        (catalogue: BlokemonCatalogue)
        (state: MatchState)
        (human: PlayerId)
        (legalActions: LegalAction seq)
        =
        let energyLabel = energyLabel catalogue

        match state.Oche human with
        | ValueNone -> Array.empty
        | ValueSome active when active.Kind <> CardKind.Bloke -> Array.empty
        | ValueSome active ->
            let legal =
                legalActions
                |> Seq.choose (fun action ->
                    match action.Command.Action with
                    | MatchAction.Attack(_, attackId) -> Some(attackId.Value, action.StableKey)
                    | _ -> None)
                |> dict

            let mechanical =
                catalogue.Mechanics.Collectibles.Single(fun card ->
                    String.Equals(card.Id, active.MechanicalId.Value, StringComparison.Ordinal))

            mechanical.Attacks
            |> Array.map (fun attack ->
                let actionId: string | null =
                    match legal.TryGetValue attack.MechanicalId with
                    | true, value -> value
                    | _ -> null

                let disabledReason: string | null =
                    if isNull actionId then
                        attackDisabledReason state human
                    else
                        null

                MatchAttackView(
                    active.Id.Value,
                    attack.MechanicalId,
                    catalogue.EffectName attack.MechanicalId,
                    attack.VimCost |> Array.map energyLabel,
                    attack.PrintedDamage,
                    actionId,
                    disabledReason
                ))

    let side
        (context: MatchContext)
        (state: MatchState)
        (player: PlayerId)
        (human: PlayerId)
        (name: string)
        (deckName: string)
        (exposeHand: bool)
        =
        let cardInstance = cardInstance context.Catalogue
        let engine = context.Engine

        let active = state.Oche player

        MatchSideView(
            name,
            deckName,
            state.CardsIn(player, CardZone.Stack).Count(),
            state.CardsIn(player, CardZone.Mitt).Count(),
            (state.Player player).BarChitsRemaining,
            (match active with
             | ValueNone -> null
             | ValueSome card -> cardInstance state human name card.Id),
            state.CardsIn(player, CardZone.Booth)
            |> Seq.map (fun card -> cardInstance state human name card.Id)
            |> Seq.toArray,
            (if exposeHand then
                 state.CardsIn(player, CardZone.Mitt)
                 |> Seq.map (fun card -> cardInstance state human name card.Id)
                 |> Seq.toArray
             else
                 Array.empty),
            state.CardsIn(player, CardZone.Local)
            |> Seq.map (fun card -> cardInstance state human name card.Id)
            |> Seq.toArray,
            // Resignation is always available, so it cannot decide whose turn it is; neither can a
            // move the player has no way to pay for.
            engine
                .GetLegalActions(state, player)
                .Any(fun action ->
                    action.Kind <> LegalActionKind.Resign
                    && action.Affordability = ActionAffordability.Payable)
        )

    let frame
        (context: MatchContext)
        (document: MatchDocument)
        (state: MatchState)
        (displayName: string)
        =
        let playerDeckName = playerDeckName context.Catalogue
        let cpuDeckName = cpuDeckName context.Catalogue
        let side = side context

        let human = document.Start.FirstDeck.Owner
        let cpuSide = document.Start.SecondDeck.Owner

        let winner: string | null =
            match state.Winner with
            | ValueSome value -> playerName value human displayName
            | ValueNone -> null

        MatchFrameView(
            Guid.Parse state.Id.Value,
            state.Revision.Value,
            state.RoundNumber,
            phaseLabel state.Phase,
            side state cpuSide human cpuName (cpuDeckName document.Start.SecondDeck) false,
            side state human human displayName (playerDeckName document.StartCommand.DeckId) true,
            state.Phase = MatchPhase.Complete,
            winner
        )

    let toView (context: MatchContext) (loaded: LoadedMatch) (displayName: string) =
        let engine = context.Engine
        let frame = frame context
        let actionView = actionView context.Catalogue
        let attacks = attacks context.Catalogue
        let eventLabel = eventLabel context.Catalogue

        let human = loaded.Document.Start.FirstDeck.Owner
        let legalActions = engine.GetLegalActions(loaded.State, human)

        MatchView(
            frame loaded.Document loaded.State displayName,
            legalActions
            |> Seq.map (actionView loaded.State human displayName)
            |> Seq.toArray,
            attacks loaded.State human legalActions,
            loaded.Events
                .Where(isPublicEvent)
                .TakeLast(16)
                .Select(fun matchEvent -> eventLabel loaded.State human displayName matchEvent)
                .ToArray()
        )

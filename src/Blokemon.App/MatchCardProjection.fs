namespace Blokemon.App

open System
open System.Collections.Generic
open System.Diagnostics
open System.Linq
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.MatchIdentity
open Blokemon.App.MatchLabels
open Blokemon.Core.SetDesign
open Blokemon.Game

/// What the catalogue calls the things in a match: deck names, energy labels, card instances and
/// the sentences an action or an event reads as.
module internal MatchCardProjection =

    let playerDeckName (catalogue: BlokemonCatalogue) (deckId: Guid) =
        match
            catalogue.StarterDecks.Decks.SingleOrDefault(fun deck -> deck.SavedDeckId = deckId)
        with
        | null -> "Custom deck"
        | deck -> deck.Name

    let cpuDeckName (catalogue: BlokemonCatalogue) (snapshot: FrozenDeckSnapshot) =
        let quantities = Dictionary<string, int>(StringComparer.Ordinal)

        for card in snapshot.Cards do
            match quantities.TryGetValue card.Value with
            | true, count -> quantities[card.Value] <- count + 1
            | _ -> quantities[card.Value] <- 1

        match
            catalogue.StarterDecks.Decks.SingleOrDefault(fun deck ->
                deck.Entries.Count = quantities.Count
                && deck.Entries
                   |> Seq.forall (fun entry ->
                       quantities.GetValueOrDefault(entry.CardId, 0) = entry.Quantity))
        with
        | null -> "Starter deck"
        | deck -> deck.Name

    let energyLabel (catalogue: BlokemonCatalogue) (cardType: BlokemonMechanicalType) =
        match
            catalogue.Mechanics.ApprovedMechanicalDisplayMap
            |> Array.tryFind (fun entry -> entry.MechanicalType = cardType)
        with
        | Some entry -> entry.ApprovedLabel.ToString()
        | None -> humanize (cardType.ToString())

    let cardName (catalogue: BlokemonCatalogue) (state: MatchState) (card: CardInstanceId) =
        catalogue.Card((state.Card card).MechanicalId.Value).Name

    let cardInstance
        (catalogue: BlokemonCatalogue)
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (cardId: CardInstanceId)
        =
        let card = state.Card cardId

        MatchCardInstanceView(
            card.Id.Value,
            catalogue.Card card.MechanicalId.Value,
            playerName card.Owner human displayName,
            humanize (card.Zone.ToString()),
            card.Damage,
            catalogue.StayingPower card.MechanicalId.Value,
            card.Attachments
            |> Seq.map state.Card
            |> Seq.filter (fun attachment -> attachment.Kind = CardKind.Vim)
            |> Seq.map (fun attachment -> catalogue.Card attachment.MechanicalId.Value)
            |> Seq.toArray,
            card.Attachments
            |> Seq.map state.Card
            |> Seq.filter (fun attachment -> attachment.Kind = CardKind.Kit)
            |> Seq.map (fun attachment -> catalogue.Card attachment.MechanicalId.Value)
            |> Seq.toArray,
            card.UnderlyingCards
            |> Seq.map state.Card
            |> Seq.map (fun underlying -> catalogue.Card underlying.MechanicalId.Value)
            |> Seq.toArray,
            card.RoughStates
            |> Seq.map (fun rough -> humanize (rough.State.ToString()))
            |> Seq.toArray
        )

    let actionSubject (command: MatchCommand) : ActionSubjectView =
        let subject (source: string | null) (target: string | null) (effect: string | null) =
            { Source = source
              Target = target
              Effect = effect }

        match command.Action with
        | MatchAction.ChooseOpening(oche, _) -> subject oche.Value null null
        | MatchAction.AttachVim(vim, bloke) -> subject vim.Value bloke.Value null
        | MatchAction.PlayBloke bloke -> subject bloke.Value null null
        | MatchAction.Promote(promotion, promoted) -> subject promotion.Value promoted.Value null
        | MatchAction.PlayKit(kit, target) ->
            subject
                kit.Value
                (match target with
                 | ValueSome value -> value.Value
                 | ValueNone -> null)
                null
        | MatchAction.Taxi(boothBloke, _) -> subject boothBloke.Value null null
        | MatchAction.UsePartyTrick(source, effect) -> subject source.Value null effect.Value
        | MatchAction.Attack(attacker, attackId) -> subject attacker.Value null attackId.Value
        | MatchAction.ChuckFossil fossil -> subject fossil.Value null null
        | MatchAction.ChooseReplacement replacement -> subject replacement.Value null null
        | MatchAction.ResolveKnockoutTrigger vim ->
            subject
                (match vim with
                 | ValueSome value -> value.Value
                 | ValueNone -> null)
                null
                null
        | MatchAction.ChooseMulliganBonus _
        | MatchAction.EndRound
        | MatchAction.ResolveEffectChoice
        | MatchAction.ResolveBarChitTrigger _
        | MatchAction.Resign -> subject null null null

    let actionLabel (catalogue: BlokemonCatalogue) (state: MatchState) (command: MatchCommand) =
        let cardName = cardName catalogue

        match command.Action with
        | MatchAction.ChooseMulliganBonus cardsToDraw ->
            if cardsToDraw = 0 then
                "Draw no extra cards"
            else
                $"""Draw {cardsToDraw} extra {if cardsToDraw = 1 then "card" else "cards"}"""
        | MatchAction.ChooseOpening(oche, _) -> $"Make {cardName state oche} your Active Blokemon"
        | MatchAction.AttachVim(vim, bloke) ->
            $"Attach {cardName state vim} to {cardName state bloke}"
        | MatchAction.PlayBloke bloke -> $"Play {cardName state bloke} to the Bench"
        | MatchAction.Promote(promotion, promoted) ->
            $"Evolve {cardName state promoted} into {cardName state promotion}"
        | MatchAction.PlayKit(kit, _) -> $"Play {cardName state kit}"
        | MatchAction.Taxi(boothBloke, _) -> $"Retreat to {cardName state boothBloke}"
        | MatchAction.UsePartyTrick(_, effect) -> catalogue.EffectName effect.Value
        | MatchAction.Attack(_, attackId) -> $"Attack with {catalogue.EffectName attackId.Value}"
        | MatchAction.ChuckFossil fossil -> $"Discard {cardName state fossil}"
        | MatchAction.EndRound -> "End the turn"
        | MatchAction.ChooseReplacement replacement ->
            $"Move {cardName state replacement} from the Bench to Active"
        // A decision the match poses is not a move anyone chose, so it has no name of its own to
        // take. It is named by the situation it puts the player in - the same word the band across
        // the middle of the table uses for it - rather than by telling them to act.
        | MatchAction.ResolveEffectChoice -> "Choosing"
        | MatchAction.ResolveKnockoutTrigger vim ->
            match vim with
            | ValueSome value -> $"Attach {cardName state value}"
            | ValueNone -> "Do not attach Energy"
        | MatchAction.ResolveBarChitTrigger putOntoBooth ->
            if putOntoBooth then
                "Put the card on the Bench"
            else
                "Put the card in your Hand"
        | MatchAction.Resign -> "Resign the battle"

    let eventLabel
        (catalogue: BlokemonCatalogue)
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (matchEvent: MatchEvent)
        =
        let cardName = cardName catalogue
        let actionLabel = actionLabel catalogue

        let actor =
            match matchEvent.Actor with
            | ValueSome value -> playerName value human displayName
            | ValueNone -> "The match"

        match matchEvent.Kind with
        | MatchEventKind.MatchStarted -> "The battle started."
        | MatchEventKind.CommandApplied ->
            match matchEvent.Command with
            | ValueSome command ->
                match command.Action with
                | MatchAction.PlayKit(kit, ValueSome target) ->
                    $"{actor}: Attached {cardName state kit} to {cardName state target}."
                // The log says what happened, and what happened was that the decision the match
                // posed got its answer - the decision itself is machinery with no name to print.
                | MatchAction.ResolveEffectChoice -> $"{actor} made a choice."
                | _ -> $"{actor}: {actionLabel state command}."
            | ValueNone -> raise (UnreachableException())
        | MatchEventKind.CardsShuffled -> $"{actor} shuffled the Deck."
        | MatchEventKind.CardsDrawn ->
            $"""{actor} drew {matchEvent.Amount} {if matchEvent.Amount = 1 then "card" else "cards"}."""
        | MatchEventKind.CardsRevealed when
            matchEvent.TargetCards.Length > 0
            && matchEvent.TargetCards
               |> Seq.forall (fun card -> (state.Card card).Zone = CardZone.BarChit)
            ->
            $"{actor} looked at their Prize Cards."
        | MatchEventKind.CardsRevealed ->
            $"""{actor} revealed {matchEvent.TargetCards.Length} {if matchEvent.TargetCards.Length = 1 then
                                                                      "card"
                                                                  else
                                                                      "cards"}."""
        | MatchEventKind.BeerMatTossed ->
            let landed =
                if matchEvent.BadgeSide = ValueSome true then
                    "badge"
                else
                    "blank"

            $"The coin landed on {landed}."
        | MatchEventKind.DamagePlaced -> $"{actor} did {matchEvent.Amount} damage."
        | MatchEventKind.DamageHealed -> $"{actor} healed {matchEvent.Amount} damage."
        | MatchEventKind.RoughStateApplied ->
            $"{humanize (matchEvent.RoughState.Value.ToString())} started."
        | MatchEventKind.RoughStateCleared ->
            $"{humanize (matchEvent.RoughState.Value.ToString())} ended."
        | MatchEventKind.BarChitsTaken ->
            $"""{actor} took {matchEvent.Amount} Prize {if matchEvent.Amount = 1 then "Card" else "Cards"}."""
        | MatchEventKind.RoundStarted -> $"{actor}'s turn started."
        | MatchEventKind.RoundEnded -> $"{actor} ended the turn."
        | MatchEventKind.BlokeSentHome -> "A Blokemon was Knocked Out."
        | MatchEventKind.AttackDeclared when matchEvent.Effect.IsSome ->
            $"{actor} used {catalogue.EffectName matchEvent.Effect.Value.Value}."
        | MatchEventKind.AttackDeclared -> $"{actor} attacked."
        | MatchEventKind.AttackCancelled -> "The attack stopped."
        | MatchEventKind.SuddenDeathStarted -> "Sudden death started."
        | MatchEventKind.MatchWon -> $"{actor} won the battle."
        | _ -> raise (UnreachableException())

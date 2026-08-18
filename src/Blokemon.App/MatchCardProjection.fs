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
        // A Game union, so this stays visitor-style until Blokemon.Game migrates in slice 7.
        command.Match<ActionSubjectView>(
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null }),
            (fun opening ->
                { Source = opening.Oche.Value
                  Target = null
                  Effect = null }),
            (fun attach ->
                { Source = attach.Vim.Value
                  Target = attach.Bloke.Value
                  Effect = null }),
            (fun play ->
                { Source = play.Bloke.Value
                  Target = null
                  Effect = null }),
            (fun promote ->
                { Source = promote.Promotion.Value
                  Target = promote.Bloke.Value
                  Effect = null }),
            (fun kit ->
                { Source = kit.Kit.Value
                  Target = if kit.Target.HasValue then kit.Target.Value.Value else null
                  Effect = null }),
            (fun taxi ->
                { Source = taxi.BoothBloke.Value
                  Target = null
                  Effect = null }),
            (fun trick ->
                { Source = trick.Source.Value
                  Target = null
                  Effect = trick.Effect.Value }),
            (fun attack ->
                { Source = attack.Attacker.Value
                  Target = null
                  Effect = attack.AttackId.Value }),
            (fun fossil ->
                { Source = fossil.Fossil.Value
                  Target = null
                  Effect = null }),
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null }),
            (fun replacement ->
                { Source = replacement.BoothBloke.Value
                  Target = null
                  Effect = null }),
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null }),
            (fun knockout ->
                { Source =
                    if knockout.Vim.HasValue then
                        knockout.Vim.Value.Value
                    else
                        null
                  Target = null
                  Effect = null }),
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null }),
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null })
        )

    let actionLabel (catalogue: BlokemonCatalogue) (state: MatchState) (command: MatchCommand) =
        let cardName = cardName catalogue

        // A Game union, so this stays visitor-style until Blokemon.Game migrates in slice 7.
        command.Match(
            (fun bonus ->
                if bonus.CardsToDraw = 0 then
                    "Draw no extra cards"
                else
                    $"""Draw {bonus.CardsToDraw} extra {if bonus.CardsToDraw = 1 then "card" else "cards"}"""),
            (fun opening -> $"Make {cardName state opening.Oche} your Active Blokemon"),
            (fun attach -> $"Attach {cardName state attach.Vim} to {cardName state attach.Bloke}"),
            (fun play -> $"Play {cardName state play.Bloke} to the Bench"),
            (fun promote ->
                $"Evolve {cardName state promote.Bloke} into {cardName state promote.Promotion}"),
            (fun kit -> $"Play {cardName state kit.Kit}"),
            (fun taxi -> $"Retreat to {cardName state taxi.BoothBloke}"),
            (fun trick -> catalogue.EffectName trick.Effect.Value),
            (fun attack -> $"Attack with {catalogue.EffectName attack.AttackId.Value}"),
            (fun fossil -> $"Discard {cardName state fossil.Fossil}"),
            (fun _ -> "End the turn"),
            (fun replacement ->
                $"Move {cardName state replacement.BoothBloke} from the Bench to Active"),
            (fun _ -> "Make the required choice"),
            (fun knockout ->
                if knockout.Vim.HasValue then
                    $"Attach {cardName state knockout.Vim.Value}"
                else
                    "Do not attach Energy"),
            (fun barChit ->
                if barChit.PutOntoBooth then
                    "Put the card on the Bench"
                else
                    "Put the card in your Hand"),
            (fun _ -> "Resign the battle")
        )

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
            if matchEvent.Actor.HasValue then
                playerName matchEvent.Actor.Value human displayName
            else
                "The match"

        match matchEvent.Kind with
        | MatchEventKind.MatchStarted -> "The battle started."
        | MatchEventKind.CommandApplied ->
            match matchEvent.Command with
            | :? MatchCommand.PlayKit as playKit when playKit.Target.HasValue ->
                $"{actor}: Attached {cardName state playKit.Kit} to {cardName state playKit.Target.Value}."
            | null -> raise (UnreachableException())
            | command -> $"{actor}: {actionLabel state command}."
        | MatchEventKind.CardsShuffled -> $"{actor} shuffled the Deck."
        | MatchEventKind.CardsDrawn ->
            $"""{actor} drew {matchEvent.Amount} {if matchEvent.Amount = 1 then "card" else "cards"}."""
        | MatchEventKind.CardsRevealed when
            matchEvent.TargetCards.Count > 0
            && matchEvent.TargetCards
               |> Seq.forall (fun card -> (state.Card card).Zone = CardZone.BarChit)
            ->
            $"{actor} looked at their Prize Cards."
        | MatchEventKind.CardsRevealed ->
            $"""{actor} revealed {matchEvent.TargetCards.Count} {if matchEvent.TargetCards.Count = 1 then "card" else "cards"}."""
        | MatchEventKind.BeerMatTossed ->
            let landed =
                if matchEvent.BadgeSide.HasValue && matchEvent.BadgeSide.Value then
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
        | MatchEventKind.AttackDeclared when matchEvent.Effect.HasValue ->
            $"{actor} used {catalogue.EffectName matchEvent.Effect.Value.Value}."
        | MatchEventKind.AttackDeclared -> $"{actor} attacked."
        | MatchEventKind.AttackCancelled -> "The attack stopped."
        | MatchEventKind.SuddenDeathStarted -> "Sudden death started."
        | MatchEventKind.MatchWon -> $"{actor} won the battle."
        | _ -> raise (UnreachableException())

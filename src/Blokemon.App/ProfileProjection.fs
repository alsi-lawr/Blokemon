namespace Blokemon.App

open System
open System.Collections.Generic
open System.Linq
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.ProfileFailures
open Blokemon.Core.SetDesign
open Blokemon.Product

/// The card, deck and starter-deck views the client draws, each read against the catalogue the
/// profile was restored under.
module internal ProfileProjection =

    // CardView is an App.Contracts C# record, so F# cannot copy-and-update it (FS0786): the
    // owned-quantity overlay is written as an explicit construction.
    let currentCard
        (catalogue: BlokemonCatalogue)
        (id: string)
        (ownership: IReadOnlyDictionary<string, int>)
        (currentCards: IReadOnlyDictionary<string, CardView>)
        =
        match currentCards.TryGetValue id with
        | true, current ->
            CardView(
                current.Id,
                current.Name,
                current.Kind,
                current.Type,
                current.Detail,
                current.FaceHtml,
                current.Rules,
                ownership.GetValueOrDefault(id, 0),
                current.FreelyAvailable
            )
        | _ ->
            CardView(
                id,
                "Unavailable card",
                CardKindView.Blokemon,
                "Historical",
                "Not in the current card set",
                catalogue.ReverseFaceHtml,
                Array.empty,
                ownership.GetValueOrDefault(id, 0),
                false
            )

    let deckWarnings (catalogue: BlokemonCatalogue) (deck: SavedDeck) =
        let includedEnergy =
            deck.Cards.Keys
            |> Seq.map _.Value
            |> Seq.filter (fun id -> id.StartsWith("VIM-", StringComparison.Ordinal))
            |> Seq.map (fun id ->
                catalogue.Mechanics.BasicVim.Single(fun card ->
                    String.Equals(card.Id, id, StringComparison.Ordinal)))
            |> Seq.map _.MechanicalType
            |> HashSet

        if includedEnergy.Count = 0 then
            [| "This deck has no Basic Energy. Its Blokemon cannot attack." |]
        else
            let hasPayableAttack =
                deck.Cards.Keys
                |> Seq.map _.Value
                |> Seq.filter (fun id -> id.StartsWith("BLK-", StringComparison.Ordinal))
                |> Seq.map (fun id ->
                    catalogue.Mechanics.Collectibles.Single(fun card ->
                        String.Equals(card.Id, id, StringComparison.Ordinal)))
                |> Seq.collect _.Attacks
                |> Seq.exists (fun attack ->
                    attack.VimCost
                    |> Array.forall (fun cost ->
                        cost = BlokemonMechanicalType.Colorless || includedEnergy.Contains cost))

            if hasPayableAttack then
                Array.empty
            else
                [| "The Basic Energy in this deck cannot pay for an attack." |]

    let deckView
        (catalogue: BlokemonCatalogue)
        (profile: LocalProfile)
        (deck: SavedDeck)
        (deckId: Guid)
        =
        let deckWarnings = deckWarnings catalogue

        let validation =
            DeckValidator.Validate
                profile
                catalogue.Mechanics
                (deck.Cards
                 |> Seq.map (fun entry ->
                     { CardId = entry.Key
                       Quantity = entry.Value }))

        let issues =
            match validation with
            | DeckValidationResult.Invalid invalid -> invalid |> Seq.map deckIssue |> Seq.toArray
            | DeckValidationResult.Valid _ -> Array.empty

        let warnings = if issues.Length = 0 then deckWarnings deck else Array.empty

        DeckView(
            deckId,
            deck.Name.Value,
            deck.Revision.Value,
            deck.Cards
                .OrderBy((fun entry -> entry.Key.Value), StringComparer.Ordinal)
                .Select(fun entry -> DeckEntryView(entry.Key.Value, entry.Value))
                .ToArray(),
            issues.Length = 0,
            issues,
            warnings
        )

    let starterViews
        (catalogue: BlokemonCatalogue)
        (claimedIds: IReadOnlySet<string>)
        (cards: IReadOnlyCollection<CardView>)
        =
        let currentCards = Dictionary<string, CardView>(StringComparer.Ordinal)

        for card in cards do
            currentCards.Add(card.Id, card)

        catalogue.StarterDecks.Decks
            .OrderBy((fun deck -> deck.Id), StringComparer.Ordinal)
            .Select(fun deck ->
                StarterDeckView(
                    deck.Id,
                    deck.Name,
                    deck.Type,
                    deck.Role,
                    deck.Description,
                    currentCards[deck.LeaderCardId],
                    deck.Entries
                    |> Seq.map (fun entry -> DeckEntryView(entry.CardId, entry.Quantity))
                    |> Seq.toArray,
                    deck.Entries
                    |> Seq.filter (fun entry ->
                        currentCards[entry.CardId].Kind = CardKindView.Blokemon)
                    |> Seq.sumBy _.Quantity,
                    deck.Entries
                    |> Seq.filter (fun entry ->
                        currentCards[entry.CardId].Kind = CardKindView.Trainer)
                    |> Seq.sumBy _.Quantity,
                    deck.Entries
                    |> Seq.filter (fun entry ->
                        currentCards[entry.CardId].Kind = CardKindView.Energy)
                    |> Seq.sumBy _.Quantity,
                    claimedIds.Contains deck.Id
                ))
            .ToArray()

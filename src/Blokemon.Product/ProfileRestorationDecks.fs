namespace Blokemon.Product

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Product.ProfileRestorationSteps

/// Rebuilding the decks a persisted profile saved. A deck saved under the current authority
/// is re-validated against the ownership the rest of the restoration already established;
/// a deck saved under an older one is only checked for entries it could never have held.
module internal ProfileRestorationDecks =

    let private restoreDeckCard
        (deckPath: string)
        (selections: DeckCardSelection list, deckCards: Map<CardId, int>)
        (cardIndex: int)
        (card: SavedDeckCardSnapshot)
        =
        let path = $"{deckPath}.Cards[{cardIndex}]"

        result {
            do! failWhen (isMissing card) (LocalProfileRestorationFailure.MissingEntry path)

            do!
                failWhen
                    (card.Quantity < 0)
                    (LocalProfileRestorationFailure.NegativeQuantity(
                        $"{path}.Quantity",
                        card.Quantity
                    ))

            let! cardId = atPath $"{path}.CardId" (CardId.Create card.CardId)

            do!
                failWhen
                    (deckCards.ContainsKey cardId)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.DeckCardId,
                        cardId.Value
                    ))

            return
                ({ CardId = cardId
                   Quantity = card.Quantity }
                : DeckCardSelection)
                :: selections,
                deckCards.Add(cardId, card.Quantity)
        }

    let private restoreDeck
        (baseState: LocalProfileState)
        (currentAuthority: BlokemonRuntimeManifest)
        (isCurrentAuthority: bool)
        (unavailableHistoricalCardIds: Set<CardId>)
        (path: string)
        (item: SavedDeckSnapshot)
        : DomainResult<SavedDeck, LocalProfileRestorationFailure> =
        result {
            do! failWhen (isMissing item) (LocalProfileRestorationFailure.MissingEntry path)
            let! deckId = atPath $"{path}.DeckId" (DeckId.Create item.DeckId)

            let! deckName =
                match DeckName.Create item.Name with
                | DomainResult.Succeeded value -> DomainResult.Succeeded value
                | DomainResult.Failed failure ->
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidDeckName(deckId, failure)
                    )

            let! revision =
                match DeckRevision.Create item.Revision with
                | DomainResult.Succeeded value -> DomainResult.Succeeded value
                | DomainResult.Failed _ ->
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidDeckRevision(deckId, item.Revision)
                    )

            let! reversedSelections, deckCards =
                foldIndexed (restoreDeckCard path) ([], Map.empty) (orEmpty item.Cards)

            let selections = List.rev reversedSelections

            let authorityCardIds =
                Seq.concat
                    [ currentAuthority.Collectibles |> Seq.map _.Id
                      currentAuthority.Kits |> Seq.map _.Id
                      currentAuthority.BasicVim |> Seq.map _.Id ]
                |> Set.ofSeq

            let unprovenUnknownCards =
                selections
                |> List.filter (fun selection ->
                    not (authorityCardIds.Contains selection.CardId.Value)
                    && not (unavailableHistoricalCardIds.Contains selection.CardId))

            let containsUnavailableHistoricalCard =
                selections
                |> List.exists (fun selection ->
                    unavailableHistoricalCardIds.Contains selection.CardId
                    && not (authorityCardIds.Contains selection.CardId.Value))

            if isCurrentAuthority && not unprovenUnknownCards.IsEmpty then
                let issues =
                    unprovenUnknownCards
                    |> List.map (fun selection -> DeckValidationIssue.UnknownCard selection.CardId)
                    |> ImmutableArray.CreateRange

                return!
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidSavedDeck(deckId, issues)
                    )
            elif isCurrentAuthority && not containsUnavailableHistoricalCard then
                match
                    DeckRules.validate
                        baseState.OwnedQuantity
                        currentAuthority
                        (List.toSeq selections)
                with
                | DeckValidationResult.Invalid issues ->
                    return!
                        DomainResult.Failed(
                            LocalProfileRestorationFailure.InvalidSavedDeck(deckId, issues)
                        )
                | DeckValidationResult.Valid validDeck ->
                    return SavedDeck(deckId, deckName, revision, validDeck.Cards)
            elif selections |> List.exists (fun selection -> selection.Quantity = 0) then
                let issues =
                    selections
                    |> List.filter (fun selection -> selection.Quantity = 0)
                    |> List.map (fun selection ->
                        DeckValidationIssue.QuantityMustBePositive(
                            selection.CardId,
                            selection.Quantity
                        ))
                    |> ImmutableArray.CreateRange

                return!
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidSavedDeck(deckId, issues)
                    )
            else
                return
                    SavedDeck(deckId, deckName, revision, ImmutableDictionary.CreateRange deckCards)
        }

    let restoreDeckAt
        (baseState: LocalProfileState)
        (currentAuthority: BlokemonRuntimeManifest)
        (isCurrentAuthority: bool)
        (unavailableHistoricalCardIds: Set<CardId>)
        (decks: Map<DeckId, SavedDeck>)
        (deckIndex: int)
        (item: SavedDeckSnapshot)
        =
        result {
            let! deck =
                restoreDeck
                    baseState
                    currentAuthority
                    isCurrentAuthority
                    unavailableHistoricalCardIds
                    $"SavedDecks[{deckIndex}]"
                    item

            do!
                failWhen
                    (decks.ContainsKey deck.Id)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.DeckId,
                        deck.Id.Value
                    ))

            return decks.Add(deck.Id, deck)
        }

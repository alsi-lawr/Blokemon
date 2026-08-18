namespace Blokemon.Product

open System
open System.Collections.Generic
open Blokemon.Core.SetDesign

/// Saving, revising and removing the decks a profile keeps.
module internal ProfileDecks =

    let private saveDeck
        (state: LocalProfileState)
        (deckId: DeckId)
        (name: DeckName)
        (revision: DeckRevision)
        (validDeck: ValidatedDeck)
        =
        let deck = SavedDeck(deckId, name, revision, validDeck.Cards)

        DomainResult.Succeeded(
            { state with
                savedDecks = state.savedDecks.SetItem(deckId, deck) },
            deck
        )

    /// Saves a new deck.
    let createDeck
        (state: LocalProfileState)
        (deckId: DeckId)
        (name: DeckName)
        (selections: IEnumerable<DeckCardSelection>)
        (authority: BlokemonRuntimeManifest)
        : DomainResult<LocalProfileState * SavedDeck, DeckSaveFailure> =
        ArgumentNullException.ThrowIfNull(deckId, nameof deckId)
        ArgumentNullException.ThrowIfNull(name, nameof name)
        ArgumentNullException.ThrowIfNull(selections, nameof selections)
        ArgumentNullException.ThrowIfNull(authority, nameof authority)

        if state.savedDecks.ContainsKey deckId then
            DomainResult.Failed(DeckSaveFailure.AlreadyExists deckId)
        else
            match DeckRules.validate state.OwnedQuantity authority selections with
            | DeckValidationResult.Valid validDeck ->
                saveDeck state deckId name DeckRevision.Initial validDeck
            | DeckValidationResult.Invalid issues ->
                DomainResult.Failed(DeckSaveFailure.InvalidDeck issues)

    /// Replaces a saved deck, provided the caller holds its current revision.
    let reviseDeck
        (state: LocalProfileState)
        (deckId: DeckId)
        (expectedRevision: DeckRevision)
        (name: DeckName)
        (selections: IEnumerable<DeckCardSelection>)
        (authority: BlokemonRuntimeManifest)
        : DomainResult<LocalProfileState * SavedDeck, DeckSaveFailure> =
        ArgumentNullException.ThrowIfNull(deckId, nameof deckId)
        ArgumentNullException.ThrowIfNull(expectedRevision, nameof expectedRevision)
        ArgumentNullException.ThrowIfNull(name, nameof name)
        ArgumentNullException.ThrowIfNull(selections, nameof selections)
        ArgumentNullException.ThrowIfNull(authority, nameof authority)

        match state.savedDecks.TryGetValue deckId with
        | false, _ -> DomainResult.Failed(DeckSaveFailure.NotFound deckId)
        | _, current when current.Revision <> expectedRevision ->
            DomainResult.Failed(
                DeckSaveFailure.StaleRevision(deckId, expectedRevision, current.Revision)
            )
        | _, current ->
            match current.Revision.TryNext() with
            | None -> DomainResult.Failed(DeckSaveFailure.RevisionExhausted deckId)
            | Some nextRevision ->
                match DeckRules.validate state.OwnedQuantity authority selections with
                | DeckValidationResult.Valid validDeck ->
                    saveDeck state deckId name nextRevision validDeck
                | DeckValidationResult.Invalid issues ->
                    DomainResult.Failed(DeckSaveFailure.InvalidDeck issues)

    // Deleting a deck removes only the deck. Collectible ownership, pack receipts and
    // starter claims are permanent history and are left exactly as they were.
    /// Removes a saved deck.
    let deleteDeck
        (state: LocalProfileState)
        (deckId: DeckId)
        : DomainResult<LocalProfileState * SavedDeck, DeckDeleteFailure> =
        ArgumentNullException.ThrowIfNull(deckId, nameof deckId)

        match state.savedDecks.TryGetValue deckId with
        | false, _ -> DomainResult.Failed DeckDeleteFailure.NotFound
        | _, deck ->
            DomainResult.Succeeded(
                { state with
                    savedDecks = state.savedDecks.Remove deckId },
                deck
            )

namespace Blokemon.Product

open System
open System.Collections.Generic
open System.Collections.Immutable

/// One of the catalogue's starter decks, as the player may claim it.
[<Sealed>]
type StarterDeckDefinition
    (id: StarterDeckId, deckId: DeckId, deckName: DeckName, cards: IEnumerable<DeckCardSelection>) =

    do
        ArgumentNullException.ThrowIfNull(id, nameof id)
        ArgumentNullException.ThrowIfNull(deckId, nameof deckId)
        ArgumentNullException.ThrowIfNull(deckName, nameof deckName)
        ArgumentNullException.ThrowIfNull(cards, nameof cards)

    let immutableCards = cards.ToImmutableArray()

    do
        for card in immutableCards do
            ArgumentNullException.ThrowIfNull(card, nameof cards)

    member _.Id = id

    member _.DeckId = deckId

    member _.DeckName = deckName

    member _.Cards = immutableCards

/// One card the starter granted, and how many copies of it.
[<Sealed>]
type StarterCollectibleGrant internal (cardId: CardId, quantity: int) =

    member _.CardId = cardId

    member _.Quantity = quantity

// A claim records only that a starter was claimed. The deck it created is an ordinary
// saved deck from that moment on: editable, deletable, and never referenced from here.
[<Sealed>]
type StarterDeckClaim
    internal
    (
        id: StarterDeckId,
        commandId: CommandId,
        collectibleGrants: ImmutableArray<StarterCollectibleGrant>
    ) =

    member _.Id = id

    member _.CommandId = commandId

    member _.CollectibleGrants = collectibleGrants

/// Why a starter deck could not be claimed.
[<RequireQualifiedAccess>]
type StarterDeckClaimFailure =
    | CommandConflict of
        CommandId: CommandId *
        ClaimedStarterDeckId: StarterDeckId *
        RequestedStarterDeckId: StarterDeckId
    | AllowanceExhausted of
        ClaimedStarterDeckId: StarterDeckId *
        RequestedStarterDeckId: StarterDeckId
    | InvalidDeck of Issues: ImmutableArray<DeckValidationIssue>

    /// Folds the failure into a single value.
    member this.Match<'TResult>
        (
            onCommandConflict: Func<CommandId, StarterDeckId, StarterDeckId, 'TResult>,
            onAllowanceExhausted: Func<StarterDeckId, StarterDeckId, 'TResult>,
            onInvalidDeck: Func<ImmutableArray<DeckValidationIssue>, 'TResult>
        ) =
        match this with
        | StarterDeckClaimFailure.CommandConflict(commandId,
                                                  claimedStarterDeckId,
                                                  requestedStarterDeckId) ->
            onCommandConflict.Invoke(commandId, claimedStarterDeckId, requestedStarterDeckId)
        | StarterDeckClaimFailure.AllowanceExhausted(claimedStarterDeckId, requestedStarterDeckId) ->
            onAllowanceExhausted.Invoke(claimedStarterDeckId, requestedStarterDeckId)
        | StarterDeckClaimFailure.InvalidDeck issues -> onInvalidDeck.Invoke issues

namespace Blokemon.Product

open System
open System.Collections.Immutable
open System.Globalization

/// Why a deck revision was rejected.
type DeckRevisionFailure =
    | MustBePositive = 0

/// A saved deck's optimistic-concurrency revision.
type DeckRevision =
    private
        { value: int64 }

    /// The revision every deck is first saved at.
    static member val Initial = { value = 1L }

    member this.Value = this.value

    override this.ToString() =
        this.value.ToString(CultureInfo.InvariantCulture)

    static member op_Equality(left: DeckRevision, right: DeckRevision) = left.Equals(right)

    static member op_Inequality(left: DeckRevision, right: DeckRevision) = not (left.Equals(right))

    static member Create(value: int64) =
        if value < 1L then
            DomainResult.Failed DeckRevisionFailure.MustBePositive
        else
            DomainResult.Succeeded { value = value }

    /// The next revision, or nothing when the counter has run out.
    member internal this.TryNext() =
        if this.value = Int64.MaxValue then
            None
        else
            Some { value = this.value + 1L }

/// How many copies of one card a deck asks for.
type DeckCardSelection =
    { CardId: CardId
      Quantity: int }

    static member op_Equality(left: DeckCardSelection, right: DeckCardSelection) =
        left.Equals(right)

    static member op_Inequality(left: DeckCardSelection, right: DeckCardSelection) =
        not (left.Equals(right))

/// Why a set of deck selections is not a legal deck.
[<RequireQualifiedAccess>]
type DeckValidationIssue =
    | QuantityMustBePositive of CardId: CardId * Quantity: int
    | WrongCardCount of Actual: int64 * Required: int
    | UnknownCard of CardId: CardId
    | MechanicalCopyLimitExceeded of CardId: CardId * Actual: int64 * Allowed: int
    | RegularCollectibleRequired
    | CollectibleQuantityNotOwned of CardId: CardId * Requested: int64 * Owned: int
    | CatalogueCardNotFree of CardId: CardId

    /// Folds the issue into a single value.
    member this.Match<'TResult>
        (
            onQuantityMustBePositive: Func<CardId, int, 'TResult>,
            onWrongCardCount: Func<int64, int, 'TResult>,
            onUnknownCard: Func<CardId, 'TResult>,
            onMechanicalCopyLimitExceeded: Func<CardId, int64, int, 'TResult>,
            onRegularCollectibleRequired: Func<'TResult>,
            onCollectibleQuantityNotOwned: Func<CardId, int64, int, 'TResult>,
            onCatalogueCardNotFree: Func<CardId, 'TResult>
        ) =
        match this with
        | DeckValidationIssue.QuantityMustBePositive(cardId, quantity) ->
            onQuantityMustBePositive.Invoke(cardId, quantity)
        | DeckValidationIssue.WrongCardCount(actual, required) ->
            onWrongCardCount.Invoke(actual, required)
        | DeckValidationIssue.UnknownCard cardId -> onUnknownCard.Invoke cardId
        | DeckValidationIssue.MechanicalCopyLimitExceeded(cardId, actual, allowed) ->
            onMechanicalCopyLimitExceeded.Invoke(cardId, actual, allowed)
        | DeckValidationIssue.RegularCollectibleRequired -> onRegularCollectibleRequired.Invoke()
        | DeckValidationIssue.CollectibleQuantityNotOwned(cardId, requested, owned) ->
            onCollectibleQuantityNotOwned.Invoke(cardId, requested, owned)
        | DeckValidationIssue.CatalogueCardNotFree cardId -> onCatalogueCardNotFree.Invoke cardId

/// Deck selections that passed every rule.
[<Sealed>]
type ValidatedDeck internal (cards: ImmutableDictionary<CardId, int>) =
    member _.Cards = cards

/// The outcome of validating deck selections.
[<RequireQualifiedAccess>]
type DeckValidationResult =
    | Valid of Deck: ValidatedDeck
    | Invalid of Issues: ImmutableArray<DeckValidationIssue>

/// A deck the player saved.
[<Sealed>]
type SavedDeck
    internal
    (id: DeckId, name: DeckName, revision: DeckRevision, cards: ImmutableDictionary<CardId, int>) =

    member _.Id = id

    member _.Name = name

    member _.Revision = revision

    member _.Cards = cards

/// Why a deck could not be saved.
[<RequireQualifiedAccess>]
type DeckSaveFailure =
    | AlreadyExists of DeckId: DeckId
    | NotFound of DeckId: DeckId
    | StaleRevision of
        DeckId: DeckId *
        ExpectedRevision: DeckRevision *
        ActualRevision: DeckRevision
    | InvalidDeck of Issues: ImmutableArray<DeckValidationIssue>
    | RevisionExhausted of DeckId: DeckId

    /// Folds the failure into a single value.
    member this.Match<'TResult>
        (
            onAlreadyExists: Func<DeckId, 'TResult>,
            onNotFound: Func<DeckId, 'TResult>,
            onStaleRevision: Func<DeckId, DeckRevision, DeckRevision, 'TResult>,
            onInvalidDeck: Func<ImmutableArray<DeckValidationIssue>, 'TResult>,
            onRevisionExhausted: Func<DeckId, 'TResult>
        ) =
        match this with
        | DeckSaveFailure.AlreadyExists deckId -> onAlreadyExists.Invoke deckId
        | DeckSaveFailure.NotFound deckId -> onNotFound.Invoke deckId
        | DeckSaveFailure.StaleRevision(deckId, expectedRevision, actualRevision) ->
            onStaleRevision.Invoke(deckId, expectedRevision, actualRevision)
        | DeckSaveFailure.InvalidDeck issues -> onInvalidDeck.Invoke issues
        | DeckSaveFailure.RevisionExhausted deckId -> onRevisionExhausted.Invoke deckId

/// Why a deck could not be deleted.
type DeckDeleteFailure =
    | NotFound = 0

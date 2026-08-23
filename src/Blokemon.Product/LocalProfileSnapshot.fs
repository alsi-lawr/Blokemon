namespace Blokemon.Product

open System.Collections.Immutable

/// How many copies of one card a profile owns.
type CollectibleOwnershipSnapshot =
    { CardId: string | null; Quantity: int }

/// One pack the profile opened, and what it drew.
type PackReceiptSnapshot =
    { ReceiptId: string | null
      CommandId: string | null
      Sequence: int
      SampledCollectibleIds: ImmutableArray<string | null> }

/// One card entry inside a saved deck.
type SavedDeckCardSnapshot =
    { CardId: string | null; Quantity: int }

/// A deck the profile saved.
type SavedDeckSnapshot =
    { DeckId: string | null
      Name: string | null
      Revision: int64
      Cards: ImmutableArray<SavedDeckCardSnapshot> }

/// One card a starter claim granted.
type StarterCollectibleGrantSnapshot =
    { CardId: string | null; Quantity: int }

/// One starter claim the profile made.
type StarterDeckClaimSnapshot =
    { StarterDeckId: string | null
      CommandId: string | null
      CollectibleGrants: ImmutableArray<StarterCollectibleGrantSnapshot> }

/// The whole persisted profile. Missing arrays arrive as the default value, which
/// restoration reads as empty.
type LocalProfileSnapshot =
    { AuthorityManifestVersion: string | null
      HistoricalAuthorityManifestVersions: ImmutableArray<string | null>
      UnavailableHistoricalCardIds: ImmutableArray<string | null>
      ProfileId: string | null
      DisplayName: string | null
      GuaranteedRegularCollectibleId: string | null
      CollectibleOwnership: ImmutableArray<CollectibleOwnershipSnapshot>
      PackReceipts: ImmutableArray<PackReceiptSnapshot>
      SavedDecks: ImmutableArray<SavedDeckSnapshot>
      StarterDeckClaims: ImmutableArray<StarterDeckClaimSnapshot>
      Economy: EconomyMode
      EconomyPackAllowance: int }

/// Which economy rule a persisted profile broke.
type EconomyViolationKind =
    | UnknownMode = 0
    | InvalidPackAllowance = 1
    | PackAllowanceExceeded = 2
    | StarterDeckClaimAllowanceExceeded = 3

/// Which persisted value was repeated where it must be unique.
type SnapshotDuplicateKind =
    | OwnershipCardId = 0
    | PackReceiptId = 1
    | PackCommandId = 2
    | SampledCardIdWithinReceipt = 3
    | DeckId = 4
    | DeckCardId = 5
    | StarterGrantCardId = 6
    | StarterClaimCommandId = 7
    | AuthorityManifestVersion = 8
    | HistoricalCardId = 9

/// Why a persisted profile could not be restored.
[<RequireQualifiedAccess>]
type LocalProfileRestorationFailure =
    | InvalidId of Path: string * Failure: TextValueFailure
    | InvalidDisplayName of Failure: DisplayNameCreationFailure
    | MissingEntry of Path: string
    | NegativeQuantity of Path: string * Quantity: int
    | DuplicateValue of Kind: SnapshotDuplicateKind * Value: string
    | UnknownCard of Path: string * CardId: CardId
    | StarterNotRegular of CardId: CardId
    | InvalidPackCardCount of ReceiptId: PackReceiptId * Actual: int
    | InvalidPackSequence of ReceiptId: PackReceiptId * Sequence: int
    | OwnershipHistoryMismatch of CardId: CardId * Actual: int * Expected: int
    | InvalidDeckName of DeckId: DeckId * Failure: TextValueFailure
    | InvalidDeckRevision of DeckId: DeckId * Revision: int64
    | InvalidSavedDeck of DeckId: DeckId * Issues: ImmutableArray<DeckValidationIssue>
    | EconomyRuleViolation of Kind: EconomyViolationKind * Actual: int * Allowed: int

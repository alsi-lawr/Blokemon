using System.Collections.Immutable;

namespace Blokemon.Product;

public sealed record CollectibleOwnershipSnapshot(string? CardId, int Quantity);

public sealed record PackReceiptSnapshot(
    string? ReceiptId,
    string? CommandId,
    int Sequence,
    ImmutableArray<string?> SampledCollectibleIds
);

public sealed record SavedDeckCardSnapshot(string? CardId, int Quantity);

public sealed record SavedDeckSnapshot(
    string? DeckId,
    string? Name,
    long Revision,
    ImmutableArray<SavedDeckCardSnapshot> Cards
);

public sealed record StarterCollectibleGrantSnapshot(string? CardId, int Quantity);

public sealed record StarterDeckClaimSnapshot(
    string? StarterDeckId,
    string? CommandId,
    ImmutableArray<StarterCollectibleGrantSnapshot> CollectibleGrants
);

public sealed record LocalProfileSnapshot(
    string? AuthorityManifestVersion,
    string? ProfileId,
    string? DisplayName,
    string? GuaranteedRegularCollectibleId,
    ImmutableArray<CollectibleOwnershipSnapshot> CollectibleOwnership,
    ImmutableArray<PackReceiptSnapshot> PackReceipts,
    ImmutableArray<SavedDeckSnapshot> SavedDecks,
    ImmutableArray<StarterDeckClaimSnapshot> StarterDeckClaims = default,
    EconomyMode Economy = EconomyMode.Unlimited,
    int EconomyPackAllowance = 0
);

public enum EconomyViolationKind
{
    UnknownMode,
    InvalidPackAllowance,
    PackAllowanceExceeded,
    StarterDeckClaimAllowanceExceeded,
}

public enum SnapshotDuplicateKind
{
    OwnershipCardId,
    PackReceiptId,
    PackCommandId,
    SampledCardIdWithinReceipt,
    DeckId,
    DeckCardId,
    StarterGrantCardId,
    StarterClaimCommandId,
}

public abstract record LocalProfileRestorationFailure
{
    private LocalProfileRestorationFailure() { }

    public abstract TResult Match<TResult>(
        Func<string, TextValueFailure, TResult> onInvalidId,
        Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
        Func<string, TResult> onMissingEntry,
        Func<string, int, TResult> onNegativeQuantity,
        Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
        Func<string, CardId, TResult> onUnknownCard,
        Func<CardId, TResult> onStarterNotRegular,
        Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
        Func<PackReceiptId, int, TResult> onInvalidPackSequence,
        Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
        Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
        Func<DeckId, long, TResult> onInvalidDeckRevision,
        Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
        Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
    );

    public sealed record InvalidId(string Path, TextValueFailure Failure)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onInvalidId(Path, Failure);
    }

    public sealed record InvalidDisplayName(DisplayNameCreationFailure Failure)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onInvalidDisplayName(Failure);
    }

    public sealed record MissingEntry(string Path) : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onMissingEntry(Path);
    }

    public sealed record NegativeQuantity(string Path, int Quantity)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onNegativeQuantity(Path, Quantity);
    }

    public sealed record DuplicateValue(SnapshotDuplicateKind Kind, string Value)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onDuplicateValue(Kind, Value);
    }

    public sealed record UnknownCard(string Path, CardId CardId) : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onUnknownCard(Path, CardId);
    }

    public sealed record StarterNotRegular(CardId CardId) : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onStarterNotRegular(CardId);
    }

    public sealed record InvalidPackCardCount(PackReceiptId ReceiptId, int Actual)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onInvalidPackCardCount(ReceiptId, Actual);
    }

    public sealed record InvalidPackSequence(PackReceiptId ReceiptId, int Sequence)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onInvalidPackSequence(ReceiptId, Sequence);
    }

    public sealed record OwnershipHistoryMismatch(CardId CardId, int Actual, int Expected)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onOwnershipHistoryMismatch(CardId, Actual, Expected);
    }

    public sealed record InvalidDeckName(DeckId DeckId, TextValueFailure Failure)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onInvalidDeckName(DeckId, Failure);
    }

    public sealed record InvalidDeckRevision(DeckId DeckId, long Revision)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onInvalidDeckRevision(DeckId, Revision);
    }

    public sealed record InvalidSavedDeck(DeckId DeckId, ImmutableArray<DeckValidationIssue> Issues)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onInvalidSavedDeck(DeckId, Issues);
    }

    public sealed record EconomyRuleViolation(EconomyViolationKind Kind, int Actual, int Allowed)
        : LocalProfileRestorationFailure
    {
        public override TResult Match<TResult>(
            Func<string, TextValueFailure, TResult> onInvalidId,
            Func<DisplayNameCreationFailure, TResult> onInvalidDisplayName,
            Func<string, TResult> onMissingEntry,
            Func<string, int, TResult> onNegativeQuantity,
            Func<SnapshotDuplicateKind, string, TResult> onDuplicateValue,
            Func<string, CardId, TResult> onUnknownCard,
            Func<CardId, TResult> onStarterNotRegular,
            Func<PackReceiptId, int, TResult> onInvalidPackCardCount,
            Func<PackReceiptId, int, TResult> onInvalidPackSequence,
            Func<CardId, int, int, TResult> onOwnershipHistoryMismatch,
            Func<DeckId, TextValueFailure, TResult> onInvalidDeckName,
            Func<DeckId, long, TResult> onInvalidDeckRevision,
            Func<DeckId, ImmutableArray<DeckValidationIssue>, TResult> onInvalidSavedDeck,
            Func<EconomyViolationKind, int, int, TResult> onEconomyRuleViolation
        ) => onEconomyRuleViolation(Kind, Actual, Allowed);
    }
}

using System.Collections.Immutable;
using Blokemon.Core.SetDesign;

namespace Blokemon.Product;

public sealed record DeckRevision
{
    private DeckRevision(long value) => Value = value;

    public long Value { get; }

    public static DeckRevision Initial { get; } = new(1);

    public static DomainResult<DeckRevision, DeckRevisionFailure> Create(long value) =>
        value < 1
            ? DomainResult<DeckRevision, DeckRevisionFailure>.Failure(
                DeckRevisionFailure.MustBePositive
            )
            : DomainResult<DeckRevision, DeckRevisionFailure>.Success(new DeckRevision(value));

    internal bool TryNext(out DeckRevision next)
    {
        if (Value == long.MaxValue)
        {
            next = this;
            return false;
        }

        next = new DeckRevision(Value + 1);
        return true;
    }

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum DeckRevisionFailure
{
    MustBePositive,
}

public sealed record DeckCardSelection(CardId CardId, int Quantity);

public abstract record DeckValidationIssue
{
    private DeckValidationIssue() { }

    public abstract TResult Match<TResult>(
        Func<CardId, int, TResult> onQuantityMustBePositive,
        Func<long, int, TResult> onWrongCardCount,
        Func<CardId, TResult> onUnknownCard,
        Func<CardId, long, int, TResult> onMechanicalCopyLimitExceeded,
        Func<TResult> onRegularCollectibleRequired,
        Func<CardId, long, int, TResult> onCollectibleQuantityNotOwned,
        Func<CardId, TResult> onCatalogueCardNotFree
    );

    public sealed record QuantityMustBePositive(CardId CardId, int Quantity) : DeckValidationIssue
    {
        public override TResult Match<TResult>(
            Func<CardId, int, TResult> onQuantityMustBePositive,
            Func<long, int, TResult> onWrongCardCount,
            Func<CardId, TResult> onUnknownCard,
            Func<CardId, long, int, TResult> onMechanicalCopyLimitExceeded,
            Func<TResult> onRegularCollectibleRequired,
            Func<CardId, long, int, TResult> onCollectibleQuantityNotOwned,
            Func<CardId, TResult> onCatalogueCardNotFree
        ) => onQuantityMustBePositive(CardId, Quantity);
    }

    public sealed record WrongCardCount(long Actual, int Required) : DeckValidationIssue
    {
        public override TResult Match<TResult>(
            Func<CardId, int, TResult> onQuantityMustBePositive,
            Func<long, int, TResult> onWrongCardCount,
            Func<CardId, TResult> onUnknownCard,
            Func<CardId, long, int, TResult> onMechanicalCopyLimitExceeded,
            Func<TResult> onRegularCollectibleRequired,
            Func<CardId, long, int, TResult> onCollectibleQuantityNotOwned,
            Func<CardId, TResult> onCatalogueCardNotFree
        ) => onWrongCardCount(Actual, Required);
    }

    public sealed record UnknownCard(CardId CardId) : DeckValidationIssue
    {
        public override TResult Match<TResult>(
            Func<CardId, int, TResult> onQuantityMustBePositive,
            Func<long, int, TResult> onWrongCardCount,
            Func<CardId, TResult> onUnknownCard,
            Func<CardId, long, int, TResult> onMechanicalCopyLimitExceeded,
            Func<TResult> onRegularCollectibleRequired,
            Func<CardId, long, int, TResult> onCollectibleQuantityNotOwned,
            Func<CardId, TResult> onCatalogueCardNotFree
        ) => onUnknownCard(CardId);
    }

    public sealed record MechanicalCopyLimitExceeded(CardId CardId, long Actual, int Allowed)
        : DeckValidationIssue
    {
        public override TResult Match<TResult>(
            Func<CardId, int, TResult> onQuantityMustBePositive,
            Func<long, int, TResult> onWrongCardCount,
            Func<CardId, TResult> onUnknownCard,
            Func<CardId, long, int, TResult> onMechanicalCopyLimitExceeded,
            Func<TResult> onRegularCollectibleRequired,
            Func<CardId, long, int, TResult> onCollectibleQuantityNotOwned,
            Func<CardId, TResult> onCatalogueCardNotFree
        ) => onMechanicalCopyLimitExceeded(CardId, Actual, Allowed);
    }

    public sealed record RegularCollectibleRequired : DeckValidationIssue
    {
        public override TResult Match<TResult>(
            Func<CardId, int, TResult> onQuantityMustBePositive,
            Func<long, int, TResult> onWrongCardCount,
            Func<CardId, TResult> onUnknownCard,
            Func<CardId, long, int, TResult> onMechanicalCopyLimitExceeded,
            Func<TResult> onRegularCollectibleRequired,
            Func<CardId, long, int, TResult> onCollectibleQuantityNotOwned,
            Func<CardId, TResult> onCatalogueCardNotFree
        ) => onRegularCollectibleRequired();
    }

    public sealed record CollectibleQuantityNotOwned(CardId CardId, long Requested, int Owned)
        : DeckValidationIssue
    {
        public override TResult Match<TResult>(
            Func<CardId, int, TResult> onQuantityMustBePositive,
            Func<long, int, TResult> onWrongCardCount,
            Func<CardId, TResult> onUnknownCard,
            Func<CardId, long, int, TResult> onMechanicalCopyLimitExceeded,
            Func<TResult> onRegularCollectibleRequired,
            Func<CardId, long, int, TResult> onCollectibleQuantityNotOwned,
            Func<CardId, TResult> onCatalogueCardNotFree
        ) => onCollectibleQuantityNotOwned(CardId, Requested, Owned);
    }

    public sealed record CatalogueCardNotFree(CardId CardId) : DeckValidationIssue
    {
        public override TResult Match<TResult>(
            Func<CardId, int, TResult> onQuantityMustBePositive,
            Func<long, int, TResult> onWrongCardCount,
            Func<CardId, TResult> onUnknownCard,
            Func<CardId, long, int, TResult> onMechanicalCopyLimitExceeded,
            Func<TResult> onRegularCollectibleRequired,
            Func<CardId, long, int, TResult> onCollectibleQuantityNotOwned,
            Func<CardId, TResult> onCatalogueCardNotFree
        ) => onCatalogueCardNotFree(CardId);
    }
}

public abstract record DeckValidationResult
{
    private DeckValidationResult() { }

    public bool IsValid => this is Valid;

    public abstract TResult Match<TResult>(
        Func<ValidatedDeck, TResult> onValid,
        Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalid
    );

    internal static DeckValidationResult Success(ValidatedDeck deck) => new Valid(deck);

    internal static DeckValidationResult Failure(ImmutableArray<DeckValidationIssue> issues) =>
        new Invalid(issues);

    public sealed record Valid(ValidatedDeck Deck) : DeckValidationResult
    {
        public override TResult Match<TResult>(
            Func<ValidatedDeck, TResult> onValid,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalid
        ) => onValid(Deck);
    }

    public sealed record Invalid(ImmutableArray<DeckValidationIssue> Issues) : DeckValidationResult
    {
        public override TResult Match<TResult>(
            Func<ValidatedDeck, TResult> onValid,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalid
        ) => onInvalid(Issues);
    }
}

public sealed class ValidatedDeck
{
    internal ValidatedDeck(ImmutableDictionary<CardId, int> cards) => Cards = cards;

    public ImmutableDictionary<CardId, int> Cards { get; }
}

public static class DeckValidator
{
    public const int RequiredCardCount = 60;

    public const int MechanicalCopyLimit = 4;

    public static DeckValidationResult Validate(
        LocalProfile profile,
        BlokemonRuntimeManifest authority,
        IEnumerable<DeckCardSelection> selections
    )
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(selections);

        var issues = ImmutableArray.CreateBuilder<DeckValidationIssue>();
        var quantities = new Dictionary<CardId, long>();

        foreach (var selection in selections)
        {
            ArgumentNullException.ThrowIfNull(selection);
            ArgumentNullException.ThrowIfNull(selection.CardId);

            if (selection.Quantity <= 0)
            {
                issues.Add(
                    new DeckValidationIssue.QuantityMustBePositive(
                        selection.CardId,
                        selection.Quantity
                    )
                );
                continue;
            }

            quantities[selection.CardId] =
                quantities.GetValueOrDefault(selection.CardId) + selection.Quantity;
        }

        var cardCount = quantities.Values.Sum();
        if (cardCount != RequiredCardCount)
        {
            issues.Add(new DeckValidationIssue.WrongCardCount(cardCount, RequiredCardCount));
        }

        var collectibles = authority.Collectibles.ToDictionary(
            static card => card.Id,
            StringComparer.Ordinal
        );
        var kits = authority.Kits.ToDictionary(static card => card.Id, StringComparer.Ordinal);
        var basicVim = authority.BasicVim.ToDictionary(
            static card => card.Id,
            StringComparer.Ordinal
        );
        var includesRegular = false;

        foreach (var (cardId, quantity) in quantities)
        {
            if (collectibles.TryGetValue(cardId.Value, out var collectible))
            {
                includesRegular |= collectible.Rank == BlokemonRank.Regular;
                CheckCopyLimit(cardId, quantity, collectible.StackCopyLimit, issues);

                var owned = profile.OwnedCollectibleQuantity(cardId);
                if (quantity > owned)
                {
                    issues.Add(
                        new DeckValidationIssue.CollectibleQuantityNotOwned(cardId, quantity, owned)
                    );
                }

                continue;
            }

            if (kits.TryGetValue(cardId.Value, out var kit))
            {
                if (!kit.FreelyAvailable)
                {
                    issues.Add(new DeckValidationIssue.CatalogueCardNotFree(cardId));
                }

                CheckCopyLimit(cardId, quantity, kit.StackCopyLimit, issues);
                continue;
            }

            if (basicVim.TryGetValue(cardId.Value, out var vim))
            {
                if (!vim.FreelyAvailable)
                {
                    issues.Add(new DeckValidationIssue.CatalogueCardNotFree(cardId));
                }

                continue;
            }

            issues.Add(new DeckValidationIssue.UnknownCard(cardId));
        }

        if (!includesRegular)
        {
            issues.Add(new DeckValidationIssue.RegularCollectibleRequired());
        }

        if (issues.Count > 0)
        {
            return DeckValidationResult.Failure(issues.ToImmutable());
        }

        var cards = quantities.ToImmutableDictionary(
            static entry => entry.Key,
            static entry => (int)entry.Value
        );
        return DeckValidationResult.Success(new ValidatedDeck(cards));
    }

    private static void CheckCopyLimit(
        CardId cardId,
        long quantity,
        int cardCopyLimit,
        ImmutableArray<DeckValidationIssue>.Builder issues
    )
    {
        var allowed = Math.Min(cardCopyLimit, MechanicalCopyLimit);
        if (quantity > allowed)
        {
            issues.Add(
                new DeckValidationIssue.MechanicalCopyLimitExceeded(cardId, quantity, allowed)
            );
        }
    }
}

public sealed class SavedDeck
{
    internal SavedDeck(
        DeckId id,
        DeckName name,
        DeckRevision revision,
        ImmutableDictionary<CardId, int> cards
    )
    {
        Id = id;
        Name = name;
        Revision = revision;
        Cards = cards;
    }

    public DeckId Id { get; }

    public DeckName Name { get; }

    public DeckRevision Revision { get; }

    public ImmutableDictionary<CardId, int> Cards { get; }
}

public abstract record DeckSaveFailure
{
    private DeckSaveFailure() { }

    public abstract TResult Match<TResult>(
        Func<DeckId, TResult> onAlreadyExists,
        Func<DeckId, TResult> onNotFound,
        Func<DeckId, DeckRevision, DeckRevision, TResult> onStaleRevision,
        Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck,
        Func<DeckId, TResult> onRevisionExhausted
    );

    public sealed record AlreadyExists(DeckId DeckId) : DeckSaveFailure
    {
        public override TResult Match<TResult>(
            Func<DeckId, TResult> onAlreadyExists,
            Func<DeckId, TResult> onNotFound,
            Func<DeckId, DeckRevision, DeckRevision, TResult> onStaleRevision,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck,
            Func<DeckId, TResult> onRevisionExhausted
        ) => onAlreadyExists(DeckId);
    }

    public sealed record NotFound(DeckId DeckId) : DeckSaveFailure
    {
        public override TResult Match<TResult>(
            Func<DeckId, TResult> onAlreadyExists,
            Func<DeckId, TResult> onNotFound,
            Func<DeckId, DeckRevision, DeckRevision, TResult> onStaleRevision,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck,
            Func<DeckId, TResult> onRevisionExhausted
        ) => onNotFound(DeckId);
    }

    public sealed record StaleRevision(
        DeckId DeckId,
        DeckRevision ExpectedRevision,
        DeckRevision ActualRevision
    ) : DeckSaveFailure
    {
        public override TResult Match<TResult>(
            Func<DeckId, TResult> onAlreadyExists,
            Func<DeckId, TResult> onNotFound,
            Func<DeckId, DeckRevision, DeckRevision, TResult> onStaleRevision,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck,
            Func<DeckId, TResult> onRevisionExhausted
        ) => onStaleRevision(DeckId, ExpectedRevision, ActualRevision);
    }

    public sealed record InvalidDeck(ImmutableArray<DeckValidationIssue> Issues) : DeckSaveFailure
    {
        public override TResult Match<TResult>(
            Func<DeckId, TResult> onAlreadyExists,
            Func<DeckId, TResult> onNotFound,
            Func<DeckId, DeckRevision, DeckRevision, TResult> onStaleRevision,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck,
            Func<DeckId, TResult> onRevisionExhausted
        ) => onInvalidDeck(Issues);
    }

    public sealed record RevisionExhausted(DeckId DeckId) : DeckSaveFailure
    {
        public override TResult Match<TResult>(
            Func<DeckId, TResult> onAlreadyExists,
            Func<DeckId, TResult> onNotFound,
            Func<DeckId, DeckRevision, DeckRevision, TResult> onStaleRevision,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck,
            Func<DeckId, TResult> onRevisionExhausted
        ) => onRevisionExhausted(DeckId);
    }
}

public sealed record DeckSaveTransition(LocalProfile Profile, SavedDeck Deck);

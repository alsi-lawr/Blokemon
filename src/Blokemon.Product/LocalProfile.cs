using System.Collections.Immutable;
using Blokemon.Core.SetDesign;

namespace Blokemon.Product;

public enum LocalProfileCreationFailure
{
    NoRegularCollectibleAvailable,
}

public enum PackOpenFailure
{
    ReceiptIdAlreadyUsed,
    ElevenCardPackUnavailable,
    AuthorityVersionMismatch,
    PackAllowanceExhausted,
}

public enum PackOpenDisposition
{
    Opened,
    AlreadyOpened,
}

public sealed class PackReceipt
{
    internal PackReceipt(
        PackReceiptId id,
        CommandId commandId,
        int sequence,
        ImmutableArray<CardId> sampledCollectibleIds
    )
    {
        Id = id;
        CommandId = commandId;
        Sequence = sequence;
        SampledCollectibleIds = sampledCollectibleIds;
    }

    public PackReceiptId Id { get; }

    public CommandId CommandId { get; }

    public int Sequence { get; }

    public ImmutableArray<CardId> SampledCollectibleIds { get; }
}

public sealed record PackOpenTransition(
    LocalProfile Profile,
    PackReceipt Receipt,
    PackOpenDisposition Disposition
);

public sealed partial class LocalProfile
{
    private readonly ImmutableDictionary<CardId, int> _collectibleOwnership;
    private readonly ImmutableDictionary<CommandId, PackReceipt> _receiptsByCommand;
    private readonly ImmutableDictionary<PackReceiptId, PackReceipt> _receiptsById;
    private readonly ImmutableDictionary<DeckId, SavedDeck> _savedDecks;
    private readonly ImmutableArray<StarterDeckClaim> _starterDeckClaims;

    private LocalProfile(
        ProfileId id,
        DisplayName displayName,
        string authorityManifestVersion,
        CardId guaranteedRegularCollectibleId,
        EconomyRules economy,
        ImmutableDictionary<CardId, int> collectibleOwnership,
        ImmutableDictionary<CommandId, PackReceipt> receiptsByCommand,
        ImmutableDictionary<PackReceiptId, PackReceipt> receiptsById,
        ImmutableDictionary<DeckId, SavedDeck> savedDecks,
        ImmutableArray<StarterDeckClaim> starterDeckClaims
    )
    {
        Id = id;
        DisplayName = displayName;
        BoundAuthorityManifestVersion = authorityManifestVersion;
        GuaranteedRegularCollectibleId = guaranteedRegularCollectibleId;
        Economy = economy;
        _collectibleOwnership = collectibleOwnership;
        _receiptsByCommand = receiptsByCommand;
        _receiptsById = receiptsById;
        _savedDecks = savedDecks;
        _starterDeckClaims = starterDeckClaims;
    }

    public ProfileId Id { get; }

    public DisplayName DisplayName { get; }

    public string BoundAuthorityManifestVersion { get; }

    public CardId GuaranteedRegularCollectibleId { get; }

    public EconomyRules Economy { get; }

    public int? RemainingPackAllowance =>
        EconomyRules.Remaining(Economy.PackAllowance, _receiptsById.Count);

    public int? RemainingStarterDeckClaimAllowance =>
        EconomyRules.Remaining(Economy.StarterDeckClaimAllowance, _starterDeckClaims.Length);

    public IReadOnlyDictionary<CardId, int> CollectibleOwnership => _collectibleOwnership;

    public IReadOnlyDictionary<PackReceiptId, PackReceipt> PackReceipts => _receiptsById;

    public IReadOnlyDictionary<DeckId, SavedDeck> SavedDecks => _savedDecks;

    public ImmutableArray<StarterDeckClaim> StarterDeckClaims => _starterDeckClaims;

    public StarterDeckClaim? LatestStarterDeckClaim =>
        _starterDeckClaims.IsEmpty ? null : _starterDeckClaims[^1];

    public static DomainResult<LocalProfile, LocalProfileCreationFailure> Create(
        ProfileId id,
        DisplayName displayName,
        BlokemonRuntimeManifest authority,
        EconomyRules? economy = null
    )
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(authority);

        var regular = authority
            .Collectibles.Where(static card => card.Rank == BlokemonRank.Regular)
            .OrderBy(static card => card.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (regular is null)
        {
            return DomainResult<LocalProfile, LocalProfileCreationFailure>.Failure(
                LocalProfileCreationFailure.NoRegularCollectibleAvailable
            );
        }

        var regularId = CardId.FromAuthority(regular.Id);
        return DomainResult<LocalProfile, LocalProfileCreationFailure>.Success(
            new LocalProfile(
                id,
                displayName,
                authority.ManifestVersion,
                regularId,
                economy ?? EconomyRules.Unlimited,
                ImmutableDictionary<CardId, int>.Empty.Add(regularId, 1),
                ImmutableDictionary<CommandId, PackReceipt>.Empty,
                ImmutableDictionary<PackReceiptId, PackReceipt>.Empty,
                ImmutableDictionary<DeckId, SavedDeck>.Empty,
                ImmutableArray<StarterDeckClaim>.Empty
            )
        );
    }

    public int OwnedCollectibleQuantity(CardId cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);
        return _collectibleOwnership.GetValueOrDefault(cardId);
    }

    public DomainResult<PackOpenTransition, PackOpenFailure> OpenPack(
        CommandId commandId,
        PackReceiptId receiptId,
        BlokemonRuntimeManifest authority,
        IBlokemonRandomSource random
    )
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ArgumentNullException.ThrowIfNull(receiptId);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(random);

        if (_receiptsByCommand.TryGetValue(commandId, out var existingReceipt))
        {
            return DomainResult<PackOpenTransition, PackOpenFailure>.Success(
                new PackOpenTransition(this, existingReceipt, PackOpenDisposition.AlreadyOpened)
            );
        }

        if (
            !string.Equals(
                BoundAuthorityManifestVersion,
                authority.ManifestVersion,
                StringComparison.Ordinal
            )
        )
        {
            return DomainResult<PackOpenTransition, PackOpenFailure>.Failure(
                PackOpenFailure.AuthorityVersionMismatch
            );
        }

        if (_receiptsById.ContainsKey(receiptId))
        {
            return DomainResult<PackOpenTransition, PackOpenFailure>.Failure(
                PackOpenFailure.ReceiptIdAlreadyUsed
            );
        }

        if (Economy.PackAllowance is { } packAllowance && _receiptsById.Count >= packAllowance)
        {
            return DomainResult<PackOpenTransition, PackOpenFailure>.Failure(
                PackOpenFailure.PackAllowanceExhausted
            );
        }

        if (!CanSampleEleven(authority))
        {
            return DomainResult<PackOpenTransition, PackOpenFailure>.Failure(
                PackOpenFailure.ElevenCardPackUnavailable
            );
        }

        var sampledIds = BlokemonPackSampler
            .SampleEleven(authority, random)
            .Select(CardId.FromAuthority)
            .ToImmutableArray();
        var ownership = _collectibleOwnership;
        foreach (var cardId in sampledIds)
        {
            ownership = ownership.SetItem(cardId, ownership.GetValueOrDefault(cardId) + 1);
        }

        var receipt = new PackReceipt(receiptId, commandId, _receiptsById.Count + 1, sampledIds);
        var profile = Copy(
            collectibleOwnership: ownership,
            receiptsByCommand: _receiptsByCommand.Add(commandId, receipt),
            receiptsById: _receiptsById.Add(receiptId, receipt)
        );
        return DomainResult<PackOpenTransition, PackOpenFailure>.Success(
            new PackOpenTransition(profile, receipt, PackOpenDisposition.Opened)
        );
    }

    public DomainResult<DeckSaveTransition, DeckSaveFailure> CreateDeck(
        DeckId deckId,
        DeckName name,
        IEnumerable<DeckCardSelection> selections,
        BlokemonRuntimeManifest authority
    )
    {
        ArgumentNullException.ThrowIfNull(deckId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(authority);

        if (_savedDecks.ContainsKey(deckId))
        {
            return DomainResult<DeckSaveTransition, DeckSaveFailure>.Failure(
                new DeckSaveFailure.AlreadyExists(deckId)
            );
        }

        return DeckValidator
            .Validate(this, authority, selections)
            .Match(
                validDeck => SaveNewDeck(deckId, name, validDeck),
                issues =>
                    DomainResult<DeckSaveTransition, DeckSaveFailure>.Failure(
                        new DeckSaveFailure.InvalidDeck(issues)
                    )
            );
    }

    public DomainResult<DeckSaveTransition, DeckSaveFailure> ReviseDeck(
        DeckId deckId,
        DeckRevision expectedRevision,
        DeckName name,
        IEnumerable<DeckCardSelection> selections,
        BlokemonRuntimeManifest authority
    )
    {
        ArgumentNullException.ThrowIfNull(deckId);
        ArgumentNullException.ThrowIfNull(expectedRevision);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(authority);

        if (!_savedDecks.TryGetValue(deckId, out var current))
        {
            return DomainResult<DeckSaveTransition, DeckSaveFailure>.Failure(
                new DeckSaveFailure.NotFound(deckId)
            );
        }

        if (current.Revision != expectedRevision)
        {
            return DomainResult<DeckSaveTransition, DeckSaveFailure>.Failure(
                new DeckSaveFailure.StaleRevision(deckId, expectedRevision, current.Revision)
            );
        }

        if (!current.Revision.TryNext(out var nextRevision))
        {
            return DomainResult<DeckSaveTransition, DeckSaveFailure>.Failure(
                new DeckSaveFailure.RevisionExhausted(deckId)
            );
        }

        return DeckValidator
            .Validate(this, authority, selections)
            .Match(
                validDeck => SaveRevisedDeck(deckId, name, nextRevision, validDeck),
                issues =>
                    DomainResult<DeckSaveTransition, DeckSaveFailure>.Failure(
                        new DeckSaveFailure.InvalidDeck(issues)
                    )
            );
    }

    // Deleting a deck removes only the deck. Collectible ownership, pack receipts and
    // starter claims are permanent history and are left exactly as they were.
    public DomainResult<DeckDeleteTransition, DeckDeleteFailure> DeleteDeck(DeckId deckId)
    {
        ArgumentNullException.ThrowIfNull(deckId);

        if (!_savedDecks.TryGetValue(deckId, out var deck))
        {
            return DomainResult<DeckDeleteTransition, DeckDeleteFailure>.Failure(
                DeckDeleteFailure.NotFound
            );
        }

        return DomainResult<DeckDeleteTransition, DeckDeleteFailure>.Success(
            new DeckDeleteTransition(Copy(savedDecks: _savedDecks.Remove(deckId)), deck)
        );
    }

    private DomainResult<DeckSaveTransition, DeckSaveFailure> SaveNewDeck(
        DeckId deckId,
        DeckName name,
        ValidatedDeck validDeck
    ) => SaveRevisedDeck(deckId, name, DeckRevision.Initial, validDeck);

    private DomainResult<DeckSaveTransition, DeckSaveFailure> SaveRevisedDeck(
        DeckId deckId,
        DeckName name,
        DeckRevision revision,
        ValidatedDeck validDeck
    )
    {
        var deck = new SavedDeck(deckId, name, revision, validDeck.Cards);
        var profile = Copy(savedDecks: _savedDecks.SetItem(deckId, deck));
        return DomainResult<DeckSaveTransition, DeckSaveFailure>.Success(
            new DeckSaveTransition(profile, deck)
        );
    }

    private static bool CanSampleEleven(BlokemonRuntimeManifest authority) =>
        authority.Products.Eleven.Count == 11
        && authority.Products.Eleven.Slots.Sum(static slot => (long)slot.Count) == 11
        && authority.Products.Eleven.Slots.All(slot =>
            slot.Count >= 0
            && authority.Collectibles.Count(card => card.ProductBucket == slot.Bucket) >= slot.Count
        );

    private LocalProfile Copy(
        ImmutableDictionary<CardId, int>? collectibleOwnership = null,
        ImmutableDictionary<CommandId, PackReceipt>? receiptsByCommand = null,
        ImmutableDictionary<PackReceiptId, PackReceipt>? receiptsById = null,
        ImmutableDictionary<DeckId, SavedDeck>? savedDecks = null,
        ImmutableArray<StarterDeckClaim>? starterDeckClaims = null
    ) =>
        new(
            Id,
            DisplayName,
            BoundAuthorityManifestVersion,
            GuaranteedRegularCollectibleId,
            Economy,
            collectibleOwnership ?? _collectibleOwnership,
            receiptsByCommand ?? _receiptsByCommand,
            receiptsById ?? _receiptsById,
            savedDecks ?? _savedDecks,
            starterDeckClaims ?? _starterDeckClaims
        );
}

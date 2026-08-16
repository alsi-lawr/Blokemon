using System.Collections.Immutable;

namespace Blokemon.Product;

public sealed class StarterDeckDefinition
{
    public StarterDeckDefinition(
        StarterDeckId id,
        DeckId deckId,
        DeckName deckName,
        IEnumerable<DeckCardSelection> cards
    )
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(deckId);
        ArgumentNullException.ThrowIfNull(deckName);
        ArgumentNullException.ThrowIfNull(cards);

        var immutableCards = cards.ToImmutableArray();
        foreach (var card in immutableCards)
        {
            ArgumentNullException.ThrowIfNull(card);
        }

        Id = id;
        DeckId = deckId;
        DeckName = deckName;
        Cards = immutableCards;
    }

    public StarterDeckId Id { get; }

    public DeckId DeckId { get; }

    public DeckName DeckName { get; }

    public ImmutableArray<DeckCardSelection> Cards { get; }

    internal bool Matches(StarterDeckClaim claim)
    {
        if (Id != claim.Id || DeckId != claim.Deck.Id || DeckName != claim.Deck.Name)
        {
            return false;
        }

        var quantities = new Dictionary<CardId, long>();
        foreach (var selection in Cards)
        {
            if (selection.Quantity <= 0)
            {
                return false;
            }

            var current = quantities.GetValueOrDefault(selection.CardId);
            if (current > long.MaxValue - selection.Quantity)
            {
                return false;
            }

            quantities[selection.CardId] = current + selection.Quantity;
        }

        return quantities.Count == claim.Deck.Cards.Count
            && quantities.All(entry =>
                claim.Deck.Cards.TryGetValue(entry.Key, out var quantity) && entry.Value == quantity
            );
    }
}

public sealed class StarterCollectibleGrant
{
    internal StarterCollectibleGrant(CardId cardId, int quantity)
    {
        CardId = cardId;
        Quantity = quantity;
    }

    public CardId CardId { get; }

    public int Quantity { get; }
}

public sealed class StarterDeckClaim
{
    internal StarterDeckClaim(
        StarterDeckId id,
        CommandId commandId,
        SavedDeck deck,
        ImmutableArray<StarterCollectibleGrant> collectibleGrants
    )
    {
        Id = id;
        CommandId = commandId;
        Deck = deck;
        CollectibleGrants = collectibleGrants;
    }

    public StarterDeckId Id { get; }

    public CommandId CommandId { get; }

    public SavedDeck Deck { get; }

    public ImmutableArray<StarterCollectibleGrant> CollectibleGrants { get; }
}

public abstract record StarterDeckClaimOutcome
{
    private StarterDeckClaimOutcome() { }

    public abstract TResult Match<TResult>(
        Func<LocalProfile, StarterDeckClaim, TResult> onClaimed,
        Func<LocalProfile, StarterDeckClaim, TResult> onAlreadyClaimed
    );

    public sealed record Claimed(LocalProfile Profile, StarterDeckClaim Claim)
        : StarterDeckClaimOutcome
    {
        public override TResult Match<TResult>(
            Func<LocalProfile, StarterDeckClaim, TResult> onClaimed,
            Func<LocalProfile, StarterDeckClaim, TResult> onAlreadyClaimed
        ) => onClaimed(Profile, Claim);
    }

    public sealed record AlreadyClaimed(LocalProfile Profile, StarterDeckClaim Claim)
        : StarterDeckClaimOutcome
    {
        public override TResult Match<TResult>(
            Func<LocalProfile, StarterDeckClaim, TResult> onClaimed,
            Func<LocalProfile, StarterDeckClaim, TResult> onAlreadyClaimed
        ) => onAlreadyClaimed(Profile, Claim);
    }
}

public abstract record StarterDeckClaimFailure
{
    private StarterDeckClaimFailure() { }

    public abstract TResult Match<TResult>(
        Func<CommandId, StarterDeckId, StarterDeckId, TResult> onCommandConflict,
        Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck
    );

    public sealed record CommandConflict(
        CommandId CommandId,
        StarterDeckId ClaimedStarterDeckId,
        StarterDeckId RequestedStarterDeckId
    ) : StarterDeckClaimFailure
    {
        public override TResult Match<TResult>(
            Func<CommandId, StarterDeckId, StarterDeckId, TResult> onCommandConflict,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck
        ) => onCommandConflict(CommandId, ClaimedStarterDeckId, RequestedStarterDeckId);
    }

    public sealed record InvalidDeck(ImmutableArray<DeckValidationIssue> Issues)
        : StarterDeckClaimFailure
    {
        public override TResult Match<TResult>(
            Func<CommandId, StarterDeckId, StarterDeckId, TResult> onCommandConflict,
            Func<ImmutableArray<DeckValidationIssue>, TResult> onInvalidDeck
        ) => onInvalidDeck(Issues);
    }
}

public sealed partial class LocalProfile
{
    public DomainResult<StarterDeckClaimOutcome, StarterDeckClaimFailure> ClaimStarterDeck(
        CommandId commandId,
        StarterDeckDefinition definition,
        Blokemon.Core.SetDesign.BlokemonRuntimeManifest currentAuthority
    )
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(currentAuthority);

        if (
            _starterDeckClaims.FirstOrDefault(claim => claim.CommandId == commandId)
            is { } existingClaim
        )
        {
            return definition.Matches(existingClaim)
                ? DomainResult<StarterDeckClaimOutcome, StarterDeckClaimFailure>.Success(
                    new StarterDeckClaimOutcome.AlreadyClaimed(this, existingClaim)
                )
                : DomainResult<StarterDeckClaimOutcome, StarterDeckClaimFailure>.Failure(
                    new StarterDeckClaimFailure.CommandConflict(
                        commandId,
                        existingClaim.Id,
                        definition.Id
                    )
                );
        }

        // Opening a starter always grants its full collectible contents, however many
        // copies of it were opened before.
        var authorityCollectibleIds = currentAuthority
            .Collectibles.Select(static card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        var grantQuantities = new Dictionary<CardId, int>();
        foreach (var selection in definition.Cards)
        {
            if (authorityCollectibleIds.Contains(selection.CardId.Value))
            {
                grantQuantities[selection.CardId] =
                    grantQuantities.GetValueOrDefault(selection.CardId) + selection.Quantity;
            }
        }

        var ownership = _collectibleOwnership;
        foreach (var (cardId, quantity) in grantQuantities)
        {
            ownership = ownership.SetItem(cardId, ownership.GetValueOrDefault(cardId) + quantity);
        }
        var grants = grantQuantities
            .OrderBy(static entry => entry.Key.Value, StringComparer.Ordinal)
            .Select(static entry => new StarterCollectibleGrant(entry.Key, entry.Value))
            .ToImmutableArray();

        var grantedProfile = Copy(collectibleOwnership: ownership);
        var validation = DeckValidator.Validate(grantedProfile, currentAuthority, definition.Cards);
        if (validation is DeckValidationResult.Invalid invalidDeck)
        {
            return DomainResult<StarterDeckClaimOutcome, StarterDeckClaimFailure>.Failure(
                new StarterDeckClaimFailure.InvalidDeck(invalidDeck.Issues)
            );
        }
        var validatedDeck = ((DeckValidationResult.Valid)validation).Deck;

        var deck = new SavedDeck(
            definition.DeckId,
            definition.DeckName,
            DeckRevision.Initial,
            validatedDeck.Cards
        );
        var claim = new StarterDeckClaim(definition.Id, commandId, deck, grants);
        var profile = Copy(
            collectibleOwnership: ownership,
            savedDecks: _savedDecks.ContainsKey(deck.Id) ? _savedDecks : _savedDecks.Add(deck.Id, deck),
            starterDeckClaims: _starterDeckClaims.Add(claim)
        );

        return DomainResult<StarterDeckClaimOutcome, StarterDeckClaimFailure>.Success(
            new StarterDeckClaimOutcome.Claimed(profile, claim)
        );
    }
}

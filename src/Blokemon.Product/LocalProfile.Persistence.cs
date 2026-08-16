using System.Collections.Immutable;
using Blokemon.Core.SetDesign;

namespace Blokemon.Product;

public sealed partial class LocalProfile
{
    public LocalProfileSnapshot ToSnapshot() =>
        new(
            BoundAuthorityManifestVersion,
            Id.Value,
            DisplayName.Value,
            GuaranteedRegularCollectibleId.Value,
            _collectibleOwnership
                .OrderBy(static entry => entry.Key.Value, StringComparer.Ordinal)
                .Select(static entry => new CollectibleOwnershipSnapshot(
                    entry.Key.Value,
                    entry.Value
                ))
                .ToImmutableArray(),
            _receiptsById
                .Values.OrderBy(static receipt => receipt.Sequence)
                .Select(static receipt => new PackReceiptSnapshot(
                    receipt.Id.Value,
                    receipt.CommandId.Value,
                    receipt.Sequence,
                    receipt
                        .SampledCollectibleIds.Select(static cardId => (string?)cardId.Value)
                        .ToImmutableArray()
                ))
                .ToImmutableArray(),
            _savedDecks
                .Values.OrderBy(static deck => deck.Id.Value, StringComparer.Ordinal)
                .Select(ToSavedDeckSnapshot)
                .ToImmutableArray(),
            _starterDeckClaims.Select(ToStarterDeckClaimSnapshot).ToImmutableArray(),
            Economy.Mode,
            Economy.PersistedPackAllowance
        );

    public static DomainResult<LocalProfile, LocalProfileRestorationFailure> Restore(
        LocalProfileSnapshot snapshot,
        BlokemonRuntimeManifest currentAuthority
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentAuthority);

        if (string.IsNullOrWhiteSpace(snapshot.AuthorityManifestVersion))
        {
            return RestorationFailed(
                new LocalProfileRestorationFailure.InvalidId(
                    nameof(snapshot.AuthorityManifestVersion),
                    TextValueFailure.Required
                )
            );
        }

        var profileIdResult = ProfileId.Create(snapshot.ProfileId);
        if (profileIdResult is DomainResult<ProfileId, TextValueFailure>.Failed invalidProfileId)
        {
            return RestorationFailed(
                new LocalProfileRestorationFailure.InvalidId(
                    nameof(snapshot.ProfileId),
                    invalidProfileId.Error
                )
            );
        }
        var profileId = (
            (DomainResult<ProfileId, TextValueFailure>.Succeeded)profileIdResult
        ).Value;

        var displayNameResult = DisplayName.Create(snapshot.DisplayName);
        if (
            displayNameResult
            is DomainResult<DisplayName, DisplayNameCreationFailure>.Failed invalidDisplayName
        )
        {
            return RestorationFailed(
                new LocalProfileRestorationFailure.InvalidDisplayName(invalidDisplayName.Error)
            );
        }
        var displayName = (
            (DomainResult<DisplayName, DisplayNameCreationFailure>.Succeeded)displayNameResult
        ).Value;

        var starterIdResult = CardId.Create(snapshot.GuaranteedRegularCollectibleId);
        if (starterIdResult is DomainResult<CardId, TextValueFailure>.Failed invalidStarterId)
        {
            return RestorationFailed(
                new LocalProfileRestorationFailure.InvalidId(
                    nameof(snapshot.GuaranteedRegularCollectibleId),
                    invalidStarterId.Error
                )
            );
        }
        var starterId = ((DomainResult<CardId, TextValueFailure>.Succeeded)starterIdResult).Value;

        var economyResult = EconomyRules.Create(snapshot.Economy, snapshot.EconomyPackAllowance);
        if (economyResult is DomainResult<EconomyRules, EconomyRulesFailure>.Failed invalidEconomy)
        {
            return RestorationFailed(
                new LocalProfileRestorationFailure.EconomyRuleViolation(
                    invalidEconomy.Error == EconomyRulesFailure.UnknownMode
                        ? EconomyViolationKind.UnknownMode
                        : EconomyViolationKind.InvalidPackAllowance,
                    invalidEconomy.Error == EconomyRulesFailure.UnknownMode
                        ? (int)snapshot.Economy
                        : snapshot.EconomyPackAllowance,
                    0
                )
            );
        }
        var economy = (
            (DomainResult<EconomyRules, EconomyRulesFailure>.Succeeded)economyResult
        ).Value;

        var ownershipSnapshots = OrEmpty(snapshot.CollectibleOwnership);
        var receiptSnapshots = OrEmpty(snapshot.PackReceipts);
        var deckSnapshots = OrEmpty(snapshot.SavedDecks);

        var isCurrentAuthority = string.Equals(
            snapshot.AuthorityManifestVersion,
            currentAuthority.ManifestVersion,
            StringComparison.Ordinal
        );
        var authorityCollectibles = currentAuthority.Collectibles.ToDictionary(
            static card => card.Id,
            StringComparer.Ordinal
        );
        var currentCollectibles = isCurrentAuthority ? authorityCollectibles : null;

        if (
            isCurrentAuthority
            && (
                !currentCollectibles!.TryGetValue(starterId.Value, out var starter)
                || starter.Rank != BlokemonRank.Regular
            )
        )
        {
            return RestorationFailed(
                currentCollectibles.ContainsKey(starterId.Value)
                    ? new LocalProfileRestorationFailure.StarterNotRegular(starterId)
                    : new LocalProfileRestorationFailure.UnknownCard(
                        nameof(snapshot.GuaranteedRegularCollectibleId),
                        starterId
                    )
            );
        }

        var claimSnapshots = OrEmpty(snapshot.StarterDeckClaims);
        var parsedClaims =
            new List<(
                StarterDeckId StarterDeckId,
                CommandId CommandId,
                SavedDeckSnapshot Deck,
                ImmutableArray<StarterCollectibleGrant> Grants
            )>(claimSnapshots.Length);
        var claimCommandIds = new HashSet<CommandId>();
        for (var claimIndex = 0; claimIndex < claimSnapshots.Length; claimIndex++)
        {
            var claimSnapshot = claimSnapshots[claimIndex];
            var claimPath = $"{nameof(snapshot.StarterDeckClaims)}[{claimIndex}]";
            if (claimSnapshot is null)
            {
                return RestorationFailed(new LocalProfileRestorationFailure.MissingEntry(claimPath));
            }

            var starterDeckIdResult = StarterDeckId.Create(claimSnapshot.StarterDeckId);
            if (
                starterDeckIdResult
                is DomainResult<StarterDeckId, TextValueFailure>.Failed invalidStarterDeckId
            )
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidId(
                        $"{claimPath}.{nameof(claimSnapshot.StarterDeckId)}",
                        invalidStarterDeckId.Error
                    )
                );
            }
            var claimedStarterDeckId = (
                (DomainResult<StarterDeckId, TextValueFailure>.Succeeded)starterDeckIdResult
            ).Value;

            var commandIdResult = CommandId.Create(claimSnapshot.CommandId);
            if (
                commandIdResult is DomainResult<CommandId, TextValueFailure>.Failed invalidCommandId
            )
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidId(
                        $"{claimPath}.{nameof(claimSnapshot.CommandId)}",
                        invalidCommandId.Error
                    )
                );
            }
            var starterClaimCommandId = (
                (DomainResult<CommandId, TextValueFailure>.Succeeded)commandIdResult
            ).Value;
            if (!claimCommandIds.Add(starterClaimCommandId))
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.StarterClaimCommandId,
                        starterClaimCommandId.Value
                    )
                );
            }

            if (claimSnapshot.Deck is null)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.MissingEntry(
                        $"{claimPath}.{nameof(claimSnapshot.Deck)}"
                    )
                );
            }

            var grantSnapshots = OrEmpty(claimSnapshot.CollectibleGrants);
            var grants = ImmutableArray.CreateBuilder<StarterCollectibleGrant>(
                grantSnapshots.Length
            );
            var grantedCardIds = new HashSet<CardId>();
            for (var grantIndex = 0; grantIndex < grantSnapshots.Length; grantIndex++)
            {
                var grant = grantSnapshots[grantIndex];
                if (grant is null)
                {
                    return RestorationFailed(
                        new LocalProfileRestorationFailure.MissingEntry(
                            $"{claimPath}.{nameof(claimSnapshot.CollectibleGrants)}[{grantIndex}]"
                        )
                    );
                }
                if (grant.Quantity <= 0)
                {
                    return RestorationFailed(
                        new LocalProfileRestorationFailure.NegativeQuantity(
                            $"{claimPath}.{nameof(claimSnapshot.CollectibleGrants)}[{grantIndex}].{nameof(grant.Quantity)}",
                            grant.Quantity
                        )
                    );
                }

                var grantCardIdResult = CardId.Create(grant.CardId);
                if (
                    grantCardIdResult
                    is DomainResult<CardId, TextValueFailure>.Failed invalidGrantCardId
                )
                {
                    return RestorationFailed(
                        new LocalProfileRestorationFailure.InvalidId(
                            $"{claimPath}.{nameof(claimSnapshot.CollectibleGrants)}[{grantIndex}].{nameof(grant.CardId)}",
                            invalidGrantCardId.Error
                        )
                    );
                }
                var grantCardId = (
                    (DomainResult<CardId, TextValueFailure>.Succeeded)grantCardIdResult
                ).Value;

                if (!grantedCardIds.Add(grantCardId))
                {
                    return RestorationFailed(
                        new LocalProfileRestorationFailure.DuplicateValue(
                            SnapshotDuplicateKind.StarterGrantCardId,
                            grantCardId.Value
                        )
                    );
                }
                if (!authorityCollectibles.ContainsKey(grantCardId.Value))
                {
                    return RestorationFailed(
                        new LocalProfileRestorationFailure.UnknownCard(
                            $"{claimPath}.{nameof(claimSnapshot.CollectibleGrants)}[{grantIndex}].{nameof(grant.CardId)}",
                            grantCardId
                        )
                    );
                }

                grants.Add(new StarterCollectibleGrant(grantCardId, grant.Quantity));
            }

            parsedClaims.Add(
                (
                    claimedStarterDeckId,
                    starterClaimCommandId,
                    claimSnapshot.Deck,
                    grants.MoveToImmutable()
                )
            );
        }

        var ownership = new Dictionary<CardId, int>();
        for (var index = 0; index < ownershipSnapshots.Length; index++)
        {
            var item = ownershipSnapshots[index];
            if (item is null)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.MissingEntry(
                        $"CollectibleOwnership[{index}]"
                    )
                );
            }
            if (item.Quantity < 0)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.NegativeQuantity(
                        $"CollectibleOwnership[{index}].Quantity",
                        item.Quantity
                    )
                );
            }

            var cardIdResult = CardId.Create(item.CardId);
            if (cardIdResult is DomainResult<CardId, TextValueFailure>.Failed invalidCardId)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidId(
                        $"CollectibleOwnership[{index}].CardId",
                        invalidCardId.Error
                    )
                );
            }
            var cardId = ((DomainResult<CardId, TextValueFailure>.Succeeded)cardIdResult).Value;

            if (!ownership.TryAdd(cardId, item.Quantity))
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.OwnershipCardId,
                        cardId.Value
                    )
                );
            }
            if (isCurrentAuthority && !currentCollectibles!.ContainsKey(cardId.Value))
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.UnknownCard(
                        $"CollectibleOwnership[{index}].CardId",
                        cardId
                    )
                );
            }
        }

        var receiptsById = new Dictionary<PackReceiptId, PackReceipt>();
        var receiptsByCommand = new Dictionary<CommandId, PackReceipt>();
        var receiptSequences = new HashSet<int>();
        var expectedOwnership = new Dictionary<CardId, int> { [starterId] = 1 };
        for (var receiptIndex = 0; receiptIndex < receiptSnapshots.Length; receiptIndex++)
        {
            var item = receiptSnapshots[receiptIndex];
            if (item is null)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.MissingEntry($"PackReceipts[{receiptIndex}]")
                );
            }

            var receiptIdResult = PackReceiptId.Create(item.ReceiptId);
            if (
                receiptIdResult
                is DomainResult<PackReceiptId, TextValueFailure>.Failed invalidReceiptId
            )
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidId(
                        $"PackReceipts[{receiptIndex}].ReceiptId",
                        invalidReceiptId.Error
                    )
                );
            }
            var receiptId = (
                (DomainResult<PackReceiptId, TextValueFailure>.Succeeded)receiptIdResult
            ).Value;

            var commandIdResult = CommandId.Create(item.CommandId);
            if (
                commandIdResult is DomainResult<CommandId, TextValueFailure>.Failed invalidCommandId
            )
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidId(
                        $"PackReceipts[{receiptIndex}].CommandId",
                        invalidCommandId.Error
                    )
                );
            }
            var commandId = (
                (DomainResult<CommandId, TextValueFailure>.Succeeded)commandIdResult
            ).Value;

            if (receiptsById.ContainsKey(receiptId))
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.PackReceiptId,
                        receiptId.Value
                    )
                );
            }
            if (receiptsByCommand.ContainsKey(commandId))
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.PackCommandId,
                        commandId.Value
                    )
                );
            }
            if (item.Sequence <= 0 || !receiptSequences.Add(item.Sequence))
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidPackSequence(receiptId, item.Sequence)
                );
            }

            var sampledSnapshots = OrEmpty(item.SampledCollectibleIds);
            if (sampledSnapshots.Length != 11)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidPackCardCount(
                        receiptId,
                        sampledSnapshots.Length
                    )
                );
            }

            var sampledIds = ImmutableArray.CreateBuilder<CardId>(sampledSnapshots.Length);
            var sampledWithinReceipt = new HashSet<CardId>();
            for (var cardIndex = 0; cardIndex < sampledSnapshots.Length; cardIndex++)
            {
                var cardIdResult = CardId.Create(sampledSnapshots[cardIndex]);
                if (cardIdResult is DomainResult<CardId, TextValueFailure>.Failed invalidCardId)
                {
                    return RestorationFailed(
                        new LocalProfileRestorationFailure.InvalidId(
                            $"PackReceipts[{receiptIndex}].SampledCollectibleIds[{cardIndex}]",
                            invalidCardId.Error
                        )
                    );
                }
                var cardId = ((DomainResult<CardId, TextValueFailure>.Succeeded)cardIdResult).Value;

                if (!sampledWithinReceipt.Add(cardId))
                {
                    return RestorationFailed(
                        new LocalProfileRestorationFailure.DuplicateValue(
                            SnapshotDuplicateKind.SampledCardIdWithinReceipt,
                            cardId.Value
                        )
                    );
                }
                if (isCurrentAuthority && !currentCollectibles!.ContainsKey(cardId.Value))
                {
                    return RestorationFailed(
                        new LocalProfileRestorationFailure.UnknownCard(
                            $"PackReceipts[{receiptIndex}].SampledCollectibleIds[{cardIndex}]",
                            cardId
                        )
                    );
                }

                sampledIds.Add(cardId);
                expectedOwnership[cardId] = expectedOwnership.GetValueOrDefault(cardId) + 1;
            }

            var receipt = new PackReceipt(
                receiptId,
                commandId,
                item.Sequence,
                sampledIds.MoveToImmutable()
            );
            receiptsById.Add(receiptId, receipt);
            receiptsByCommand.Add(commandId, receipt);
        }

        var receiptsInSequence = receiptsById.Values.OrderBy(static receipt => receipt.Sequence);
        var expectedSequence = 1;
        foreach (var receipt in receiptsInSequence)
        {
            if (receipt.Sequence != expectedSequence)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidPackSequence(
                        receipt.Id,
                        receipt.Sequence
                    )
                );
            }
            expectedSequence++;
        }

        foreach (var parsedClaim in parsedClaims)
        {
            foreach (var grant in parsedClaim.Grants)
            {
                expectedOwnership[grant.CardId] =
                    expectedOwnership.GetValueOrDefault(grant.CardId) + grant.Quantity;
            }
        }

        foreach (
            var cardId in ownership
                .Keys.Concat(expectedOwnership.Keys)
                .Distinct()
                .OrderBy(static cardId => cardId.Value, StringComparer.Ordinal)
        )
        {
            var actual = ownership.GetValueOrDefault(cardId);
            var expected = expectedOwnership.GetValueOrDefault(cardId);
            if (actual != expected)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.OwnershipHistoryMismatch(
                        cardId,
                        actual,
                        expected
                    )
                );
            }
        }

        if (economy.PackAllowance is { } packAllowance && receiptsById.Count > packAllowance)
        {
            return RestorationFailed(
                new LocalProfileRestorationFailure.EconomyRuleViolation(
                    EconomyViolationKind.PackAllowanceExceeded,
                    receiptsById.Count,
                    packAllowance
                )
            );
        }
        if (
            economy.StarterDeckClaimAllowance is { } claimAllowance
            && parsedClaims.Count > claimAllowance
        )
        {
            return RestorationFailed(
                new LocalProfileRestorationFailure.EconomyRuleViolation(
                    EconomyViolationKind.StarterDeckClaimAllowanceExceeded,
                    parsedClaims.Count,
                    claimAllowance
                )
            );
        }

        var baseProfile = new LocalProfile(
            profileId,
            displayName,
            snapshot.AuthorityManifestVersion,
            starterId,
            economy,
            ownership.ToImmutableDictionary(),
            receiptsByCommand.ToImmutableDictionary(),
            receiptsById.ToImmutableDictionary(),
            ImmutableDictionary<DeckId, SavedDeck>.Empty,
            ImmutableArray<StarterDeckClaim>.Empty
        );
        var savedDecks = new Dictionary<DeckId, SavedDeck>();
        for (var deckIndex = 0; deckIndex < deckSnapshots.Length; deckIndex++)
        {
            var restoredDeckResult = RestoreDeckSnapshot(
                deckSnapshots[deckIndex],
                $"SavedDecks[{deckIndex}]",
                baseProfile,
                currentAuthority,
                isCurrentAuthority
            );
            if (
                restoredDeckResult
                is DomainResult<SavedDeck, LocalProfileRestorationFailure>.Failed invalidDeck
            )
            {
                return RestorationFailed(invalidDeck.Error);
            }
            var deck = (
                (DomainResult<
                    SavedDeck,
                    LocalProfileRestorationFailure
                >.Succeeded)restoredDeckResult
            ).Value;

            if (!savedDecks.TryAdd(deck.Id, deck))
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.DeckId,
                        deck.Id.Value
                    )
                );
            }
        }

        var starterDeckClaims = ImmutableArray.CreateBuilder<StarterDeckClaim>(parsedClaims.Count);
        for (var claimIndex = 0; claimIndex < parsedClaims.Count; claimIndex++)
        {
            var parsedClaim = parsedClaims[claimIndex];
            var claimPath = $"{nameof(snapshot.StarterDeckClaims)}[{claimIndex}]";
            var claimDeckResult = RestoreDeckSnapshot(
                parsedClaim.Deck,
                $"{claimPath}.Deck",
                baseProfile,
                currentAuthority,
                true
            );
            if (
                claimDeckResult
                is DomainResult<SavedDeck, LocalProfileRestorationFailure>.Failed invalidClaimDeck
            )
            {
                return RestorationFailed(invalidClaimDeck.Error);
            }
            var claimDeck = (
                (DomainResult<SavedDeck, LocalProfileRestorationFailure>.Succeeded)claimDeckResult
            ).Value;

            if (claimDeck.Revision != DeckRevision.Initial)
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidDeckRevision(
                        claimDeck.Id,
                        claimDeck.Revision.Value
                    )
                );
            }
            if (!savedDecks.TryGetValue(claimDeck.Id, out var savedClaimDeck))
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.MissingEntry(
                        $"SavedDecks[{claimDeck.Id.Value}]"
                    )
                );
            }
            if (
                savedClaimDeck.Revision == DeckRevision.Initial
                && !SavedDecksMatch(savedClaimDeck, claimDeck)
            )
            {
                return RestorationFailed(
                    new LocalProfileRestorationFailure.InvalidSavedDeck(
                        claimDeck.Id,
                        ImmutableArray<DeckValidationIssue>.Empty
                    )
                );
            }

            // Every claim grants the starter's full collectible contents, so its
            // recorded grants must equal the claim deck's collectible quantities.
            var expectedGrants = claimDeck
                .Cards.Where(entry => authorityCollectibles.ContainsKey(entry.Key.Value))
                .ToDictionary(static entry => entry.Key, static entry => entry.Value);
            var recordedGrants = parsedClaim.Grants.ToDictionary(
                static grant => grant.CardId,
                static grant => grant.Quantity
            );
            if (
                recordedGrants.Count != expectedGrants.Count
                || recordedGrants.Any(entry =>
                    expectedGrants.GetValueOrDefault(entry.Key) != entry.Value
                )
            )
            {
                var mismatched = recordedGrants
                    .Keys.Concat(expectedGrants.Keys)
                    .Distinct()
                    .OrderBy(static cardId => cardId.Value, StringComparer.Ordinal)
                    .First(cardId =>
                        recordedGrants.GetValueOrDefault(cardId)
                        != expectedGrants.GetValueOrDefault(cardId)
                    );
                return RestorationFailed(
                    new LocalProfileRestorationFailure.OwnershipHistoryMismatch(
                        mismatched,
                        recordedGrants.GetValueOrDefault(mismatched),
                        expectedGrants.GetValueOrDefault(mismatched)
                    )
                );
            }

            starterDeckClaims.Add(
                new StarterDeckClaim(
                    parsedClaim.StarterDeckId,
                    parsedClaim.CommandId,
                    claimDeck,
                    parsedClaim.Grants
                )
            );
        }

        return DomainResult<LocalProfile, LocalProfileRestorationFailure>.Success(
            baseProfile.Copy(
                savedDecks: savedDecks.ToImmutableDictionary(),
                starterDeckClaims: starterDeckClaims.MoveToImmutable()
            )
        );
    }

    private static SavedDeckSnapshot ToSavedDeckSnapshot(SavedDeck deck) =>
        new(
            deck.Id.Value,
            deck.Name.Value,
            deck.Revision.Value,
            deck.Cards.OrderBy(static entry => entry.Key.Value, StringComparer.Ordinal)
                .Select(static entry => new SavedDeckCardSnapshot(entry.Key.Value, entry.Value))
                .ToImmutableArray()
        );

    private static StarterDeckClaimSnapshot ToStarterDeckClaimSnapshot(StarterDeckClaim claim) =>
        new(
            claim.Id.Value,
            claim.CommandId.Value,
            ToSavedDeckSnapshot(claim.Deck),
            claim
                .CollectibleGrants.OrderBy(
                    static grant => grant.CardId.Value,
                    StringComparer.Ordinal
                )
                .Select(static grant => new StarterCollectibleGrantSnapshot(
                    grant.CardId.Value,
                    grant.Quantity
                ))
                .ToImmutableArray()
        );

    private static DomainResult<SavedDeck, LocalProfileRestorationFailure> RestoreDeckSnapshot(
        SavedDeckSnapshot? item,
        string path,
        LocalProfile baseProfile,
        BlokemonRuntimeManifest currentAuthority,
        bool isCurrentAuthority
    )
    {
        if (item is null)
        {
            return DeckRestorationFailed(new LocalProfileRestorationFailure.MissingEntry(path));
        }

        var deckIdResult = DeckId.Create(item.DeckId);
        if (deckIdResult is DomainResult<DeckId, TextValueFailure>.Failed invalidDeckId)
        {
            return DeckRestorationFailed(
                new LocalProfileRestorationFailure.InvalidId(
                    $"{path}.{nameof(item.DeckId)}",
                    invalidDeckId.Error
                )
            );
        }
        var deckId = ((DomainResult<DeckId, TextValueFailure>.Succeeded)deckIdResult).Value;

        var deckNameResult = DeckName.Create(item.Name);
        if (deckNameResult is DomainResult<DeckName, TextValueFailure>.Failed invalidDeckName)
        {
            return DeckRestorationFailed(
                new LocalProfileRestorationFailure.InvalidDeckName(deckId, invalidDeckName.Error)
            );
        }
        var deckName = ((DomainResult<DeckName, TextValueFailure>.Succeeded)deckNameResult).Value;

        var revisionResult = DeckRevision.Create(item.Revision);
        if (revisionResult is DomainResult<DeckRevision, DeckRevisionFailure>.Failed)
        {
            return DeckRestorationFailed(
                new LocalProfileRestorationFailure.InvalidDeckRevision(deckId, item.Revision)
            );
        }
        var revision = (
            (DomainResult<DeckRevision, DeckRevisionFailure>.Succeeded)revisionResult
        ).Value;

        var cardSnapshots = OrEmpty(item.Cards);
        var selections = ImmutableArray.CreateBuilder<DeckCardSelection>(cardSnapshots.Length);
        var deckCards = new Dictionary<CardId, int>();
        for (var cardIndex = 0; cardIndex < cardSnapshots.Length; cardIndex++)
        {
            var card = cardSnapshots[cardIndex];
            if (card is null)
            {
                return DeckRestorationFailed(
                    new LocalProfileRestorationFailure.MissingEntry(
                        $"{path}.{nameof(item.Cards)}[{cardIndex}]"
                    )
                );
            }
            if (card.Quantity < 0)
            {
                return DeckRestorationFailed(
                    new LocalProfileRestorationFailure.NegativeQuantity(
                        $"{path}.{nameof(item.Cards)}[{cardIndex}].{nameof(card.Quantity)}",
                        card.Quantity
                    )
                );
            }

            var cardIdResult = CardId.Create(card.CardId);
            if (cardIdResult is DomainResult<CardId, TextValueFailure>.Failed invalidCardId)
            {
                return DeckRestorationFailed(
                    new LocalProfileRestorationFailure.InvalidId(
                        $"{path}.{nameof(item.Cards)}[{cardIndex}].{nameof(card.CardId)}",
                        invalidCardId.Error
                    )
                );
            }
            var cardId = ((DomainResult<CardId, TextValueFailure>.Succeeded)cardIdResult).Value;

            if (!deckCards.TryAdd(cardId, card.Quantity))
            {
                return DeckRestorationFailed(
                    new LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.DeckCardId,
                        cardId.Value
                    )
                );
            }
            selections.Add(new DeckCardSelection(cardId, card.Quantity));
        }

        if (isCurrentAuthority)
        {
            var validation = DeckValidator.Validate(baseProfile, currentAuthority, selections);
            if (validation is DeckValidationResult.Invalid invalidDeck)
            {
                return DeckRestorationFailed(
                    new LocalProfileRestorationFailure.InvalidSavedDeck(deckId, invalidDeck.Issues)
                );
            }
            deckCards = ((DeckValidationResult.Valid)validation).Deck.Cards.ToDictionary();
        }
        else if (selections.Any(static selection => selection.Quantity == 0))
        {
            var zeroQuantityIssues = selections
                .Where(static selection => selection.Quantity == 0)
                .Select(static selection =>
                    (DeckValidationIssue)
                        new DeckValidationIssue.QuantityMustBePositive(
                            selection.CardId,
                            selection.Quantity
                        )
                )
                .ToImmutableArray();
            return DeckRestorationFailed(
                new LocalProfileRestorationFailure.InvalidSavedDeck(deckId, zeroQuantityIssues)
            );
        }

        return DomainResult<SavedDeck, LocalProfileRestorationFailure>.Success(
            new SavedDeck(deckId, deckName, revision, deckCards.ToImmutableDictionary())
        );
    }

    private static bool SavedDecksMatch(SavedDeck left, SavedDeck right) =>
        left.Id == right.Id
        && left.Name == right.Name
        && left.Revision == right.Revision
        && left.Cards.Count == right.Cards.Count
        && left.Cards.All(entry =>
            right.Cards.TryGetValue(entry.Key, out var quantity) && entry.Value == quantity
        );

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> values) =>
        values.IsDefault ? ImmutableArray<T>.Empty : values;

    private static DomainResult<LocalProfile, LocalProfileRestorationFailure> RestorationFailed(
        LocalProfileRestorationFailure failure
    ) => DomainResult<LocalProfile, LocalProfileRestorationFailure>.Failure(failure);

    private static DomainResult<SavedDeck, LocalProfileRestorationFailure> DeckRestorationFailed(
        LocalProfileRestorationFailure failure
    ) => DomainResult<SavedDeck, LocalProfileRestorationFailure>.Failure(failure);
}

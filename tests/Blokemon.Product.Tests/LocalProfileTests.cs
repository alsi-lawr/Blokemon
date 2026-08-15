using Blokemon.Core.SetDesign;

namespace Blokemon.Product.Tests;

public sealed class LocalProfileTests
{
    private static readonly Lazy<BlokemonRuntimeManifest> _authority = new(() =>
        BlokemonSetJson.RuntimeManifest(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")
            )
        )
    );

    [Test]
    public async Task DisplayNameCreation_TrimsAndAcceptsThirtyTwoCharacters()
    {
        var boundary = new string('a', DisplayName.MaximumLength);

        var result = Success(DisplayName.Create($"  {boundary}\t"));

        await Assert.That(result.Value).IsEqualTo(boundary);
        await Assert.That(result.Value.Length).IsEqualTo(DisplayName.MaximumLength);
    }

    [Test]
    public async Task DisplayNameCreation_RejectsMissingAndOverlongValues()
    {
        var missing = Failure(DisplayName.Create(" \t "));
        var overlong = Failure(DisplayName.Create(new string('a', DisplayName.MaximumLength + 1)));

        await Assert.That(missing).IsEqualTo(DisplayNameCreationFailure.Required);
        await Assert.That(overlong).IsEqualTo(DisplayNameCreationFailure.TooLong);
    }

    [Test]
    public async Task DuplicatePackCommand_ReturnsPersistedReceiptWithoutSamplingOrDoubleApplying()
    {
        var profile = CreateProfile();
        var firstRandom = new BlokemonSeededRandom(41);
        var first = Success(
            profile.OpenPack(
                Value(CommandId.Create("command-1")),
                Value(PackReceiptId.Create("receipt-1")),
                _authority.Value,
                firstRandom
            )
        );
        var retryRandom = new CountingRandomSource();

        var retried = Success(
            first.Profile.OpenPack(
                Value(CommandId.Create("command-1")),
                Value(PackReceiptId.Create("ignored-receipt")),
                _authority.Value,
                retryRandom
            )
        );

        await Assert.That(first.Disposition).IsEqualTo(PackOpenDisposition.Opened);
        await Assert.That(retried.Disposition).IsEqualTo(PackOpenDisposition.AlreadyOpened);
        await Assert.That(retried.Profile).IsSameReferenceAs(first.Profile);
        await Assert.That(retried.Receipt).IsSameReferenceAs(first.Receipt);
        await Assert.That(retried.Receipt.Id.Value).IsEqualTo("receipt-1");
        await Assert.That(retried.Receipt.SampledCollectibleIds.Length).IsEqualTo(11);
        await Assert
            .That(
                retried.Receipt.SampledCollectibleIds.SequenceEqual(
                    first.Receipt.SampledCollectibleIds
                )
            )
            .IsTrue();
        foreach (var group in first.Receipt.SampledCollectibleIds.GroupBy(static id => id))
        {
            var initialQuantity = group.Key == profile.GuaranteedRegularCollectibleId ? 1 : 0;
            await Assert
                .That(first.Profile.OwnedCollectibleQuantity(group.Key))
                .IsEqualTo(initialQuantity + group.Count());
        }
        await Assert.That(retried.Profile.AvailablePackEntitlements).IsEqualTo(9);
        await Assert.That(retried.Profile.PackReceipts.Count).IsEqualTo(1);
        await Assert.That(retryRandom.ConsumptionIndex).IsEqualTo(0);
    }

    [Test]
    public async Task UnavailableEntitlement_DoesNotSampleOrChangeExhaustedProfile()
    {
        var profile = CreateProfile();
        for (var index = 0; index < LocalProfile.InitialPackEntitlementCount; index++)
        {
            profile = Success(
                profile.OpenPack(
                    Value(CommandId.Create($"command-{index}")),
                    Value(PackReceiptId.Create($"receipt-{index}")),
                    _authority.Value,
                    new BlokemonSeededRandom((ulong)index)
                )
            ).Profile;
        }

        var random = new CountingRandomSource();
        var result = profile.OpenPack(
            Value(CommandId.Create("command-unavailable")),
            Value(PackReceiptId.Create("receipt-unavailable")),
            _authority.Value,
            random
        );

        var failure = Failure(result);
        await Assert.That(failure).IsEqualTo(PackOpenFailure.EntitlementUnavailable);
        await Assert.That(profile.AvailablePackEntitlements).IsEqualTo(0);
        await Assert
            .That(profile.CollectibleOwnership.Values.Sum())
            .IsEqualTo(1 + LocalProfile.InitialPackEntitlementCount * 11);
        await Assert.That(profile.PackReceipts.Count).IsEqualTo(10);
        await Assert.That(random.ConsumptionIndex).IsEqualTo(0);
    }

    [Test]
    public async Task DeckValidation_UsesOwnedCollectiblesAndFreeCatalogueCards()
    {
        var profile = CreateProfile();
        var regular = profile.GuaranteedRegularCollectibleId;
        var kit = Value(CardId.Create(_authority.Value.Kits[0].Id));
        var vim = Value(CardId.Create(_authority.Value.BasicVim[0].Id));

        var legal = DeckValidator.Validate(
            profile,
            _authority.Value,
            [new(regular, 1), new(kit, 4), new(vim, 55)]
        );
        var overOwned = InvalidIssues(
            DeckValidator.Validate(profile, _authority.Value, [new(regular, 2), new(vim, 58)])
        );
        var overMechanicalLimit = InvalidIssues(
            DeckValidator.Validate(
                profile,
                _authority.Value,
                [new(regular, 1), new(kit, 5), new(vim, 54)]
            )
        );

        await Assert.That(legal.IsValid).IsTrue();
        await Assert
            .That(
                overOwned.Any(static issue =>
                    issue is DeckValidationIssue.CollectibleQuantityNotOwned
                )
            )
            .IsTrue();
        await Assert
            .That(
                overMechanicalLimit.Any(static issue =>
                    issue is DeckValidationIssue.MechanicalCopyLimitExceeded
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task DeckValidation_RequiresExactlySixtyCardsAndARegularCollectible()
    {
        var profile = CreateProfile();
        var vim = Value(CardId.Create(_authority.Value.BasicVim[0].Id));
        var wrongCount = InvalidIssues(
            DeckValidator.Validate(
                profile,
                _authority.Value,
                [new(profile.GuaranteedRegularCollectibleId, 1), new(vim, 58)]
            )
        );
        var noRegular = InvalidIssues(
            DeckValidator.Validate(profile, _authority.Value, [new(vim, 60)])
        );

        await Assert
            .That(wrongCount.Any(static issue => issue is DeckValidationIssue.WrongCardCount))
            .IsTrue();
        await Assert
            .That(
                noRegular.Any(static issue =>
                    issue is DeckValidationIssue.RegularCollectibleRequired
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task StaleDeckRevision_IsRejectedWithoutOverwritingSavedDeck()
    {
        var profile = CreateProfile();
        var deckId = Value(DeckId.Create("deck-1"));
        var cards = LegalCards(profile);
        var created = Success(
            profile.CreateDeck(deckId, Value(DeckName.Create("First")), cards, _authority.Value)
        );
        var revised = Success(
            created.Profile.ReviseDeck(
                deckId,
                created.Deck.Revision,
                Value(DeckName.Create("Second")),
                cards,
                _authority.Value
            )
        );

        var stale = revised.Profile.ReviseDeck(
            deckId,
            created.Deck.Revision,
            Value(DeckName.Create("Stale overwrite")),
            cards,
            _authority.Value
        );

        var failure = Failure(stale);
        await Assert.That(failure).IsTypeOf<DeckSaveFailure.StaleRevision>();
        var staleRevision = (DeckSaveFailure.StaleRevision)failure;
        await Assert.That(staleRevision.DeckId).IsEqualTo(deckId);
        await Assert.That(staleRevision.ExpectedRevision).IsEqualTo(created.Deck.Revision);
        await Assert.That(staleRevision.ActualRevision).IsEqualTo(revised.Deck.Revision);
        await Assert.That(revised.Profile.SavedDecks[deckId].Name.Value).IsEqualTo("Second");
        await Assert.That(revised.Profile.SavedDecks[deckId].Revision.Value).IsEqualTo(2);
    }

    [Test]
    public async Task SnapshotRestore_RehydratesReceiptsOwnershipAndRevisedDecks()
    {
        var profile = CreatePopulatedProfile();
        var snapshot = profile.ToSnapshot();

        var restored = Success(LocalProfile.Restore(snapshot, _authority.Value));
        var restoredSnapshot = restored.ToSnapshot();
        var firstReceipt = snapshot.PackReceipts[0];
        var retryRandom = new CountingRandomSource();
        var retried = Success(
            restored.OpenPack(
                Value(CommandId.Create(firstReceipt.CommandId)),
                Value(PackReceiptId.Create("ignored-after-restore")),
                _authority.Value,
                retryRandom
            )
        );

        await Assert
            .That(restored.BoundAuthorityManifestVersion)
            .IsEqualTo(snapshot.AuthorityManifestVersion);
        await Assert
            .That(
                restoredSnapshot.CollectibleOwnership.SequenceEqual(snapshot.CollectibleOwnership)
            )
            .IsTrue();
        await Assert.That(restoredSnapshot.PackReceipts.Length).IsEqualTo(2);
        for (var index = 0; index < snapshot.PackReceipts.Length; index++)
        {
            await Assert
                .That(restoredSnapshot.PackReceipts[index].ReceiptId)
                .IsEqualTo(snapshot.PackReceipts[index].ReceiptId);
            await Assert
                .That(restoredSnapshot.PackReceipts[index].CommandId)
                .IsEqualTo(snapshot.PackReceipts[index].CommandId);
            await Assert.That(restoredSnapshot.PackReceipts[index].Sequence).IsEqualTo(index + 1);
            await Assert
                .That(
                    restoredSnapshot
                        .PackReceipts[index]
                        .SampledCollectibleIds.SequenceEqual(
                            snapshot.PackReceipts[index].SampledCollectibleIds
                        )
                )
                .IsTrue();
        }
        await Assert.That(restoredSnapshot.SavedDecks.Length).IsEqualTo(1);
        await Assert.That(restoredSnapshot.SavedDecks[0].Revision).IsEqualTo(2);
        await Assert.That(restoredSnapshot.SavedDecks[0].Name).IsEqualTo("Revised deck");
        await Assert
            .That(restoredSnapshot.SavedDecks[0].Cards.SequenceEqual(snapshot.SavedDecks[0].Cards))
            .IsTrue();
        await Assert.That(retried.Disposition).IsEqualTo(PackOpenDisposition.AlreadyOpened);
        await Assert
            .That(restored.PackReceipts.Values.Max(static receipt => receipt.Sequence))
            .IsEqualTo(2);
        await Assert.That(retryRandom.ConsumptionIndex).IsEqualTo(0);
    }

    [Test]
    public async Task HistoricalProfile_PreservesPackBindingWhileDecksUseCurrentMechanics()
    {
        var snapshot = CreatePopulatedProfile().ToSnapshot();
        var firstReceipt = snapshot.PackReceipts[0];
        var replacedCardId = firstReceipt.SampledCollectibleIds[0]!;
        const string historicalCardId = "HISTORICAL-COLLECTIBLE";
        var historicalReceipt = firstReceipt with
        {
            SampledCollectibleIds = firstReceipt.SampledCollectibleIds.SetItem(0, historicalCardId),
        };
        var ownership = snapshot.CollectibleOwnership.ToBuilder();
        var replacedOwnershipIndex = Enumerable
            .Range(0, ownership.Count)
            .Single(index => ownership[index].CardId == replacedCardId);
        ownership[replacedOwnershipIndex] = ownership[replacedOwnershipIndex] with
        {
            Quantity = ownership[replacedOwnershipIndex].Quantity - 1,
        };
        ownership.Add(new CollectibleOwnershipSnapshot(historicalCardId, 1));
        var historicalDeck = snapshot.SavedDecks[0] with
        {
            Cards =
            [
                new SavedDeckCardSnapshot(snapshot.GuaranteedRegularCollectibleId, 1),
                new SavedDeckCardSnapshot("HISTORICAL-DECK-CARD", 59),
            ],
        };
        var historicalSnapshot = snapshot with
        {
            AuthorityManifestVersion = "historical-manifest",
            CollectibleOwnership = ownership.ToImmutable(),
            PackReceipts = snapshot.PackReceipts.SetItem(0, historicalReceipt),
            SavedDecks = snapshot.SavedDecks.SetItem(0, historicalDeck),
        };

        var restored = Success(LocalProfile.Restore(historicalSnapshot, _authority.Value));
        var restoredSnapshot = restored.ToSnapshot();
        var historicalDeckId = Value(DeckId.Create(historicalDeck.DeckId));
        var currentCards = LegalCards(restored);
        var revised = Success(
            restored.ReviseDeck(
                historicalDeckId,
                restored.SavedDecks[historicalDeckId].Revision,
                Value(DeckName.Create("Current deck")),
                currentCards,
                _authority.Value
            )
        );
        var created = Success(
            revised.Profile.CreateDeck(
                Value(DeckId.Create("current-deck")),
                Value(DeckName.Create("Another current deck")),
                currentCards,
                _authority.Value
            )
        );
        var random = new CountingRandomSource();
        var packFailure = Failure(
            created.Profile.OpenPack(
                Value(CommandId.Create("blocked-command")),
                Value(PackReceiptId.Create("blocked-receipt")),
                _authority.Value,
                random
            )
        );

        await Assert.That(restored.BoundAuthorityManifestVersion).IsEqualTo("historical-manifest");
        await Assert
            .That(
                restoredSnapshot.CollectibleOwnership.Any(static item =>
                    item.CardId == historicalCardId && item.Quantity == 1
                )
            )
            .IsTrue();
        await Assert
            .That(restoredSnapshot.PackReceipts[0].SampledCollectibleIds.Contains(historicalCardId))
            .IsTrue();
        await Assert
            .That(
                restoredSnapshot
                    .SavedDecks[0]
                    .Cards.Any(static card =>
                        card.CardId == "HISTORICAL-DECK-CARD" && card.Quantity == 59
                    )
            )
            .IsTrue();
        await Assert
            .That(revised.Deck.Cards.Keys.Select(static cardId => cardId.Value))
            .IsEquivalentTo(currentCards.Select(static selection => selection.CardId.Value));
        await Assert.That(revised.Deck.Revision.Value).IsEqualTo(historicalDeck.Revision + 1);
        await Assert.That(created.Deck.Cards.Keys).IsEquivalentTo(revised.Deck.Cards.Keys);
        await Assert
            .That(created.Profile.BoundAuthorityManifestVersion)
            .IsEqualTo("historical-manifest");
        await Assert.That(packFailure).IsEqualTo(PackOpenFailure.AuthorityVersionMismatch);
        await Assert.That(random.ConsumptionIndex).IsEqualTo(0);
    }

    [Test]
    public async Task SnapshotRestore_RejectsInvalidIdentityQuantitiesAndDuplicates()
    {
        var snapshot = CreatePopulatedProfile().ToSnapshot();
        var invalidIdentity = Failure(
            LocalProfile.Restore(snapshot with { ProfileId = " " }, _authority.Value)
        );
        var negativeQuantity = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    CollectibleOwnership = snapshot.CollectibleOwnership.SetItem(
                        0,
                        snapshot.CollectibleOwnership[0] with
                        {
                            Quantity = -1,
                        }
                    ),
                },
                _authority.Value
            )
        );
        var negativeEntitlements = Failure(
            LocalProfile.Restore(snapshot with { AvailablePackEntitlements = -1 }, _authority.Value)
        );
        var duplicateReceipt = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    PackReceipts = snapshot.PackReceipts.SetItem(
                        1,
                        snapshot.PackReceipts[1] with
                        {
                            ReceiptId = snapshot.PackReceipts[0].ReceiptId,
                        }
                    ),
                },
                _authority.Value
            )
        );
        var duplicateCommand = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    PackReceipts = snapshot.PackReceipts.SetItem(
                        1,
                        snapshot.PackReceipts[1] with
                        {
                            CommandId = snapshot.PackReceipts[0].CommandId,
                        }
                    ),
                },
                _authority.Value
            )
        );
        var duplicateSequence = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    PackReceipts = snapshot.PackReceipts.SetItem(
                        1,
                        snapshot.PackReceipts[1] with
                        {
                            Sequence = snapshot.PackReceipts[0].Sequence,
                        }
                    ),
                },
                _authority.Value
            )
        );
        var nonPositiveSequence = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    PackReceipts = snapshot.PackReceipts.SetItem(
                        0,
                        snapshot.PackReceipts[0] with
                        {
                            Sequence = 0,
                        }
                    ),
                },
                _authority.Value
            )
        );
        var gappedSequence = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    PackReceipts = snapshot.PackReceipts.SetItem(
                        1,
                        snapshot.PackReceipts[1] with
                        {
                            Sequence = 3,
                        }
                    ),
                },
                _authority.Value
            )
        );

        await Assert.That(invalidIdentity).IsTypeOf<LocalProfileRestorationFailure.InvalidId>();
        await Assert
            .That(negativeQuantity)
            .IsTypeOf<LocalProfileRestorationFailure.NegativeQuantity>();
        await Assert
            .That(negativeEntitlements)
            .IsTypeOf<LocalProfileRestorationFailure.NegativeQuantity>();
        await Assert
            .That(duplicateReceipt)
            .IsTypeOf<LocalProfileRestorationFailure.DuplicateValue>();
        await Assert
            .That(((LocalProfileRestorationFailure.DuplicateValue)duplicateReceipt).Kind)
            .IsEqualTo(SnapshotDuplicateKind.PackReceiptId);
        await Assert
            .That(((LocalProfileRestorationFailure.DuplicateValue)duplicateCommand).Kind)
            .IsEqualTo(SnapshotDuplicateKind.PackCommandId);
        await Assert
            .That(duplicateSequence)
            .IsTypeOf<LocalProfileRestorationFailure.InvalidPackSequence>();
        await Assert
            .That(nonPositiveSequence)
            .IsTypeOf<LocalProfileRestorationFailure.InvalidPackSequence>();
        await Assert
            .That(gappedSequence)
            .IsTypeOf<LocalProfileRestorationFailure.InvalidPackSequence>();
    }

    [Test]
    public async Task SnapshotRestore_RejectsCurrentAuthorityAndDeckClaimsThatAreInvalid()
    {
        var snapshot = CreatePopulatedProfile().ToSnapshot();
        var unknownCurrentCard = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    CollectibleOwnership = snapshot.CollectibleOwnership.Add(
                        new CollectibleOwnershipSnapshot("UNKNOWN-CURRENT-CARD", 0)
                    ),
                },
                _authority.Value
            )
        );
        var invalidRevision = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    SavedDecks = snapshot.SavedDecks.SetItem(
                        0,
                        snapshot.SavedDecks[0] with
                        {
                            Revision = 0,
                        }
                    ),
                },
                _authority.Value
            )
        );
        var vimId = _authority.Value.BasicVim[0].Id;
        var illegalDeck = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    SavedDecks = snapshot.SavedDecks.SetItem(
                        0,
                        snapshot.SavedDecks[0] with
                        {
                            Cards = [new SavedDeckCardSnapshot(vimId, 60)],
                        }
                    ),
                },
                _authority.Value
            )
        );

        await Assert
            .That(unknownCurrentCard)
            .IsTypeOf<LocalProfileRestorationFailure.UnknownCard>();
        await Assert
            .That(invalidRevision)
            .IsTypeOf<LocalProfileRestorationFailure.InvalidDeckRevision>();
        await Assert.That(illegalDeck).IsTypeOf<LocalProfileRestorationFailure.InvalidSavedDeck>();
    }

    private static LocalProfile CreateProfile() =>
        Success(
            LocalProfile.Create(
                Value(ProfileId.Create("profile-1")),
                Success(DisplayName.Create("Local Player")),
                _authority.Value
            )
        );

    private static LocalProfile CreatePopulatedProfile()
    {
        var profile = CreateProfile();
        for (var index = 0; index < 2; index++)
        {
            profile = Success(
                profile.OpenPack(
                    Value(CommandId.Create($"snapshot-command-{index}")),
                    Value(PackReceiptId.Create($"snapshot-receipt-{index}")),
                    _authority.Value,
                    new BlokemonSeededRandom((ulong)(100 + index))
                )
            ).Profile;
        }

        var deckId = Value(DeckId.Create("snapshot-deck"));
        var created = Success(
            profile.CreateDeck(
                deckId,
                Value(DeckName.Create("Initial deck")),
                LegalCards(profile),
                _authority.Value
            )
        );
        return Success(
            created.Profile.ReviseDeck(
                deckId,
                created.Deck.Revision,
                Value(DeckName.Create("Revised deck")),
                LegalCards(profile),
                _authority.Value
            )
        ).Profile;
    }

    private static DeckCardSelection[] LegalCards(LocalProfile profile) =>
        [
            new(profile.GuaranteedRegularCollectibleId, 1),
            new(Value(CardId.Create(_authority.Value.BasicVim[0].Id)), 59),
        ];

    private static TSuccess Success<TSuccess, TFailure>(DomainResult<TSuccess, TFailure> result)
        where TSuccess : notnull
        where TFailure : notnull =>
        result.Match(
            static value => value,
            static failure =>
                throw new InvalidOperationException($"Expected success, received {failure}.")
        );

    private static TFailure Failure<TSuccess, TFailure>(DomainResult<TSuccess, TFailure> result)
        where TSuccess : notnull
        where TFailure : notnull =>
        result.Match(
            static _ => throw new InvalidOperationException("Expected failure."),
            static failure => failure
        );

    private static TValue Value<TValue>(DomainResult<TValue, TextValueFailure> result)
        where TValue : notnull => Success(result);

    private static IReadOnlyList<DeckValidationIssue> InvalidIssues(DeckValidationResult result) =>
        result.Match(
            static _ => throw new InvalidOperationException("Expected invalid deck."),
            static issues => issues
        );

    private sealed class CountingRandomSource : IBlokemonRandomSource
    {
        public int ConsumptionIndex { get; private set; }

        public int NextInt(int exclusiveMaximum)
        {
            ConsumptionIndex++;
            return 0;
        }
    }
}

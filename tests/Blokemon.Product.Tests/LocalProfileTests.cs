using Blokemon.Core.SetDesign;
using Shouldly;

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

        result.Value.ShouldBe(boundary);
        result.Value.Length.ShouldBe(DisplayName.MaximumLength);
    }

    [Test]
    public async Task DisplayNameCreation_RejectsMissingAndOverlongValues()
    {
        var missing = Failure(DisplayName.Create(" \t "));
        var overlong = Failure(DisplayName.Create(new string('a', DisplayName.MaximumLength + 1)));

        missing.ShouldBe(DisplayNameCreationFailure.Required);
        overlong.ShouldBe(DisplayNameCreationFailure.TooLong);
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

        first.Disposition.ShouldBe(PackOpenDisposition.Opened);
        retried.Disposition.ShouldBe(PackOpenDisposition.AlreadyOpened);
        retried.Profile.ShouldBeSameAs(first.Profile);
        retried.Receipt.ShouldBeSameAs(first.Receipt);
        retried.Receipt.Id.Value.ShouldBe("receipt-1");
        retried.Receipt.SampledCollectibleIds.Length.ShouldBe(11);
        retried.Receipt.SampledCollectibleIds.SequenceEqual(
            first.Receipt.SampledCollectibleIds
        ).ShouldBeTrue();
        foreach (var group in first.Receipt.SampledCollectibleIds.GroupBy(static id => id))
        {
            var initialQuantity = group.Key == profile.GuaranteedRegularCollectibleId ? 1 : 0;
            first.Profile.OwnedCollectibleQuantity(group.Key)
                .ShouldBe(initialQuantity + group.Count());
        }
        retried.Profile.PackReceipts.Count.ShouldBe(1);
        retryRandom.ConsumptionIndex.ShouldBe(0);
    }

    [Test]
    public async Task OpeningPacksBeyondTheFormerTenPackLimit_KeepsGrantingSamplesInSequence()
    {
        var profile = CreateProfile();
        for (var index = 0; index < 12; index++)
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

        profile.PackReceipts.Count.ShouldBe(12);
        profile.PackReceipts.Values.Select(static receipt => receipt.Sequence)
            .ShouldBe(Enumerable.Range(1, 12), ignoreOrder: true);
        profile.CollectibleOwnership.Values.Sum().ShouldBe(1 + 12 * 11);
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

        legal.IsValid.ShouldBeTrue();
        overOwned.Any(static issue =>
            issue is DeckValidationIssue.CollectibleQuantityNotOwned
        ).ShouldBeTrue();
        overMechanicalLimit.Any(static issue =>
            issue is DeckValidationIssue.MechanicalCopyLimitExceeded
        ).ShouldBeTrue();
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

        wrongCount.Any(static issue => issue is DeckValidationIssue.WrongCardCount).ShouldBeTrue();
        noRegular.Any(static issue =>
            issue is DeckValidationIssue.RegularCollectibleRequired
        ).ShouldBeTrue();
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
        failure.ShouldBeOfType<DeckSaveFailure.StaleRevision>();
        var staleRevision = (DeckSaveFailure.StaleRevision)failure;
        staleRevision.DeckId.ShouldBe(deckId);
        staleRevision.ExpectedRevision.ShouldBe(created.Deck.Revision);
        staleRevision.ActualRevision.ShouldBe(revised.Deck.Revision);
        revised.Profile.SavedDecks[deckId].Name.Value.ShouldBe("Second");
        revised.Profile.SavedDecks[deckId].Revision.Value.ShouldBe(2);
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

        restored.BoundAuthorityManifestVersion.ShouldBe(snapshot.AuthorityManifestVersion);
        restoredSnapshot.CollectibleOwnership.SequenceEqual(snapshot.CollectibleOwnership)
            .ShouldBeTrue();
        restoredSnapshot.PackReceipts.Length.ShouldBe(2);
        for (var index = 0; index < snapshot.PackReceipts.Length; index++)
        {
            restoredSnapshot.PackReceipts[index].ReceiptId
                .ShouldBe(snapshot.PackReceipts[index].ReceiptId);
            restoredSnapshot.PackReceipts[index].CommandId
                .ShouldBe(snapshot.PackReceipts[index].CommandId);
            restoredSnapshot.PackReceipts[index].Sequence.ShouldBe(index + 1);
            restoredSnapshot
                .PackReceipts[index]
                .SampledCollectibleIds.SequenceEqual(
                    snapshot.PackReceipts[index].SampledCollectibleIds
                ).ShouldBeTrue();
        }
        restoredSnapshot.SavedDecks.Length.ShouldBe(1);
        restoredSnapshot.SavedDecks[0].Revision.ShouldBe(2);
        restoredSnapshot.SavedDecks[0].Name.ShouldBe("Revised deck");
        restoredSnapshot.SavedDecks[0].Cards.SequenceEqual(snapshot.SavedDecks[0].Cards)
            .ShouldBeTrue();
        retried.Disposition.ShouldBe(PackOpenDisposition.AlreadyOpened);
        restored.PackReceipts.Values.Max(static receipt => receipt.Sequence).ShouldBe(2);
        retryRandom.ConsumptionIndex.ShouldBe(0);
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

        restored.BoundAuthorityManifestVersion.ShouldBe("historical-manifest");
        restoredSnapshot.CollectibleOwnership.Any(static item =>
            item.CardId == historicalCardId && item.Quantity == 1
        ).ShouldBeTrue();
        restoredSnapshot.PackReceipts[0].SampledCollectibleIds.Contains(historicalCardId)
            .ShouldBeTrue();
        restoredSnapshot
            .SavedDecks[0]
            .Cards.Any(static card =>
                card.CardId == "HISTORICAL-DECK-CARD" && card.Quantity == 59
            ).ShouldBeTrue();
        revised.Deck.Cards.Keys.Select(static cardId => cardId.Value)
            .ShouldBe(
                currentCards.Select(static selection => selection.CardId.Value),
                ignoreOrder: true
            );
        revised.Deck.Revision.Value.ShouldBe(historicalDeck.Revision + 1);
        created.Deck.Cards.Keys.ShouldBe(revised.Deck.Cards.Keys, ignoreOrder: true);
        created.Profile.BoundAuthorityManifestVersion.ShouldBe("historical-manifest");
        packFailure.ShouldBe(PackOpenFailure.AuthorityVersionMismatch);
        random.ConsumptionIndex.ShouldBe(0);
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

        invalidIdentity.ShouldBeOfType<LocalProfileRestorationFailure.InvalidId>();
        negativeQuantity.ShouldBeOfType<LocalProfileRestorationFailure.NegativeQuantity>();
        duplicateReceipt.ShouldBeOfType<LocalProfileRestorationFailure.DuplicateValue>();
        ((LocalProfileRestorationFailure.DuplicateValue)duplicateReceipt).Kind
            .ShouldBe(SnapshotDuplicateKind.PackReceiptId);
        ((LocalProfileRestorationFailure.DuplicateValue)duplicateCommand).Kind
            .ShouldBe(SnapshotDuplicateKind.PackCommandId);
        duplicateSequence.ShouldBeOfType<LocalProfileRestorationFailure.InvalidPackSequence>();
        nonPositiveSequence.ShouldBeOfType<LocalProfileRestorationFailure.InvalidPackSequence>();
        gappedSequence.ShouldBeOfType<LocalProfileRestorationFailure.InvalidPackSequence>();
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

        unknownCurrentCard.ShouldBeOfType<LocalProfileRestorationFailure.UnknownCard>();
        invalidRevision.ShouldBeOfType<LocalProfileRestorationFailure.InvalidDeckRevision>();
        illegalDeck.ShouldBeOfType<LocalProfileRestorationFailure.InvalidSavedDeck>();
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

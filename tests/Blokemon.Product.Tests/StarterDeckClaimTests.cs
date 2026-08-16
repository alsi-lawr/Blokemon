using System.Collections.Immutable;
using Blokemon.Core.SetDesign;
using Shouldly;

namespace Blokemon.Product.Tests;

public sealed class StarterDeckClaimTests
{
    private static readonly Lazy<BlokemonRuntimeManifest> _authority = new(() =>
        BlokemonSetJson.RuntimeManifest(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")
            )
        )
    );

    [Test]
    public async Task ClaimStarterDeck_AtomicallyGrantsFullCollectibleContentsAndCreatesEditableDeck()
    {
        // The claim records that a starter was claimed and what it granted. The deck it
        // creates is an ordinary saved deck, so revising it leaves the claim untouched.
        var profile = CreateProfile();
        var fixture = CreateStarterFixture(profile);
        var commandId = Value(CommandId.Create("starter-command"));
        var invalidDefinition = new StarterDeckDefinition(
            fixture.Definition.Id,
            fixture.Definition.DeckId,
            fixture.Definition.DeckName,
            fixture.Definition.Cards.Select(selection =>
                selection.CardId == fixture.BasicVimId
                    ? selection with
                    {
                        Quantity = selection.Quantity - 1,
                    }
                    : selection
            )
        );

        var invalid = Failure(
            profile.ClaimStarterDeck(commandId, invalidDefinition, _authority.Value)
        );

        invalid.ShouldBeOfType<StarterDeckClaimFailure.InvalidDeck>();
        profile.StarterDeckClaims.ShouldBeEmpty();
        profile.LatestStarterDeckClaim.ShouldBeNull();
        profile.SavedDecks.ShouldBeEmpty();
        profile.OwnedCollectibleQuantity(fixture.CollectibleId).ShouldBe(0);

        var claimed = (StarterDeckClaimOutcome.Claimed)Success(
            profile.ClaimStarterDeck(commandId, fixture.Definition, _authority.Value)
        );

        claimed.Profile.ShouldNotBeSameAs(profile);
        claimed.Profile.StarterDeckClaims.ShouldHaveSingleItem();
        claimed.Profile.LatestStarterDeckClaim.ShouldBeSameAs(claimed.Claim);
        claimed.Profile.SavedDecks.Count.ShouldBe(1);
        var starterDeck = claimed.Profile.SavedDecks[fixture.Definition.DeckId];
        starterDeck.Revision.ShouldBe(DeckRevision.Initial);
        starterDeck.Cards.Values.Sum().ShouldBe(60);
        claimed.Claim.Id.ShouldBe(fixture.Definition.Id);
        claimed.Claim.CommandId.ShouldBe(commandId);
        claimed.Claim.CollectibleGrants.Length.ShouldBe(2);
        claimed
            .Claim.CollectibleGrants.Single(grant => grant.CardId == fixture.CollectibleId)
            .Quantity.ShouldBe(fixture.RequiredCollectibleQuantity);
        claimed
            .Claim.CollectibleGrants.Single(grant =>
                grant.CardId == profile.GuaranteedRegularCollectibleId
            )
            .Quantity.ShouldBe(1);
        claimed
            .Profile.OwnedCollectibleQuantity(fixture.CollectibleId)
            .ShouldBe(fixture.RequiredCollectibleQuantity);
        claimed
            .Profile.OwnedCollectibleQuantity(claimed.Profile.GuaranteedRegularCollectibleId)
            .ShouldBe(2);
        claimed.Profile.OwnedCollectibleQuantity(fixture.KitId).ShouldBe(0);
        claimed.Profile.OwnedCollectibleQuantity(fixture.BasicVimId).ShouldBe(0);

        var revised = Success(
            claimed.Profile.ReviseDeck(
                fixture.Definition.DeckId,
                starterDeck.Revision,
                Value(DeckName.Create("Edited starter")),
                fixture.Definition.Cards,
                _authority.Value
            )
        );

        revised.Deck.Revision.Value.ShouldBe(2);
        revised.Deck.Name.Value.ShouldBe("Edited starter");
        revised.Profile.LatestStarterDeckClaim.ShouldBeSameAs(claimed.Claim);
        revised.Profile.StarterDeckClaims.ShouldHaveSingleItem();
    }

    [Test]
    public async Task ClaimStarterDeck_ExactRetryIsIdempotentWhileConflictsAreTypedFailures()
    {
        var profile = CreateProfile();
        var fixture = CreateStarterFixture(profile);
        var commandId = Value(CommandId.Create("starter-command"));
        var claimed = (StarterDeckClaimOutcome.Claimed)Success(
            profile.ClaimStarterDeck(commandId, fixture.Definition, _authority.Value)
        );
        // Replaying the command identifies the claim by its starter alone: the same starter
        // is the same claim however its deck payload is spelled, a different starter is a
        // conflicting reuse of the command id.
        var equivalentDefinition = new StarterDeckDefinition(
            fixture.Definition.Id,
            fixture.Definition.DeckId,
            Value(DeckName.Create("Renamed starter payload")),
            fixture.Definition.Cards.Reverse()
        );

        var retried = (StarterDeckClaimOutcome.AlreadyClaimed)Success(
            claimed.Profile.ClaimStarterDeck(commandId, equivalentDefinition, _authority.Value)
        );
        var commandConflict = Failure(
            claimed.Profile.ClaimStarterDeck(
                commandId,
                SecondStarterDefinition(profile, fixture),
                _authority.Value
            )
        );

        retried.Profile.ShouldBeSameAs(claimed.Profile);
        retried.Claim.ShouldBeSameAs(claimed.Claim);
        retried.Profile.SavedDecks.Count.ShouldBe(1);
        retried
            .Profile.OwnedCollectibleQuantity(fixture.CollectibleId)
            .ShouldBe(fixture.RequiredCollectibleQuantity);
        var conflict = commandConflict.ShouldBeOfType<StarterDeckClaimFailure.CommandConflict>();
        conflict.ClaimedStarterDeckId.ShouldBe(fixture.Definition.Id);
        conflict.RequestedStarterDeckId.Value.ShouldBe("starter-beta");
        claimed.Profile.StarterDeckClaims.ShouldHaveSingleItem();
        claimed.Profile.SavedDecks.Count.ShouldBe(1);
        claimed.Profile.LatestStarterDeckClaim.ShouldBeSameAs(claimed.Claim);
    }

    [Test]
    public async Task ClaimingADifferentStarter_AddsASecondClaimAndItsDeck()
    {
        var profile = CreateProfile();
        var fixture = CreateStarterFixture(profile);
        var second = SecondStarterDefinition(profile, fixture);
        var claimedAlpha = (StarterDeckClaimOutcome.Claimed)Success(
            profile.ClaimStarterDeck(
                Value(CommandId.Create("starter-command-alpha")),
                fixture.Definition,
                _authority.Value
            )
        );

        var claimedBeta = (StarterDeckClaimOutcome.Claimed)Success(
            claimedAlpha.Profile.ClaimStarterDeck(
                Value(CommandId.Create("starter-command-beta")),
                second,
                _authority.Value
            )
        );

        claimedBeta.Profile.StarterDeckClaims.Length.ShouldBe(2);
        claimedBeta.Profile.StarterDeckClaims[0].ShouldBeSameAs(claimedAlpha.Claim);
        claimedBeta.Profile.LatestStarterDeckClaim.ShouldBeSameAs(claimedBeta.Claim);
        claimedBeta.Profile.SavedDecks.Count.ShouldBe(2);
        claimedBeta.Profile.SavedDecks[second.DeckId].Cards.Values.Sum().ShouldBe(60);
        claimedBeta
            .Profile.OwnedCollectibleQuantity(fixture.CollectibleId)
            .ShouldBe(fixture.RequiredCollectibleQuantity + 1);
        claimedBeta
            .Profile.OwnedCollectibleQuantity(profile.GuaranteedRegularCollectibleId)
            .ShouldBe(3);
    }

    [Test]
    public async Task ReclaimingTheSameStarter_DoublesItsGrantsWithoutTouchingTheSavedDeck()
    {
        var profile = CreateProfile();
        var fixture = CreateStarterFixture(profile);
        var claimed = (StarterDeckClaimOutcome.Claimed)Success(
            profile.ClaimStarterDeck(
                Value(CommandId.Create("starter-command")),
                fixture.Definition,
                _authority.Value
            )
        );
        var revised = Success(
            claimed.Profile.ReviseDeck(
                fixture.Definition.DeckId,
                DeckRevision.Initial,
                Value(DeckName.Create("Edited starter")),
                fixture.Definition.Cards,
                _authority.Value
            )
        );

        var reclaimed = (StarterDeckClaimOutcome.Claimed)Success(
            revised.Profile.ClaimStarterDeck(
                Value(CommandId.Create("starter-command-again")),
                fixture.Definition,
                _authority.Value
            )
        );

        reclaimed.Claim.ShouldNotBeSameAs(claimed.Claim);
        reclaimed.Profile.StarterDeckClaims.Length.ShouldBe(2);
        reclaimed.Profile.StarterDeckClaims[0].ShouldBeSameAs(claimed.Claim);
        reclaimed.Profile.LatestStarterDeckClaim.ShouldBeSameAs(reclaimed.Claim);
        reclaimed.Profile.SavedDecks.Count.ShouldBe(1);
        reclaimed.Profile.SavedDecks[fixture.Definition.DeckId].ShouldBeSameAs(revised.Deck);
        reclaimed
            .Profile.OwnedCollectibleQuantity(fixture.CollectibleId)
            .ShouldBe(2 * fixture.RequiredCollectibleQuantity);
        reclaimed
            .Profile.OwnedCollectibleQuantity(profile.GuaranteedRegularCollectibleId)
            .ShouldBe(3);
    }

    [Test]
    public async Task SnapshotRestore_PreservesRepeatedStarterClaimHistoryAcrossPacksAndDeckEdits()
    {
        var persisted = CreatePersistedClaimFixture();
        var snapshot = persisted.Snapshot;

        var restored = Success(LocalProfile.Restore(snapshot, _authority.Value));
        var restoredSnapshot = restored.ToSnapshot();
        var restoredRetry = (StarterDeckClaimOutcome.AlreadyClaimed)Success(
            restored.ClaimStarterDeck(
                persisted.FirstCommandId,
                persisted.Starter.Definition,
                _authority.Value
            )
        );

        snapshot.StarterDeckClaims.Length.ShouldBe(2);
        snapshot.StarterDeckClaims[0].StarterDeckId.ShouldBe("starter-alpha");
        snapshot.StarterDeckClaims[0].CommandId.ShouldBe(persisted.FirstCommandId.Value);
        snapshot.StarterDeckClaims[1].CommandId.ShouldBe(persisted.SecondCommandId.Value);
        foreach (var claim in snapshot.StarterDeckClaims)
        {
            claim.CollectibleGrants.Length.ShouldBe(2);
            claim
                .CollectibleGrants.Single(grant =>
                    grant.CardId == persisted.Starter.CollectibleId.Value
                )
                .Quantity.ShouldBe(persisted.Starter.RequiredCollectibleQuantity);
        }
        snapshot.SavedDecks.Single().Revision.ShouldBe(2);
        restored.StarterDeckClaims.Length.ShouldBe(2);
        restored.LatestStarterDeckClaim!.CommandId.ShouldBe(persisted.SecondCommandId);
        restored.SavedDecks[persisted.Starter.Definition.DeckId].Revision.Value.ShouldBe(2);
        restoredRetry.Profile.ShouldBeSameAs(restored);
        restoredRetry.Claim.CommandId.ShouldBe(persisted.FirstCommandId);
        restoredRetry.Profile.SavedDecks.Count.ShouldBe(1);
        restoredSnapshot
            .CollectibleOwnership.SequenceEqual(snapshot.CollectibleOwnership)
            .ShouldBeTrue();
        PackReceiptsEqual(restoredSnapshot.PackReceipts, snapshot.PackReceipts).ShouldBeTrue();
        StarterDeckClaimsEqual(restoredSnapshot.StarterDeckClaims, snapshot.StarterDeckClaims)
            .ShouldBeTrue();
        restored.GuaranteedRegularCollectibleId.ShouldBe(
            persisted.Profile.GuaranteedRegularCollectibleId
        );
        restored.PackReceipts.Keys.ShouldBe(persisted.Profile.PackReceipts.Keys, ignoreOrder: true);
    }

    [Test]
    public async Task SnapshotRestore_RejectsCorruptedStarterClaimHistory()
    {
        var persisted = CreatePersistedClaimFixture();
        var snapshot = persisted.Snapshot;
        var firstClaim = snapshot.StarterDeckClaims[0];
        var grantIndex = Enumerable
            .Range(0, firstClaim.CollectibleGrants.Length)
            .Single(index =>
                firstClaim.CollectibleGrants[index].CardId == persisted.Starter.CollectibleId.Value
            );
        var grant = firstClaim.CollectibleGrants[grantIndex];
        var ownershipIndex = Enumerable
            .Range(0, snapshot.CollectibleOwnership.Length)
            .Single(index => snapshot.CollectibleOwnership[index].CardId == grant.CardId);

        var missingGrantHistory = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    CollectibleOwnership = snapshot.CollectibleOwnership.SetItem(
                        ownershipIndex,
                        snapshot.CollectibleOwnership[ownershipIndex] with
                        {
                            Quantity =
                                snapshot.CollectibleOwnership[ownershipIndex].Quantity
                                - grant.Quantity,
                        }
                    ),
                },
                _authority.Value
            )
        );
        var unknownGrant = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    StarterDeckClaims = snapshot.StarterDeckClaims.SetItem(
                        0,
                        firstClaim with
                        {
                            CollectibleGrants =
                            [
                                new StarterCollectibleGrantSnapshot("UNKNOWN-STARTER-GRANT", 1),
                            ],
                        }
                    ),
                },
                _authority.Value
            )
        );
        var unrecordedGrant = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    StarterDeckClaims = snapshot.StarterDeckClaims.SetItem(
                        0,
                        firstClaim with
                        {
                            CollectibleGrants = firstClaim.CollectibleGrants.SetItem(
                                grantIndex,
                                grant with
                                {
                                    Quantity = grant.Quantity + 1,
                                }
                            ),
                        }
                    ),
                },
                _authority.Value
            )
        );
        var nonPositiveGrant = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    StarterDeckClaims = snapshot.StarterDeckClaims.SetItem(
                        0,
                        firstClaim with
                        {
                            CollectibleGrants = firstClaim.CollectibleGrants.SetItem(
                                grantIndex,
                                grant with
                                {
                                    Quantity = 0,
                                }
                            ),
                        }
                    ),
                },
                _authority.Value
            )
        );
        var duplicateGrantCard = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    StarterDeckClaims = snapshot.StarterDeckClaims.SetItem(
                        0,
                        firstClaim with
                        {
                            CollectibleGrants = firstClaim.CollectibleGrants.Add(grant),
                        }
                    ),
                },
                _authority.Value
            )
        );
        var duplicateClaimCommand = Failure(
            LocalProfile.Restore(
                snapshot with
                {
                    StarterDeckClaims = snapshot.StarterDeckClaims.SetItem(
                        1,
                        snapshot.StarterDeckClaims[1] with
                        {
                            CommandId = firstClaim.CommandId,
                        }
                    ),
                },
                _authority.Value
            )
        );

        missingGrantHistory.ShouldBeOfType<LocalProfileRestorationFailure.OwnershipHistoryMismatch>();
        unknownGrant.ShouldBeOfType<LocalProfileRestorationFailure.UnknownCard>();
        unrecordedGrant.ShouldBeOfType<LocalProfileRestorationFailure.OwnershipHistoryMismatch>();
        nonPositiveGrant.ShouldBeOfType<LocalProfileRestorationFailure.NegativeQuantity>();
        duplicateGrantCard
            .ShouldBeOfType<LocalProfileRestorationFailure.DuplicateValue>()
            .Kind.ShouldBe(SnapshotDuplicateKind.StarterGrantCardId);
        duplicateClaimCommand
            .ShouldBeOfType<LocalProfileRestorationFailure.DuplicateValue>()
            .Kind.ShouldBe(SnapshotDuplicateKind.StarterClaimCommandId);
    }

    [Test]
    public async Task DeletingTheStarterCreatedDeck_KeepsItsClaimAndOwnedCardsAcrossRestore()
    {
        var persisted = CreatePersistedClaimFixture();
        var deckId = persisted.Starter.Definition.DeckId;
        var ownershipBefore = persisted.Profile.CollectibleOwnership.ToDictionary(
            static entry => entry.Key,
            static entry => entry.Value
        );

        var deleted = Success(persisted.Profile.DeleteDeck(deckId)).Profile;
        var snapshot = deleted.ToSnapshot();
        var restored = Success(LocalProfile.Restore(snapshot, _authority.Value));

        deleted.SavedDecks.ShouldBeEmpty();
        deleted.StarterDeckClaims.Length.ShouldBe(2);
        deleted
            .CollectibleOwnership.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value
            )
            .ShouldBe(ownershipBefore);
        snapshot.SavedDecks.ShouldBeEmpty();
        snapshot.StarterDeckClaims.Length.ShouldBe(2);
        restored.SavedDecks.ShouldBeEmpty();
        restored.StarterDeckClaims.Length.ShouldBe(2);
        restored.LatestStarterDeckClaim!.CommandId.ShouldBe(persisted.SecondCommandId);
        restored
            .CollectibleOwnership.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value
            )
            .ShouldBe(ownershipBefore);
        StarterDeckClaimsEqual(restored.ToSnapshot().StarterDeckClaims, snapshot.StarterDeckClaims)
            .ShouldBeTrue();
    }

    [Test]
    public async Task SnapshotRestore_AcceptsLegacySnapshotsWithoutStarterClaims()
    {
        var withPack = Success(
            CreateProfile()
                .OpenPack(
                    Value(CommandId.Create("pack-before-starter")),
                    Value(PackReceiptId.Create("pack-before-starter")),
                    _authority.Value,
                    new BlokemonSeededRandom(173)
                )
        ).Profile;
        var source = withPack.ToSnapshot();
        var legacySnapshot = new LocalProfileSnapshot(
            source.AuthorityManifestVersion,
            source.ProfileId,
            source.DisplayName,
            source.GuaranteedRegularCollectibleId,
            source.CollectibleOwnership,
            source.PackReceipts,
            source.SavedDecks
        );

        var restored = Success(LocalProfile.Restore(legacySnapshot, _authority.Value));
        var restoredSnapshot = restored.ToSnapshot();

        restored.StarterDeckClaims.ShouldBeEmpty();
        restored.LatestStarterDeckClaim.ShouldBeNull();
        restoredSnapshot
            .CollectibleOwnership.SequenceEqual(source.CollectibleOwnership)
            .ShouldBeTrue();
        PackReceiptsEqual(restoredSnapshot.PackReceipts, source.PackReceipts).ShouldBeTrue();
        restoredSnapshot.SavedDecks.ShouldBeEmpty();
    }

    private static LocalProfile CreateProfile() =>
        Success(
            LocalProfile.Create(
                Value(ProfileId.Create("starter-profile")),
                Success(DisplayName.Create("Starter Player")),
                _authority.Value
            )
        );

    private static StarterFixture CreateStarterFixture(LocalProfile profile)
    {
        var collectible = _authority
            .Value.Collectibles.Where(card =>
                card.Id != profile.GuaranteedRegularCollectibleId.Value
                && Math.Min(card.StackCopyLimit, DeckValidator.MechanicalCopyLimit) >= 2
            )
            .OrderBy(static card => card.Id, StringComparer.Ordinal)
            .First();
        var collectibleId = Value(CardId.Create(collectible.Id));
        const int requiredCollectibleQuantity = 2;
        var kit = _authority
            .Value.Kits.Where(card =>
                card.FreelyAvailable
                && Math.Min(card.StackCopyLimit, DeckValidator.MechanicalCopyLimit) >= 1
            )
            .OrderBy(static card => card.Id, StringComparer.Ordinal)
            .First();
        var kitId = Value(CardId.Create(kit.Id));
        var basicVim = _authority
            .Value.BasicVim.Where(static card => card.FreelyAvailable)
            .OrderBy(static card => card.Id, StringComparer.Ordinal)
            .First();
        var basicVimId = Value(CardId.Create(basicVim.Id));
        var cards = new DeckCardSelection[]
        {
            new(profile.GuaranteedRegularCollectibleId, 1),
            new(collectibleId, requiredCollectibleQuantity),
            new(kitId, 1),
            new(basicVimId, 56),
        };

        return new StarterFixture(
            new StarterDeckDefinition(
                Value(StarterDeckId.Create("starter-alpha")),
                Value(DeckId.Create("starter-deck")),
                Value(DeckName.Create("Starter deck")),
                cards
            ),
            collectibleId,
            requiredCollectibleQuantity,
            kitId,
            basicVimId
        );
    }

    private static StarterDeckDefinition SecondStarterDefinition(
        LocalProfile profile,
        StarterFixture fixture
    ) =>
        new(
            Value(StarterDeckId.Create("starter-beta")),
            Value(DeckId.Create("starter-deck-beta")),
            Value(DeckName.Create("Starter deck beta")),
            [
                new(profile.GuaranteedRegularCollectibleId, 1),
                new(fixture.CollectibleId, 1),
                new(fixture.KitId, 1),
                new(fixture.BasicVimId, 57),
            ]
        );

    private static PersistedClaimFixture CreatePersistedClaimFixture()
    {
        var withPack = Success(
            CreateProfile()
                .OpenPack(
                    Value(CommandId.Create("pack-before-starter")),
                    Value(PackReceiptId.Create("pack-before-starter")),
                    _authority.Value,
                    new BlokemonSeededRandom(173)
                )
        ).Profile;
        var fixture = CreateStarterFixture(withPack);
        var firstCommandId = Value(CommandId.Create("persisted-starter-command"));
        var claimed = (StarterDeckClaimOutcome.Claimed)Success(
            withPack.ClaimStarterDeck(firstCommandId, fixture.Definition, _authority.Value)
        );
        var revised = Success(
            claimed.Profile.ReviseDeck(
                fixture.Definition.DeckId,
                DeckRevision.Initial,
                Value(DeckName.Create("Persisted edit")),
                fixture.Definition.Cards,
                _authority.Value
            )
        );
        var secondCommandId = Value(CommandId.Create("persisted-starter-command-again"));
        var reclaimed = (StarterDeckClaimOutcome.Claimed)Success(
            revised.Profile.ClaimStarterDeck(secondCommandId, fixture.Definition, _authority.Value)
        );

        return new PersistedClaimFixture(
            reclaimed.Profile,
            reclaimed.Profile.ToSnapshot(),
            fixture,
            firstCommandId,
            secondCommandId
        );
    }

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

    private static bool PackReceiptsEqual(
        ImmutableArray<PackReceiptSnapshot> left,
        ImmutableArray<PackReceiptSnapshot> right
    ) =>
        left.Length == right.Length
        && left.Zip(right)
            .All(pair =>
                pair.First.ReceiptId == pair.Second.ReceiptId
                && pair.First.CommandId == pair.Second.CommandId
                && pair.First.Sequence == pair.Second.Sequence
                && pair.First.SampledCollectibleIds.SequenceEqual(pair.Second.SampledCollectibleIds)
            );

    private static bool StarterDeckClaimsEqual(
        ImmutableArray<StarterDeckClaimSnapshot> left,
        ImmutableArray<StarterDeckClaimSnapshot> right
    ) =>
        left.Length == right.Length
        && left.Zip(right)
            .All(pair =>
                pair.First.StarterDeckId == pair.Second.StarterDeckId
                && pair.First.CommandId == pair.Second.CommandId
                && pair.First.CollectibleGrants.SequenceEqual(pair.Second.CollectibleGrants)
            );

    private sealed record StarterFixture(
        StarterDeckDefinition Definition,
        CardId CollectibleId,
        int RequiredCollectibleQuantity,
        CardId KitId,
        CardId BasicVimId
    );

    private sealed record PersistedClaimFixture(
        LocalProfile Profile,
        LocalProfileSnapshot Snapshot,
        StarterFixture Starter,
        CommandId FirstCommandId,
        CommandId SecondCommandId
    );
}

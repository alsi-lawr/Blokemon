using System.Collections.Immutable;
using Blokemon.Core.SetDesign;
using Shouldly;

namespace Blokemon.Product.Tests;

public sealed class EconomyModeTests
{
    private static readonly Lazy<BlokemonRuntimeManifest> _authority = new(() =>
        BlokemonSetJson.RuntimeManifest(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")
            )
        )
    );

    [Test]
    public async Task UnlimitedIsTheDefaultMode_AndKeepsPacksAndStarterClaimsUncapped()
    {
        var profile = CreateProfile();

        var opened = OpenPacks(profile, 12);
        var first = ClaimStarter(opened, "starter-alpha", "claim-1", "deck-1");
        var second = ClaimStarter(first.Profile, "starter-beta", "claim-2", "deck-2");

        profile.Economy.Mode.ShouldBe(EconomyMode.Unlimited);
        profile.Economy.PackAllowance.ShouldBeNull();
        profile.Economy.StarterDeckClaimAllowance.ShouldBeNull();
        profile.RemainingPackAllowance.ShouldBeNull();
        profile.RemainingStarterDeckClaimAllowance.ShouldBeNull();
        opened.PackReceipts.Count.ShouldBe(12);
        opened.RemainingPackAllowance.ShouldBeNull();
        second.Profile.StarterDeckClaims.Length.ShouldBe(2);
        second.Profile.RemainingStarterDeckClaimAllowance.ShouldBeNull();
    }

    [Test]
    public async Task ClassicMode_ExhaustsItsPackAllowanceWithATypedFailureAfterTheLastPack()
    {
        var profile = CreateProfile(Classic(3));

        var opened = OpenPacks(profile, 3);
        var exhausted = Failure(
            opened.OpenPack(
                Value(CommandId.Create("classic-command-3")),
                Value(PackReceiptId.Create("classic-receipt-3")),
                _authority.Value,
                new BlokemonSeededRandom(77)
            )
        );
        var retryRandom = new CountingRandomSource();
        var retried = Success(
            opened.OpenPack(
                Value(CommandId.Create("classic-command-0")),
                Value(PackReceiptId.Create("ignored-receipt")),
                _authority.Value,
                retryRandom
            )
        );

        profile.Economy.PackAllowance.ShouldBe(3);
        profile.RemainingPackAllowance.ShouldBe(3);
        opened.RemainingPackAllowance.ShouldBe(0);
        opened.PackReceipts.Count.ShouldBe(3);
        exhausted.ShouldBe(PackOpenFailure.PackAllowanceExhausted);
        retried.Disposition.ShouldBe(PackOpenDisposition.AlreadyOpened);
        retried.Profile.ShouldBeSameAs(opened);
        retryRandom.ConsumptionIndex.ShouldBe(0);
    }

    [Test]
    public async Task ClassicMode_AllowsOneStarterClaimAndTypesEveryLaterClaim()
    {
        var profile = CreateProfile(Classic(1));

        var claimed = ClaimStarter(profile, "starter-alpha", "claim-1", "deck-1");
        var secondStarter = Failure(
            claimed.Profile.ClaimStarterDeck(
                Value(CommandId.Create("claim-2")),
                Definition(claimed.Profile, "starter-beta", "deck-2"),
                _authority.Value
            )
        );
        var retried = Success(
            claimed.Profile.ClaimStarterDeck(
                Value(CommandId.Create("claim-1")),
                Definition(claimed.Profile, "starter-alpha", "deck-1"),
                _authority.Value
            )
        );
        var commandConflict = Failure(
            claimed.Profile.ClaimStarterDeck(
                Value(CommandId.Create("claim-1")),
                Definition(claimed.Profile, "starter-gamma", "deck-1"),
                _authority.Value
            )
        );

        profile.RemainingStarterDeckClaimAllowance.ShouldBe(1);
        claimed.Profile.RemainingStarterDeckClaimAllowance.ShouldBe(0);
        secondStarter.ShouldBeOfType<StarterDeckClaimFailure.AllowanceExhausted>();
        var allowanceExhausted = (StarterDeckClaimFailure.AllowanceExhausted)secondStarter;
        allowanceExhausted.ClaimedStarterDeckId.Value.ShouldBe("starter-alpha");
        allowanceExhausted.RequestedStarterDeckId.Value.ShouldBe("starter-beta");
        retried.ShouldBeOfType<StarterDeckClaimOutcome.AlreadyClaimed>();
        commandConflict.ShouldBeOfType<StarterDeckClaimFailure.CommandConflict>();
        claimed.Profile.StarterDeckClaims.ShouldHaveSingleItem();
        claimed.Profile.SavedDecks.Count.ShouldBe(1);
    }

    [Test]
    public async Task SnapshotRoundTrip_CarriesEachModeAndKeepsRestoringTheSameEnforcement()
    {
        var classic = OpenPacks(CreateProfile(Classic(2)), 1);
        var unlimited = OpenPacks(CreateProfile(), 1);
        var classicSnapshot = classic.ToSnapshot();
        var unlimitedSnapshot = unlimited.ToSnapshot();

        var restoredClassic = Success(LocalProfile.Restore(classicSnapshot, _authority.Value));
        var restoredUnlimited = Success(LocalProfile.Restore(unlimitedSnapshot, _authority.Value));
        var restoredClassicExhausted = Failure(
            OpenPacks(restoredClassic, 1, "restored-classic")
                .OpenPack(
                    Value(CommandId.Create("restored-classic-overflow")),
                    Value(PackReceiptId.Create("restored-classic-overflow")),
                    _authority.Value,
                    new BlokemonSeededRandom(9)
                )
        );

        classicSnapshot.Economy.ShouldBe(EconomyMode.ClassicScarcity);
        classicSnapshot.EconomyPackAllowance.ShouldBe(2);
        unlimitedSnapshot.Economy.ShouldBe(EconomyMode.Unlimited);
        unlimitedSnapshot.EconomyPackAllowance.ShouldBe(0);
        restoredClassic.Economy.ShouldBe(classic.Economy);
        restoredClassic.RemainingPackAllowance.ShouldBe(1);
        SnapshotsMatch(restoredClassic.ToSnapshot(), classicSnapshot).ShouldBeTrue();
        restoredUnlimited.Economy.ShouldBe(EconomyRules.Unlimited);
        restoredUnlimited.RemainingPackAllowance.ShouldBeNull();
        SnapshotsMatch(restoredUnlimited.ToSnapshot(), unlimitedSnapshot).ShouldBeTrue();
        restoredClassicExhausted.ShouldBe(PackOpenFailure.PackAllowanceExhausted);
    }

    [Test]
    public async Task SnapshotWithoutEconomyFields_RestoresAsUnlimited()
    {
        var populated = OpenPacks(CreateProfile(), 2);
        var recorded = populated.ToSnapshot();
        var legacySnapshot = new LocalProfileSnapshot(
            recorded.AuthorityManifestVersion,
            recorded.ProfileId,
            recorded.DisplayName,
            recorded.GuaranteedRegularCollectibleId,
            recorded.CollectibleOwnership,
            recorded.PackReceipts,
            recorded.SavedDecks,
            recorded.StarterDeckClaims
        );

        var restored = Success(LocalProfile.Restore(legacySnapshot, _authority.Value));

        legacySnapshot.Economy.ShouldBe(EconomyMode.Unlimited);
        legacySnapshot.EconomyPackAllowance.ShouldBe(0);
        restored.Economy.ShouldBe(EconomyRules.Unlimited);
        restored.RemainingPackAllowance.ShouldBeNull();
        restored.RemainingStarterDeckClaimAllowance.ShouldBeNull();
        restored.PackReceipts.Count.ShouldBe(2);
    }

    [Test]
    public async Task ClassicRestoration_RejectsHistoryAndRulesThatBreakItsAllowances()
    {
        var classic = OpenPacks(CreateProfile(Classic(2)), 2);
        var classicSnapshot = classic.ToSnapshot();
        var twoClaims = ClaimStarter(
            ClaimStarter(CreateProfile(), "starter-alpha", "claim-1", "deck-1").Profile,
            "starter-beta",
            "claim-2",
            "deck-2"
        )
            .Profile.ToSnapshot();

        var packsBeyondAllowance = Failure(
            LocalProfile.Restore(
                classicSnapshot with
                {
                    EconomyPackAllowance = 1,
                },
                _authority.Value
            )
        );
        var claimsBeyondAllowance = Failure(
            LocalProfile.Restore(
                twoClaims with
                {
                    Economy = EconomyMode.ClassicScarcity,
                    EconomyPackAllowance = 5,
                },
                _authority.Value
            )
        );
        var unknownMode = Failure(
            LocalProfile.Restore(
                classicSnapshot with
                {
                    Economy = (EconomyMode)7,
                },
                _authority.Value
            )
        );
        var negativeAllowance = Failure(
            LocalProfile.Restore(
                classicSnapshot with
                {
                    EconomyPackAllowance = -1,
                },
                _authority.Value
            )
        );
        var unlimitedWithAllowance = Failure(
            LocalProfile.Restore(
                classicSnapshot with
                {
                    Economy = EconomyMode.Unlimited,
                },
                _authority.Value
            )
        );
        var unlimitedHistory = Success(
            LocalProfile.Restore(twoClaims with { EconomyPackAllowance = 0 }, _authority.Value)
        );

        Violation(packsBeyondAllowance)
            .ShouldBe(
                new LocalProfileRestorationFailure.EconomyRuleViolation(
                    EconomyViolationKind.PackAllowanceExceeded,
                    2,
                    1
                )
            );
        Violation(claimsBeyondAllowance)
            .ShouldBe(
                new LocalProfileRestorationFailure.EconomyRuleViolation(
                    EconomyViolationKind.StarterDeckClaimAllowanceExceeded,
                    2,
                    1
                )
            );
        Violation(unknownMode).Kind.ShouldBe(EconomyViolationKind.UnknownMode);
        Violation(negativeAllowance).Kind.ShouldBe(EconomyViolationKind.InvalidPackAllowance);
        Violation(unlimitedWithAllowance).Kind.ShouldBe(EconomyViolationKind.InvalidPackAllowance);
        unlimitedHistory.StarterDeckClaims.Length.ShouldBe(2);
        unlimitedHistory.Economy.ShouldBe(EconomyRules.Unlimited);
    }

    private static bool SnapshotsMatch(LocalProfileSnapshot left, LocalProfileSnapshot right) =>
        left.Economy == right.Economy
        && left.EconomyPackAllowance == right.EconomyPackAllowance
        && left.AuthorityManifestVersion == right.AuthorityManifestVersion
        && left.ProfileId == right.ProfileId
        && left.DisplayName == right.DisplayName
        && left.GuaranteedRegularCollectibleId == right.GuaranteedRegularCollectibleId
        && left.CollectibleOwnership.SequenceEqual(right.CollectibleOwnership)
        && left.SavedDecks.SequenceEqual(right.SavedDecks)
        && left.PackReceipts.Length == right.PackReceipts.Length
        && left.PackReceipts.All(receipt =>
            right.PackReceipts.Any(other =>
                other.ReceiptId == receipt.ReceiptId
                && other.CommandId == receipt.CommandId
                && other.Sequence == receipt.Sequence
                && other.SampledCollectibleIds.SequenceEqual(receipt.SampledCollectibleIds)
            )
        );

    private static LocalProfile CreateProfile(EconomyRules? economy = null) =>
        Success(
            LocalProfile.Create(
                Value(ProfileId.Create("profile-1")),
                Success(DisplayName.Create("Local Player")),
                _authority.Value,
                economy
            )
        );

    private static EconomyRules Classic(int packAllowance) =>
        EconomyRules
            .Classic(packAllowance)
            .Match(
                static rules => rules,
                static failure =>
                    throw new InvalidOperationException($"Expected classic rules, got {failure}.")
            );

    private static LocalProfile OpenPacks(
        LocalProfile profile,
        int count,
        string prefix = "classic"
    )
    {
        for (var index = 0; index < count; index++)
        {
            profile = Success(
                profile.OpenPack(
                    Value(CommandId.Create($"{prefix}-command-{index}")),
                    Value(PackReceiptId.Create($"{prefix}-receipt-{index}")),
                    _authority.Value,
                    new BlokemonSeededRandom((ulong)index)
                )
            ).Profile;
        }
        return profile;
    }

    private static StarterDeckClaimOutcome.Claimed ClaimStarter(
        LocalProfile profile,
        string starterDeckId,
        string commandId,
        string deckId
    ) =>
        (StarterDeckClaimOutcome.Claimed)Success(
            profile.ClaimStarterDeck(
                Value(CommandId.Create(commandId)),
                Definition(profile, starterDeckId, deckId),
                _authority.Value
            )
        );

    private static StarterDeckDefinition Definition(
        LocalProfile profile,
        string starterDeckId,
        string deckId
    ) =>
        new(
            Value(StarterDeckId.Create(starterDeckId)),
            Value(DeckId.Create(deckId)),
            Value(DeckName.Create($"{starterDeckId} deck")),
            [
                new DeckCardSelection(profile.GuaranteedRegularCollectibleId, 1),
                new DeckCardSelection(Value(CardId.Create(_authority.Value.BasicVim[0].Id)), 59),
            ]
        );

    private static LocalProfileRestorationFailure.EconomyRuleViolation Violation(
        LocalProfileRestorationFailure failure
    ) =>
        failure as LocalProfileRestorationFailure.EconomyRuleViolation
        ?? throw new InvalidOperationException(
            $"Expected an economy violation, received {failure}."
        );

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

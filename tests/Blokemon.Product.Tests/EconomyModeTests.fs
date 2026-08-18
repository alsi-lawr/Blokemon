namespace Blokemon.Product.Tests

open System
open System.Collections.Immutable
open System.IO
open Blokemon.Core.SetDesign
open Blokemon.Product
open Shouldly
open TUnit.Core

[<AutoOpen>]
module private EconomyModeFixtures =

    let authority =
        lazy
            (BlokemonSetJson.RuntimeManifest(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")
                )
            ))

    type CountingRandomSource() =
        let mutable consumed = 0

        member _.ConsumptionIndex = consumed

        interface IBlokemonRandomSource with
            member _.ConsumptionIndex = consumed

            member _.NextInt(_exclusiveMaximum) =
                consumed <- consumed + 1
                0

    let success (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Succeeded value -> value
        | DomainResult.Failed error -> failwith $"Expected success, received {error}."

    let failure (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Failed error -> error
        | DomainResult.Succeeded _ -> failwith "Expected failure."

    let value (result: DomainResult<'TValue, TextValueFailure>) = success result

    let sameItems (left: ImmutableArray<'T>) (right: ImmutableArray<'T>) =
        List.ofSeq left = List.ofSeq right

    let snapshotsMatch (left: LocalProfileSnapshot) (right: LocalProfileSnapshot) =
        left.Economy = right.Economy
        && left.EconomyPackAllowance = right.EconomyPackAllowance
        && left.AuthorityManifestVersion = right.AuthorityManifestVersion
        && left.ProfileId = right.ProfileId
        && left.DisplayName = right.DisplayName
        && left.GuaranteedRegularCollectibleId = right.GuaranteedRegularCollectibleId
        && sameItems left.CollectibleOwnership right.CollectibleOwnership
        && sameItems left.SavedDecks right.SavedDecks
        && left.PackReceipts.Length = right.PackReceipts.Length
        && left.PackReceipts
           |> Seq.forall (fun receipt ->
               right.PackReceipts
               |> Seq.exists (fun other ->
                   other.ReceiptId = receipt.ReceiptId
                   && other.CommandId = receipt.CommandId
                   && other.Sequence = receipt.Sequence
                   && sameItems other.SampledCollectibleIds receipt.SampledCollectibleIds))

    let classic packAllowance =
        EconomyRules
            .Classic(packAllowance)
            .Match(
                (fun rules -> rules),
                (fun error -> failwith $"Expected classic rules, got {error}.")
            )

    let createProfile (economy: EconomyRules | null) =
        success (
            LocalProfile.Create(
                value (ProfileId.Create "profile-1"),
                success (DisplayName.Create "Local Player"),
                authority.Value,
                economy
            )
        )

    let definition (profile: LocalProfile) starterDeckId deckId =
        StarterDeckDefinition(
            value (StarterDeckId.Create starterDeckId),
            value (DeckId.Create deckId),
            value (DeckName.Create $"{starterDeckId} deck"),
            [ { CardId = profile.GuaranteedRegularCollectibleId
                Quantity = 1 }
              { CardId = value (CardId.Create authority.Value.BasicVim[0].Id)
                Quantity = 59 } ]
        )

    let openPacks (profile: LocalProfile) count prefix =
        let mutable current = profile

        for index in 0 .. count - 1 do
            let opened =
                success (
                    current.OpenPack(
                        value (CommandId.Create $"{prefix}-command-{index}"),
                        value (PackReceiptId.Create $"{prefix}-receipt-{index}"),
                        authority.Value,
                        BlokemonSeededRandom(uint64 index)
                    )
                )

            current <- opened.Profile

        current

    let claimStarter (profile: LocalProfile) starterDeckId commandId deckId =
        match
            success (
                profile.ClaimStarterDeck(
                    value (CommandId.Create commandId),
                    definition profile starterDeckId deckId,
                    authority.Value
                )
            )
        with
        | StarterDeckClaimOutcome.Claimed(claimedProfile, claim) -> claimedProfile, claim
        | outcome -> failwith $"Expected a claim, received {outcome}."

    let violationKind (restoration: LocalProfileRestorationFailure) =
        match restoration with
        | LocalProfileRestorationFailure.EconomyRuleViolation(kind, _, _) -> kind
        | other -> failwith $"Expected an economy violation, received {other}."

type EconomyModeTests() =

    [<Test>]
    member _.UnlimitedIsTheDefaultMode_AndKeepsPacksAndStarterClaimsUncapped() =
        let profile = createProfile null

        let opened = openPacks profile 12 "classic"
        let firstProfile, _ = claimStarter opened "starter-alpha" "claim-1" "deck-1"
        let secondProfile, _ = claimStarter firstProfile "starter-beta" "claim-2" "deck-2"

        profile.Economy.Mode.ShouldBe(EconomyMode.Unlimited)
        profile.Economy.PackAllowance.HasValue.ShouldBeFalse()
        profile.Economy.StarterDeckClaimAllowance.HasValue.ShouldBeFalse()
        profile.RemainingPackAllowance.HasValue.ShouldBeFalse()
        profile.RemainingStarterDeckClaimAllowance.HasValue.ShouldBeFalse()
        opened.PackReceipts.Count.ShouldBe(12)
        opened.RemainingPackAllowance.HasValue.ShouldBeFalse()
        secondProfile.StarterDeckClaims.Length.ShouldBe(2)
        secondProfile.RemainingStarterDeckClaimAllowance.HasValue.ShouldBeFalse()

    [<Test>]
    member _.ClassicMode_ExhaustsItsPackAllowanceWithATypedFailureAfterTheLastPack() =
        let profile = createProfile (classic 3)

        let opened = openPacks profile 3 "classic"

        let exhausted =
            failure (
                opened.OpenPack(
                    value (CommandId.Create "classic-command-3"),
                    value (PackReceiptId.Create "classic-receipt-3"),
                    authority.Value,
                    BlokemonSeededRandom 77UL
                )
            )

        let retryRandom = CountingRandomSource()

        let retried =
            success (
                opened.OpenPack(
                    value (CommandId.Create "classic-command-0"),
                    value (PackReceiptId.Create "ignored-receipt"),
                    authority.Value,
                    retryRandom
                )
            )

        profile.Economy.PackAllowance.ShouldBe(Nullable 3)
        profile.RemainingPackAllowance.ShouldBe(Nullable 3)
        opened.RemainingPackAllowance.ShouldBe(Nullable 0)
        opened.PackReceipts.Count.ShouldBe(3)
        exhausted.ShouldBe(PackOpenFailure.PackAllowanceExhausted)
        retried.Disposition.ShouldBe(PackOpenDisposition.AlreadyOpened)
        retried.Profile.ShouldBeSameAs(opened)
        retryRandom.ConsumptionIndex.ShouldBe(0)

    [<Test>]
    member _.ClassicMode_AllowsOneStarterClaimAndTypesEveryLaterClaim() =
        let profile = createProfile (classic 1)

        let claimedProfile, _ = claimStarter profile "starter-alpha" "claim-1" "deck-1"

        let secondStarter =
            failure (
                claimedProfile.ClaimStarterDeck(
                    value (CommandId.Create "claim-2"),
                    definition claimedProfile "starter-beta" "deck-2",
                    authority.Value
                )
            )

        let retried =
            success (
                claimedProfile.ClaimStarterDeck(
                    value (CommandId.Create "claim-1"),
                    definition claimedProfile "starter-alpha" "deck-1",
                    authority.Value
                )
            )

        let commandConflict =
            failure (
                claimedProfile.ClaimStarterDeck(
                    value (CommandId.Create "claim-1"),
                    definition claimedProfile "starter-gamma" "deck-1",
                    authority.Value
                )
            )

        profile.RemainingStarterDeckClaimAllowance.ShouldBe(Nullable 1)
        claimedProfile.RemainingStarterDeckClaimAllowance.ShouldBe(Nullable 0)

        match secondStarter with
        | StarterDeckClaimFailure.AllowanceExhausted(claimedStarterDeckId, requestedStarterDeckId) ->
            claimedStarterDeckId.Value.ShouldBe<string>("starter-alpha")
            requestedStarterDeckId.Value.ShouldBe<string>("starter-beta")
        | other -> failwith $"Expected an exhausted allowance, received {other}."

        retried.IsAlreadyClaimed.ShouldBeTrue()
        commandConflict.IsCommandConflict.ShouldBeTrue()
        claimedProfile.StarterDeckClaims.Length.ShouldBe(1)
        claimedProfile.SavedDecks.Count.ShouldBe(1)

    [<Test>]
    member _.SnapshotRoundTrip_CarriesEachModeAndKeepsRestoringTheSameEnforcement() =
        let classicProfile = openPacks (createProfile (classic 2)) 1 "classic"
        let unlimitedProfile = openPacks (createProfile null) 1 "classic"
        let classicSnapshot = classicProfile.ToSnapshot()
        let unlimitedSnapshot = unlimitedProfile.ToSnapshot()

        let restoredClassic =
            success (LocalProfile.Restore(classicSnapshot, authority.Value))

        let restoredUnlimited =
            success (LocalProfile.Restore(unlimitedSnapshot, authority.Value))

        let restoredClassicExhausted =
            failure (
                (openPacks restoredClassic 1 "restored-classic")
                    .OpenPack(
                        value (CommandId.Create "restored-classic-overflow"),
                        value (PackReceiptId.Create "restored-classic-overflow"),
                        authority.Value,
                        BlokemonSeededRandom 9UL
                    )
            )

        classicSnapshot.Economy.ShouldBe(EconomyMode.ClassicScarcity)
        classicSnapshot.EconomyPackAllowance.ShouldBe(2)
        unlimitedSnapshot.Economy.ShouldBe(EconomyMode.Unlimited)
        unlimitedSnapshot.EconomyPackAllowance.ShouldBe(0)
        restoredClassic.Economy.ShouldBe(classicProfile.Economy)
        restoredClassic.RemainingPackAllowance.ShouldBe(Nullable 1)
        (snapshotsMatch (restoredClassic.ToSnapshot()) classicSnapshot).ShouldBeTrue()
        restoredUnlimited.Economy.ShouldBe(EconomyRules.Unlimited)
        restoredUnlimited.RemainingPackAllowance.HasValue.ShouldBeFalse()
        (snapshotsMatch (restoredUnlimited.ToSnapshot()) unlimitedSnapshot).ShouldBeTrue()
        restoredClassicExhausted.ShouldBe(PackOpenFailure.PackAllowanceExhausted)

    [<Test>]
    member _.SnapshotWithoutEconomyFields_RestoresAsUnlimited() =
        let populated = openPacks (createProfile null) 2 "classic"
        let recorded = populated.ToSnapshot()

        // The economy fields are absent from a pre-economy document, which restoration
        // reads as the defaults the persisted shape carries.
        let legacySnapshot =
            { AuthorityManifestVersion = recorded.AuthorityManifestVersion
              ProfileId = recorded.ProfileId
              DisplayName = recorded.DisplayName
              GuaranteedRegularCollectibleId = recorded.GuaranteedRegularCollectibleId
              CollectibleOwnership = recorded.CollectibleOwnership
              PackReceipts = recorded.PackReceipts
              SavedDecks = recorded.SavedDecks
              StarterDeckClaims = recorded.StarterDeckClaims
              Economy = EconomyMode.Unlimited
              EconomyPackAllowance = 0 }

        let restored = success (LocalProfile.Restore(legacySnapshot, authority.Value))

        legacySnapshot.Economy.ShouldBe(EconomyMode.Unlimited)
        legacySnapshot.EconomyPackAllowance.ShouldBe(0)
        restored.Economy.ShouldBe(EconomyRules.Unlimited)
        restored.RemainingPackAllowance.HasValue.ShouldBeFalse()
        restored.RemainingStarterDeckClaimAllowance.HasValue.ShouldBeFalse()
        restored.PackReceipts.Count.ShouldBe(2)

    [<Test>]
    member _.ClassicRestoration_RejectsHistoryAndRulesThatBreakItsAllowances() =
        let classicProfile = openPacks (createProfile (classic 2)) 2 "classic"
        let classicSnapshot = classicProfile.ToSnapshot()

        let firstClaimProfile, _ =
            claimStarter (createProfile null) "starter-alpha" "claim-1" "deck-1"

        let secondClaimProfile, _ =
            claimStarter firstClaimProfile "starter-beta" "claim-2" "deck-2"

        let twoClaims = secondClaimProfile.ToSnapshot()

        let packsBeyondAllowance =
            failure (
                LocalProfile.Restore(
                    { classicSnapshot with
                        EconomyPackAllowance = 1 },
                    authority.Value
                )
            )

        let claimsBeyondAllowance =
            failure (
                LocalProfile.Restore(
                    { twoClaims with
                        Economy = EconomyMode.ClassicScarcity
                        EconomyPackAllowance = 5 },
                    authority.Value
                )
            )

        let unknownMode =
            failure (
                LocalProfile.Restore(
                    { classicSnapshot with
                        Economy = enum<EconomyMode> 7 },
                    authority.Value
                )
            )

        let negativeAllowance =
            failure (
                LocalProfile.Restore(
                    { classicSnapshot with
                        EconomyPackAllowance = -1 },
                    authority.Value
                )
            )

        let unlimitedWithAllowance =
            failure (
                LocalProfile.Restore(
                    { classicSnapshot with
                        Economy = EconomyMode.Unlimited },
                    authority.Value
                )
            )

        let unlimitedHistory =
            success (
                LocalProfile.Restore(
                    { twoClaims with
                        EconomyPackAllowance = 0 },
                    authority.Value
                )
            )

        packsBeyondAllowance.ShouldBe(
            LocalProfileRestorationFailure.EconomyRuleViolation(
                EconomyViolationKind.PackAllowanceExceeded,
                2,
                1
            )
        )

        claimsBeyondAllowance.ShouldBe(
            LocalProfileRestorationFailure.EconomyRuleViolation(
                EconomyViolationKind.StarterDeckClaimAllowanceExceeded,
                2,
                1
            )
        )

        (violationKind unknownMode).ShouldBe(EconomyViolationKind.UnknownMode)
        (violationKind negativeAllowance).ShouldBe(EconomyViolationKind.InvalidPackAllowance)
        (violationKind unlimitedWithAllowance).ShouldBe(EconomyViolationKind.InvalidPackAllowance)
        unlimitedHistory.StarterDeckClaims.Length.ShouldBe(2)
        unlimitedHistory.Economy.ShouldBe(EconomyRules.Unlimited)

namespace Blokemon.Product.Tests

open System
open System.Collections.Immutable
open System.IO
open Blokemon.Core.SetDesign
open Blokemon.Product
open FsUnit
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
    member _.``unlimited economy mode should be the default and keep packs and starter claims uncapped``
        ()
        =
        let profile = createProfile null

        let opened = openPacks profile 12 "classic"
        let firstProfile, _ = claimStarter opened "starter-alpha" "claim-1" "deck-1"
        let secondProfile, _ = claimStarter firstProfile "starter-beta" "claim-2" "deck-2"

        profile.Economy.Mode |> should equal EconomyMode.Unlimited
        profile.Economy.PackAllowance.HasValue |> should be False
        profile.Economy.StarterDeckClaimAllowance.HasValue |> should be False
        profile.RemainingPackAllowance.HasValue |> should be False
        profile.RemainingStarterDeckClaimAllowance.HasValue |> should be False
        opened.PackReceipts.Count |> should equal 12
        opened.RemainingPackAllowance.HasValue |> should be False
        secondProfile.StarterDeckClaims.Length |> should equal 2
        secondProfile.RemainingStarterDeckClaimAllowance.HasValue |> should be False

    [<Test>]
    member _.``classic economy mode should exhaust its pack allowance with a typed failure after the last pack``
        ()
        =
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

        profile.Economy.PackAllowance |> should equal (Nullable 3)
        profile.RemainingPackAllowance |> should equal (Nullable 3)
        opened.RemainingPackAllowance |> should equal (Nullable 0)
        opened.PackReceipts.Count |> should equal 3
        exhausted |> should equal PackOpenFailure.PackAllowanceExhausted
        retried.Disposition |> should equal PackOpenDisposition.AlreadyOpened
        obj.ReferenceEquals(retried.Profile, opened) |> should be True
        retryRandom.ConsumptionIndex |> should equal 0

    [<Test>]
    member _.``classic economy mode should allow one starter claim and type every later claim``() =
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

        profile.RemainingStarterDeckClaimAllowance |> should equal (Nullable 1)
        claimedProfile.RemainingStarterDeckClaimAllowance |> should equal (Nullable 0)

        match secondStarter with
        | StarterDeckClaimFailure.AllowanceExhausted(claimedStarterDeckId, requestedStarterDeckId) ->
            claimedStarterDeckId.Value |> should equal "starter-alpha"
            requestedStarterDeckId.Value |> should equal "starter-beta"
        | other -> failwith $"Expected an exhausted allowance, received {other}."

        retried.IsAlreadyClaimed |> should be True
        commandConflict.IsCommandConflict |> should be True
        claimedProfile.StarterDeckClaims.Length |> should equal 1
        claimedProfile.SavedDecks.Count |> should equal 1

    [<Test>]
    member _.``snapshot round trip should carry each economy mode and keep restoring the same enforcement``
        ()
        =
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

        classicSnapshot.Economy |> should equal EconomyMode.ClassicScarcity
        classicSnapshot.EconomyPackAllowance |> should equal 2
        unlimitedSnapshot.Economy |> should equal EconomyMode.Unlimited
        unlimitedSnapshot.EconomyPackAllowance |> should equal 0
        restoredClassic.Economy |> should equal classicProfile.Economy
        restoredClassic.RemainingPackAllowance |> should equal (Nullable 1)
        snapshotsMatch (restoredClassic.ToSnapshot()) classicSnapshot |> should be True
        restoredUnlimited.Economy |> should equal EconomyRules.Unlimited
        restoredUnlimited.RemainingPackAllowance.HasValue |> should be False

        snapshotsMatch (restoredUnlimited.ToSnapshot()) unlimitedSnapshot
        |> should be True

        restoredClassicExhausted |> should equal PackOpenFailure.PackAllowanceExhausted

    [<Test>]
    member _.``snapshot without economy fields should restore as unlimited``() =
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

        legacySnapshot.Economy |> should equal EconomyMode.Unlimited
        legacySnapshot.EconomyPackAllowance |> should equal 0
        restored.Economy |> should equal EconomyRules.Unlimited
        restored.RemainingPackAllowance.HasValue |> should be False
        restored.RemainingStarterDeckClaimAllowance.HasValue |> should be False
        restored.PackReceipts.Count |> should equal 2

    [<Test>]
    member _.``classic restoration should reject history and rules that break its allowances``() =
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

        packsBeyondAllowance
        |> should
            equal
            (LocalProfileRestorationFailure.EconomyRuleViolation(
                EconomyViolationKind.PackAllowanceExceeded,
                2,
                1
            ))

        claimsBeyondAllowance
        |> should
            equal
            (LocalProfileRestorationFailure.EconomyRuleViolation(
                EconomyViolationKind.StarterDeckClaimAllowanceExceeded,
                2,
                1
            ))

        violationKind unknownMode |> should equal EconomyViolationKind.UnknownMode

        violationKind negativeAllowance
        |> should equal EconomyViolationKind.InvalidPackAllowance

        violationKind unlimitedWithAllowance
        |> should equal EconomyViolationKind.InvalidPackAllowance

        unlimitedHistory.StarterDeckClaims.Length |> should equal 2
        unlimitedHistory.Economy |> should equal EconomyRules.Unlimited

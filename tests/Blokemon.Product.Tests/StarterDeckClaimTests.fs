namespace Blokemon.Product.Tests

open System
open System.Collections.Immutable
open System.IO
open Blokemon.Core.SetDesign
open Blokemon.Product
open Shouldly
open TUnit.Core

[<AutoOpen>]
module private StarterDeckClaimFixtures =

    let authority =
        lazy
            (BlokemonSetJson.RuntimeManifest(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")
                )
            ))

    let success (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Succeeded value -> value
        | DomainResult.Failed error -> failwith $"Expected success, received {error}."

    let failure (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Failed error -> error
        | DomainResult.Succeeded _ -> failwith "Expected failure."

    let value (result: DomainResult<'TValue, TextValueFailure>) = success result

    let claimedParts (outcome: StarterDeckClaimOutcome) =
        match outcome with
        | StarterDeckClaimOutcome.Claimed(profile, claim) -> profile, claim
        | other -> failwith $"Expected a new claim, received {other}."

    let alreadyClaimedParts (outcome: StarterDeckClaimOutcome) =
        match outcome with
        | StarterDeckClaimOutcome.AlreadyClaimed(profile, claim) -> profile, claim
        | other -> failwith $"Expected an existing claim, received {other}."

    let latestClaim (profile: LocalProfile) =
        match profile.LatestStarterDeckClaim with
        | null -> failwith "Expected a starter claim."
        | claim -> claim

    let packReceiptsEqual
        (left: ImmutableArray<PackReceiptSnapshot>)
        (right: ImmutableArray<PackReceiptSnapshot>)
        =
        left.Length = right.Length
        && Seq.forall2
            (fun (first: PackReceiptSnapshot) (second: PackReceiptSnapshot) ->
                first.ReceiptId = second.ReceiptId
                && first.CommandId = second.CommandId
                && first.Sequence = second.Sequence
                && List.ofSeq first.SampledCollectibleIds = List.ofSeq second.SampledCollectibleIds)
            left
            right

    let starterDeckClaimsEqual
        (left: ImmutableArray<StarterDeckClaimSnapshot>)
        (right: ImmutableArray<StarterDeckClaimSnapshot>)
        =
        left.Length = right.Length
        && Seq.forall2
            (fun (first: StarterDeckClaimSnapshot) (second: StarterDeckClaimSnapshot) ->
                first.StarterDeckId = second.StarterDeckId
                && first.CommandId = second.CommandId
                && List.ofSeq first.CollectibleGrants = List.ofSeq second.CollectibleGrants)
            left
            right

    let ownershipRows (profile: LocalProfile) =
        profile.CollectibleOwnership
        |> Seq.map (fun entry -> entry.Key.Value, entry.Value)
        |> Seq.sortBy fst
        |> List.ofSeq

    [<NoComparison>]
    type StarterFixture =
        { Definition: StarterDeckDefinition
          CollectibleId: CardId
          RequiredCollectibleQuantity: int
          KitId: CardId
          BasicVimId: CardId }

    [<NoComparison>]
    type PersistedClaimFixture =
        { Profile: LocalProfile
          Snapshot: LocalProfileSnapshot
          Starter: StarterFixture
          FirstCommandId: CommandId
          SecondCommandId: CommandId }

    let createProfile () =
        success (
            LocalProfile.Create(
                value (ProfileId.Create "starter-profile"),
                success (DisplayName.Create "Starter Player"),
                authority.Value
            )
        )

    let createStarterFixture (profile: LocalProfile) =
        let collectible =
            authority.Value.Collectibles
            |> Array.filter (fun card ->
                card.Id <> profile.GuaranteedRegularCollectibleId.Value
                && min card.StackCopyLimit DeckValidator.MechanicalCopyLimit >= 2)
            |> Array.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
            |> Array.head

        let collectibleId = value (CardId.Create collectible.Id)
        let requiredCollectibleQuantity = 2

        let kit =
            authority.Value.Kits
            |> Array.filter (fun card ->
                card.FreelyAvailable
                && min card.StackCopyLimit DeckValidator.MechanicalCopyLimit >= 1)
            |> Array.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
            |> Array.head

        let kitId = value (CardId.Create kit.Id)

        let basicVim =
            authority.Value.BasicVim
            |> Array.filter (fun card -> card.FreelyAvailable)
            |> Array.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
            |> Array.head

        let basicVimId = value (CardId.Create basicVim.Id)

        let cards =
            [ { CardId = profile.GuaranteedRegularCollectibleId
                Quantity = 1 }
              { CardId = collectibleId
                Quantity = requiredCollectibleQuantity }
              { CardId = kitId; Quantity = 1 }
              { CardId = basicVimId; Quantity = 56 } ]

        { Definition =
            StarterDeckDefinition(
                value (StarterDeckId.Create "starter-alpha"),
                value (DeckId.Create "starter-deck"),
                value (DeckName.Create "Starter deck"),
                cards
            )
          CollectibleId = collectibleId
          RequiredCollectibleQuantity = requiredCollectibleQuantity
          KitId = kitId
          BasicVimId = basicVimId }

    let secondStarterDefinition (profile: LocalProfile) (fixture: StarterFixture) =
        StarterDeckDefinition(
            value (StarterDeckId.Create "starter-beta"),
            value (DeckId.Create "starter-deck-beta"),
            value (DeckName.Create "Starter deck beta"),
            [ { CardId = profile.GuaranteedRegularCollectibleId
                Quantity = 1 }
              { CardId = fixture.CollectibleId
                Quantity = 1 }
              { CardId = fixture.KitId; Quantity = 1 }
              { CardId = fixture.BasicVimId
                Quantity = 57 } ]
        )

    let createPersistedClaimFixture () =
        let opened =
            success (
                (createProfile ())
                    .OpenPack(
                        value (CommandId.Create "pack-before-starter"),
                        value (PackReceiptId.Create "pack-before-starter"),
                        authority.Value,
                        BlokemonSeededRandom 173UL
                    )
            )

        let withPack = opened.Profile
        let fixture = createStarterFixture withPack
        let firstCommandId = value (CommandId.Create "persisted-starter-command")

        let claimedProfile, _ =
            claimedParts (
                success (
                    withPack.ClaimStarterDeck(firstCommandId, fixture.Definition, authority.Value)
                )
            )

        let revised =
            success (
                claimedProfile.ReviseDeck(
                    fixture.Definition.DeckId,
                    DeckRevision.Initial,
                    value (DeckName.Create "Persisted edit"),
                    fixture.Definition.Cards,
                    authority.Value
                )
            )

        let secondCommandId = value (CommandId.Create "persisted-starter-command-again")

        let reclaimedProfile, _ =
            claimedParts (
                success (
                    revised.Profile.ClaimStarterDeck(
                        secondCommandId,
                        fixture.Definition,
                        authority.Value
                    )
                )
            )

        { Profile = reclaimedProfile
          Snapshot = reclaimedProfile.ToSnapshot()
          Starter = fixture
          FirstCommandId = firstCommandId
          SecondCommandId = secondCommandId }

type StarterDeckClaimTests() =

    [<Test>]
    member _.ClaimStarterDeck_AtomicallyGrantsFullCollectibleContentsAndCreatesEditableDeck() =
        // The claim records that a starter was claimed and what it granted. The deck it
        // creates is an ordinary saved deck, so revising it leaves the claim untouched.
        let profile = createProfile ()
        let fixture = createStarterFixture profile
        let commandId = value (CommandId.Create "starter-command")

        let invalidDefinition =
            StarterDeckDefinition(
                fixture.Definition.Id,
                fixture.Definition.DeckId,
                fixture.Definition.DeckName,
                fixture.Definition.Cards
                |> Seq.map (fun selection ->
                    if selection.CardId = fixture.BasicVimId then
                        { selection with
                            Quantity = selection.Quantity - 1 }
                    else
                        selection)
            )

        let invalid =
            failure (profile.ClaimStarterDeck(commandId, invalidDefinition, authority.Value))

        invalid.IsInvalidDeck.ShouldBeTrue()
        profile.StarterDeckClaims.IsEmpty.ShouldBeTrue()
        (isNull profile.LatestStarterDeckClaim).ShouldBeTrue()
        profile.SavedDecks.Count.ShouldBe(0)
        (profile.OwnedCollectibleQuantity fixture.CollectibleId).ShouldBe(0)

        let claimedProfile, claim =
            claimedParts (
                success (profile.ClaimStarterDeck(commandId, fixture.Definition, authority.Value))
            )

        claimedProfile.ShouldNotBeSameAs(profile)
        claimedProfile.StarterDeckClaims.Length.ShouldBe(1)
        (latestClaim claimedProfile).ShouldBeSameAs(claim)
        claimedProfile.SavedDecks.Count.ShouldBe(1)
        let starterDeck = claimedProfile.SavedDecks[fixture.Definition.DeckId]
        starterDeck.Revision.ShouldBe(DeckRevision.Initial)
        (starterDeck.Cards.Values |> Seq.sum).ShouldBe(60)
        claim.Id.ShouldBe(fixture.Definition.Id)
        claim.CommandId.ShouldBe(commandId)
        claim.CollectibleGrants.Length.ShouldBe(2)

        (claim.CollectibleGrants
         |> Seq.find (fun grant -> grant.CardId = fixture.CollectibleId))
            .Quantity.ShouldBe(fixture.RequiredCollectibleQuantity)

        (claim.CollectibleGrants
         |> Seq.find (fun grant -> grant.CardId = profile.GuaranteedRegularCollectibleId))
            .Quantity.ShouldBe(1)

        (claimedProfile.OwnedCollectibleQuantity fixture.CollectibleId)
            .ShouldBe(fixture.RequiredCollectibleQuantity)

        (claimedProfile.OwnedCollectibleQuantity claimedProfile.GuaranteedRegularCollectibleId)
            .ShouldBe(2)

        (claimedProfile.OwnedCollectibleQuantity fixture.KitId).ShouldBe(0)
        (claimedProfile.OwnedCollectibleQuantity fixture.BasicVimId).ShouldBe(0)

        let revised =
            success (
                claimedProfile.ReviseDeck(
                    fixture.Definition.DeckId,
                    starterDeck.Revision,
                    value (DeckName.Create "Edited starter"),
                    fixture.Definition.Cards,
                    authority.Value
                )
            )

        revised.Deck.Revision.Value.ShouldBe(2L)
        revised.Deck.Name.Value.ShouldBe<string>("Edited starter")
        (latestClaim revised.Profile).ShouldBeSameAs(claim)
        revised.Profile.StarterDeckClaims.Length.ShouldBe(1)

    [<Test>]
    member _.ClaimStarterDeck_ExactRetryIsIdempotentWhileConflictsAreTypedFailures() =
        let profile = createProfile ()
        let fixture = createStarterFixture profile
        let commandId = value (CommandId.Create "starter-command")

        let claimedProfile, claim =
            claimedParts (
                success (profile.ClaimStarterDeck(commandId, fixture.Definition, authority.Value))
            )

        // Replaying the command identifies the claim by its starter alone: the same starter
        // is the same claim however its deck payload is spelled, a different starter is a
        // conflicting reuse of the command id.
        let equivalentDefinition =
            StarterDeckDefinition(
                fixture.Definition.Id,
                fixture.Definition.DeckId,
                value (DeckName.Create "Renamed starter payload"),
                Seq.rev fixture.Definition.Cards
            )

        let retriedProfile, retriedClaim =
            alreadyClaimedParts (
                success (
                    claimedProfile.ClaimStarterDeck(
                        commandId,
                        equivalentDefinition,
                        authority.Value
                    )
                )
            )

        let commandConflict =
            failure (
                claimedProfile.ClaimStarterDeck(
                    commandId,
                    secondStarterDefinition profile fixture,
                    authority.Value
                )
            )

        retriedProfile.ShouldBeSameAs(claimedProfile)
        retriedClaim.ShouldBeSameAs(claim)
        retriedProfile.SavedDecks.Count.ShouldBe(1)

        (retriedProfile.OwnedCollectibleQuantity fixture.CollectibleId)
            .ShouldBe(fixture.RequiredCollectibleQuantity)

        match commandConflict with
        | StarterDeckClaimFailure.CommandConflict(_, claimedStarterDeckId, requestedStarterDeckId) ->
            claimedStarterDeckId.ShouldBe(fixture.Definition.Id)
            requestedStarterDeckId.Value.ShouldBe<string>("starter-beta")
        | other -> failwith $"Expected a command conflict, received {other}."

        claimedProfile.StarterDeckClaims.Length.ShouldBe(1)
        claimedProfile.SavedDecks.Count.ShouldBe(1)
        (latestClaim claimedProfile).ShouldBeSameAs(claim)

    [<Test>]
    member _.ClaimingADifferentStarter_AddsASecondClaimAndItsDeck() =
        let profile = createProfile ()
        let fixture = createStarterFixture profile
        let second = secondStarterDefinition profile fixture

        let alphaProfile, alphaClaim =
            claimedParts (
                success (
                    profile.ClaimStarterDeck(
                        value (CommandId.Create "starter-command-alpha"),
                        fixture.Definition,
                        authority.Value
                    )
                )
            )

        let betaProfile, betaClaim =
            claimedParts (
                success (
                    alphaProfile.ClaimStarterDeck(
                        value (CommandId.Create "starter-command-beta"),
                        second,
                        authority.Value
                    )
                )
            )

        betaProfile.StarterDeckClaims.Length.ShouldBe(2)
        betaProfile.StarterDeckClaims[0].ShouldBeSameAs(alphaClaim)
        (latestClaim betaProfile).ShouldBeSameAs(betaClaim)
        betaProfile.SavedDecks.Count.ShouldBe(2)
        (betaProfile.SavedDecks[second.DeckId].Cards.Values |> Seq.sum).ShouldBe(60)

        (betaProfile.OwnedCollectibleQuantity fixture.CollectibleId)
            .ShouldBe(fixture.RequiredCollectibleQuantity + 1)

        (betaProfile.OwnedCollectibleQuantity profile.GuaranteedRegularCollectibleId).ShouldBe(3)

    [<Test>]
    member _.ReclaimingTheSameStarter_DoublesItsGrantsWithoutTouchingTheSavedDeck() =
        let profile = createProfile ()
        let fixture = createStarterFixture profile

        let claimedProfile, claim =
            claimedParts (
                success (
                    profile.ClaimStarterDeck(
                        value (CommandId.Create "starter-command"),
                        fixture.Definition,
                        authority.Value
                    )
                )
            )

        let revised =
            success (
                claimedProfile.ReviseDeck(
                    fixture.Definition.DeckId,
                    DeckRevision.Initial,
                    value (DeckName.Create "Edited starter"),
                    fixture.Definition.Cards,
                    authority.Value
                )
            )

        let reclaimedProfile, reclaimedClaim =
            claimedParts (
                success (
                    revised.Profile.ClaimStarterDeck(
                        value (CommandId.Create "starter-command-again"),
                        fixture.Definition,
                        authority.Value
                    )
                )
            )

        reclaimedClaim.ShouldNotBeSameAs(claim)
        reclaimedProfile.StarterDeckClaims.Length.ShouldBe(2)
        reclaimedProfile.StarterDeckClaims[0].ShouldBeSameAs(claim)
        (latestClaim reclaimedProfile).ShouldBeSameAs(reclaimedClaim)
        reclaimedProfile.SavedDecks.Count.ShouldBe(1)
        reclaimedProfile.SavedDecks[fixture.Definition.DeckId].ShouldBeSameAs(revised.Deck)

        (reclaimedProfile.OwnedCollectibleQuantity fixture.CollectibleId)
            .ShouldBe(2 * fixture.RequiredCollectibleQuantity)

        (reclaimedProfile.OwnedCollectibleQuantity profile.GuaranteedRegularCollectibleId)
            .ShouldBe(3)

    [<Test>]
    member _.SnapshotRestore_PreservesRepeatedStarterClaimHistoryAcrossPacksAndDeckEdits() =
        let persisted = createPersistedClaimFixture ()
        let snapshot = persisted.Snapshot

        let restored = success (LocalProfile.Restore(snapshot, authority.Value))
        let restoredSnapshot = restored.ToSnapshot()

        let restoredRetryProfile, restoredRetryClaim =
            alreadyClaimedParts (
                success (
                    restored.ClaimStarterDeck(
                        persisted.FirstCommandId,
                        persisted.Starter.Definition,
                        authority.Value
                    )
                )
            )

        snapshot.StarterDeckClaims.Length.ShouldBe(2)
        snapshot.StarterDeckClaims[0].StarterDeckId.ShouldBe<string>("starter-alpha")
        snapshot.StarterDeckClaims[0].CommandId.ShouldBe<string>(persisted.FirstCommandId.Value)
        snapshot.StarterDeckClaims[1].CommandId.ShouldBe<string>(persisted.SecondCommandId.Value)

        for claim in snapshot.StarterDeckClaims do
            claim.CollectibleGrants.Length.ShouldBe(2)

            (claim.CollectibleGrants
             |> Seq.find (fun grant -> grant.CardId = persisted.Starter.CollectibleId.Value))
                .Quantity.ShouldBe(persisted.Starter.RequiredCollectibleQuantity)

        (snapshot.SavedDecks |> Seq.exactlyOne).Revision.ShouldBe(2L)
        restored.StarterDeckClaims.Length.ShouldBe(2)
        (latestClaim restored).CommandId.ShouldBe(persisted.SecondCommandId)
        restored.SavedDecks[persisted.Starter.Definition.DeckId].Revision.Value.ShouldBe(2L)
        restoredRetryProfile.ShouldBeSameAs(restored)
        restoredRetryClaim.CommandId.ShouldBe(persisted.FirstCommandId)
        restoredRetryProfile.SavedDecks.Count.ShouldBe(1)

        (List.ofSeq restoredSnapshot.CollectibleOwnership = List.ofSeq snapshot.CollectibleOwnership)
            .ShouldBeTrue()

        (packReceiptsEqual restoredSnapshot.PackReceipts snapshot.PackReceipts).ShouldBeTrue()

        (starterDeckClaimsEqual restoredSnapshot.StarterDeckClaims snapshot.StarterDeckClaims)
            .ShouldBeTrue()

        restored.GuaranteedRegularCollectibleId.ShouldBe(
            persisted.Profile.GuaranteedRegularCollectibleId
        )

        (restored.PackReceipts.Keys
         |> Seq.map (fun key -> key.Value)
         |> Seq.sort
         |> List.ofSeq)
            .ShouldBe(
                persisted.Profile.PackReceipts.Keys
                |> Seq.map (fun key -> key.Value)
                |> Seq.sort
                |> List.ofSeq
            )

    [<Test>]
    member _.SnapshotRestore_RejectsCorruptedStarterClaimHistory() =
        let persisted = createPersistedClaimFixture ()
        let snapshot = persisted.Snapshot
        let firstClaim = snapshot.StarterDeckClaims[0]

        let grantIndex =
            Seq.init firstClaim.CollectibleGrants.Length id
            |> Seq.filter (fun index ->
                firstClaim.CollectibleGrants[index].CardId = persisted.Starter.CollectibleId.Value)
            |> Seq.exactlyOne

        let grant = firstClaim.CollectibleGrants[grantIndex]

        let ownershipIndex =
            Seq.init snapshot.CollectibleOwnership.Length id
            |> Seq.filter (fun index -> snapshot.CollectibleOwnership[index].CardId = grant.CardId)
            |> Seq.exactlyOne

        let missingGrantHistory =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        CollectibleOwnership =
                            snapshot.CollectibleOwnership.SetItem(
                                ownershipIndex,
                                { snapshot.CollectibleOwnership[ownershipIndex] with
                                    Quantity =
                                        snapshot.CollectibleOwnership[ownershipIndex].Quantity
                                        - grant.Quantity }
                            ) },
                    authority.Value
                )
            )

        let unknownGrant =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        StarterDeckClaims =
                            snapshot.StarterDeckClaims.SetItem(
                                0,
                                { firstClaim with
                                    CollectibleGrants =
                                        ImmutableArray.CreateRange
                                            [ ({ CardId = "UNKNOWN-STARTER-GRANT"
                                                 Quantity = 1 }
                                              : StarterCollectibleGrantSnapshot) ] }
                            ) },
                    authority.Value
                )
            )

        let unrecordedGrant =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        StarterDeckClaims =
                            snapshot.StarterDeckClaims.SetItem(
                                0,
                                { firstClaim with
                                    CollectibleGrants =
                                        firstClaim.CollectibleGrants.SetItem(
                                            grantIndex,
                                            { grant with
                                                Quantity = grant.Quantity + 1 }
                                        ) }
                            ) },
                    authority.Value
                )
            )

        let nonPositiveGrant =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        StarterDeckClaims =
                            snapshot.StarterDeckClaims.SetItem(
                                0,
                                { firstClaim with
                                    CollectibleGrants =
                                        firstClaim.CollectibleGrants.SetItem(
                                            grantIndex,
                                            { grant with Quantity = 0 }
                                        ) }
                            ) },
                    authority.Value
                )
            )

        let duplicateGrantCard =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        StarterDeckClaims =
                            snapshot.StarterDeckClaims.SetItem(
                                0,
                                { firstClaim with
                                    CollectibleGrants = firstClaim.CollectibleGrants.Add grant }
                            ) },
                    authority.Value
                )
            )

        let duplicateClaimCommand =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        StarterDeckClaims =
                            snapshot.StarterDeckClaims.SetItem(
                                1,
                                { snapshot.StarterDeckClaims[1] with
                                    CommandId = firstClaim.CommandId }
                            ) },
                    authority.Value
                )
            )

        missingGrantHistory.IsOwnershipHistoryMismatch.ShouldBeTrue()
        unknownGrant.IsUnknownCard.ShouldBeTrue()
        unrecordedGrant.IsOwnershipHistoryMismatch.ShouldBeTrue()
        nonPositiveGrant.IsNegativeQuantity.ShouldBeTrue()

        match duplicateGrantCard with
        | LocalProfileRestorationFailure.DuplicateValue(kind, _) ->
            kind.ShouldBe(SnapshotDuplicateKind.StarterGrantCardId)
        | other -> failwith $"Expected a duplicate value, received {other}."

        match duplicateClaimCommand with
        | LocalProfileRestorationFailure.DuplicateValue(kind, _) ->
            kind.ShouldBe(SnapshotDuplicateKind.StarterClaimCommandId)
        | other -> failwith $"Expected a duplicate value, received {other}."

    [<Test>]
    member _.DeletingTheStarterCreatedDeck_KeepsItsClaimAndOwnedCardsAcrossRestore() =
        let persisted = createPersistedClaimFixture ()
        let deckId = persisted.Starter.Definition.DeckId
        let ownershipBefore = ownershipRows persisted.Profile

        let deleted = (success (persisted.Profile.DeleteDeck deckId)).Profile
        let snapshot = deleted.ToSnapshot()
        let restored = success (LocalProfile.Restore(snapshot, authority.Value))

        deleted.SavedDecks.Count.ShouldBe(0)
        deleted.StarterDeckClaims.Length.ShouldBe(2)
        (ownershipRows deleted).ShouldBe(ownershipBefore)
        snapshot.SavedDecks.IsEmpty.ShouldBeTrue()
        snapshot.StarterDeckClaims.Length.ShouldBe(2)
        restored.SavedDecks.Count.ShouldBe(0)
        restored.StarterDeckClaims.Length.ShouldBe(2)
        (latestClaim restored).CommandId.ShouldBe(persisted.SecondCommandId)
        (ownershipRows restored).ShouldBe(ownershipBefore)

        (starterDeckClaimsEqual (restored.ToSnapshot()).StarterDeckClaims snapshot.StarterDeckClaims)
            .ShouldBeTrue()

    [<Test>]
    member _.SnapshotRestore_AcceptsLegacySnapshotsWithoutStarterClaims() =
        let opened =
            success (
                (createProfile ())
                    .OpenPack(
                        value (CommandId.Create "pack-before-starter"),
                        value (PackReceiptId.Create "pack-before-starter"),
                        authority.Value,
                        BlokemonSeededRandom 173UL
                    )
            )

        let withPack = opened.Profile
        let source = withPack.ToSnapshot()

        // A pre-claim document carries no starter claims at all, which restoration reads
        // as the empty history the persisted shape defaults to.
        let legacySnapshot =
            { source with
                StarterDeckClaims = ImmutableArray<StarterDeckClaimSnapshot>.Empty }

        let restored = success (LocalProfile.Restore(legacySnapshot, authority.Value))
        let restoredSnapshot = restored.ToSnapshot()

        restored.StarterDeckClaims.IsEmpty.ShouldBeTrue()
        (isNull restored.LatestStarterDeckClaim).ShouldBeTrue()

        (List.ofSeq restoredSnapshot.CollectibleOwnership = List.ofSeq source.CollectibleOwnership)
            .ShouldBeTrue()

        (packReceiptsEqual restoredSnapshot.PackReceipts source.PackReceipts).ShouldBeTrue()
        restoredSnapshot.SavedDecks.IsEmpty.ShouldBeTrue()

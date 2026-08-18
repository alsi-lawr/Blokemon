namespace Blokemon.Product.Tests

open System
open System.Collections.Immutable
open System.IO
open Blokemon.Core.SetDesign
open Blokemon.Product
open Shouldly
open TUnit.Core

[<AutoOpen>]
module private LocalProfileFixtures =

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

    let invalidIssues (result: DeckValidationResult) =
        match result with
        | DeckValidationResult.Invalid issues -> issues
        | DeckValidationResult.Valid _ -> failwith "Expected invalid deck."

    let duplicateKind (restoration: LocalProfileRestorationFailure) =
        match restoration with
        | LocalProfileRestorationFailure.DuplicateValue(kind, _) -> kind
        | other -> failwith $"Expected a duplicate value, received {other}."

    let sameItems (left: ImmutableArray<'T>) (right: ImmutableArray<'T>) =
        List.ofSeq left = List.ofSeq right

    let orderedIds (ids: seq<string>) = ids |> Seq.sort |> List.ofSeq

    let createProfile () =
        success (
            LocalProfile.Create(
                value (ProfileId.Create "profile-1"),
                success (DisplayName.Create "Local Player"),
                authority.Value
            )
        )

    let legalCards (profile: LocalProfile) =
        [ { CardId = profile.GuaranteedRegularCollectibleId
            Quantity = 1 }
          { CardId = value (CardId.Create authority.Value.BasicVim[0].Id)
            Quantity = 59 } ]

    let createPopulatedProfile () =
        let mutable profile = createProfile ()

        for index in 0..1 do
            let opened =
                success (
                    profile.OpenPack(
                        value (CommandId.Create $"snapshot-command-{index}"),
                        value (PackReceiptId.Create $"snapshot-receipt-{index}"),
                        authority.Value,
                        BlokemonSeededRandom(uint64 (100 + index))
                    )
                )

            profile <- opened.Profile

        let deckId = value (DeckId.Create "snapshot-deck")

        let created =
            success (
                profile.CreateDeck(
                    deckId,
                    value (DeckName.Create "Initial deck"),
                    legalCards profile,
                    authority.Value
                )
            )

        let revised =
            success (
                created.Profile.ReviseDeck(
                    deckId,
                    created.Deck.Revision,
                    value (DeckName.Create "Revised deck"),
                    legalCards profile,
                    authority.Value
                )
            )

        revised.Profile

type LocalProfileTests() =

    [<Test>]
    member _.DisplayNameCreation_TrimsAndAcceptsThirtyTwoCharacters() =
        let boundary = String('a', DisplayName.MaximumLength)

        let result = success (DisplayName.Create $"  {boundary}\t")

        result.Value.ShouldBe<string>(boundary)
        result.Value.Length.ShouldBe(DisplayName.MaximumLength)

    [<Test>]
    member _.DisplayNameCreation_RejectsMissingAndOverlongValues() =
        let missing = failure (DisplayName.Create " \t ")

        let overlong =
            failure (DisplayName.Create(String('a', DisplayName.MaximumLength + 1)))

        missing.ShouldBe(DisplayNameCreationFailure.Required)
        overlong.ShouldBe(DisplayNameCreationFailure.TooLong)

    [<Test>]
    member _.DuplicatePackCommand_ReturnsPersistedReceiptWithoutSamplingOrDoubleApplying() =
        let profile = createProfile ()
        let firstRandom = BlokemonSeededRandom 41UL

        let first =
            success (
                profile.OpenPack(
                    value (CommandId.Create "command-1"),
                    value (PackReceiptId.Create "receipt-1"),
                    authority.Value,
                    firstRandom
                )
            )

        let retryRandom = CountingRandomSource()

        let retried =
            success (
                first.Profile.OpenPack(
                    value (CommandId.Create "command-1"),
                    value (PackReceiptId.Create "ignored-receipt"),
                    authority.Value,
                    retryRandom
                )
            )

        first.Disposition.ShouldBe(PackOpenDisposition.Opened)
        retried.Disposition.ShouldBe(PackOpenDisposition.AlreadyOpened)
        retried.Profile.ShouldBeSameAs(first.Profile)
        retried.Receipt.ShouldBeSameAs(first.Receipt)
        retried.Receipt.Id.Value.ShouldBe<string>("receipt-1")
        retried.Receipt.SampledCollectibleIds.Length.ShouldBe(11)

        (sameItems retried.Receipt.SampledCollectibleIds first.Receipt.SampledCollectibleIds)
            .ShouldBeTrue()

        for cardId, drawn in first.Receipt.SampledCollectibleIds |> Seq.countBy id do
            let initialQuantity =
                if cardId = profile.GuaranteedRegularCollectibleId then
                    1
                else
                    0

            (first.Profile.OwnedCollectibleQuantity cardId).ShouldBe(initialQuantity + drawn)

        retried.Profile.PackReceipts.Count.ShouldBe(1)
        retryRandom.ConsumptionIndex.ShouldBe(0)

    [<Test>]
    member _.OpeningPacksBeyondTheFormerTenPackLimit_KeepsGrantingSamplesInSequence() =
        let mutable profile = createProfile ()

        for index in 0..11 do
            let opened =
                success (
                    profile.OpenPack(
                        value (CommandId.Create $"command-{index}"),
                        value (PackReceiptId.Create $"receipt-{index}"),
                        authority.Value,
                        BlokemonSeededRandom(uint64 index)
                    )
                )

            profile <- opened.Profile

        profile.PackReceipts.Count.ShouldBe(12)

        (profile.PackReceipts.Values
         |> Seq.map (fun receipt -> receipt.Sequence)
         |> Seq.sort
         |> List.ofSeq)
            .ShouldBe([ 1..12 ])

        (profile.CollectibleOwnership.Values |> Seq.sum).ShouldBe(1 + 12 * 11)

    [<Test>]
    member _.DeckValidation_UsesOwnedCollectiblesAndFreeCatalogueCards() =
        let profile = createProfile ()
        let regular = profile.GuaranteedRegularCollectibleId
        let kit = value (CardId.Create authority.Value.Kits[0].Id)
        let vim = value (CardId.Create authority.Value.BasicVim[0].Id)

        let legal =
            DeckValidator.Validate
                profile
                authority.Value
                [ { CardId = regular; Quantity = 1 }
                  { CardId = kit; Quantity = 4 }
                  { CardId = vim; Quantity = 55 } ]

        let overOwned =
            invalidIssues (
                DeckValidator.Validate
                    profile
                    authority.Value
                    [ { CardId = regular; Quantity = 2 }; { CardId = vim; Quantity = 58 } ]
            )

        let overMechanicalLimit =
            invalidIssues (
                DeckValidator.Validate
                    profile
                    authority.Value
                    [ { CardId = regular; Quantity = 1 }
                      { CardId = kit; Quantity = 5 }
                      { CardId = vim; Quantity = 54 } ]
            )

        legal.IsValid.ShouldBeTrue()

        (overOwned |> Seq.exists (fun issue -> issue.IsCollectibleQuantityNotOwned)).ShouldBeTrue()

        (overMechanicalLimit
         |> Seq.exists (fun issue -> issue.IsMechanicalCopyLimitExceeded))
            .ShouldBeTrue()

    [<Test>]
    member _.DeckValidation_RequiresExactlySixtyCardsAndARegularCollectible() =
        let profile = createProfile ()
        let vim = value (CardId.Create authority.Value.BasicVim[0].Id)

        let wrongCount =
            invalidIssues (
                DeckValidator.Validate
                    profile
                    authority.Value
                    [ { CardId = profile.GuaranteedRegularCollectibleId
                        Quantity = 1 }
                      { CardId = vim; Quantity = 58 } ]
            )

        let noRegular =
            invalidIssues (
                DeckValidator.Validate profile authority.Value [ { CardId = vim; Quantity = 60 } ]
            )

        (wrongCount |> Seq.exists (fun issue -> issue.IsWrongCardCount)).ShouldBeTrue()

        (noRegular |> Seq.exists (fun issue -> issue.IsRegularCollectibleRequired)).ShouldBeTrue()

    [<Test>]
    member _.StaleDeckRevision_IsRejectedWithoutOverwritingSavedDeck() =
        let profile = createProfile ()
        let deckId = value (DeckId.Create "deck-1")
        let cards = legalCards profile

        let created =
            success (
                profile.CreateDeck(deckId, value (DeckName.Create "First"), cards, authority.Value)
            )

        let revised =
            success (
                created.Profile.ReviseDeck(
                    deckId,
                    created.Deck.Revision,
                    value (DeckName.Create "Second"),
                    cards,
                    authority.Value
                )
            )

        let stale =
            revised.Profile.ReviseDeck(
                deckId,
                created.Deck.Revision,
                value (DeckName.Create "Stale overwrite"),
                cards,
                authority.Value
            )

        match failure stale with
        | DeckSaveFailure.StaleRevision(staleDeckId, expectedRevision, actualRevision) ->
            staleDeckId.ShouldBe(deckId)
            expectedRevision.ShouldBe(created.Deck.Revision)
            actualRevision.ShouldBe(revised.Deck.Revision)
        | other -> failwith $"Expected a stale revision, received {other}."

        revised.Profile.SavedDecks[deckId].Name.Value.ShouldBe<string>("Second")
        revised.Profile.SavedDecks[deckId].Revision.Value.ShouldBe(2L)

    [<Test>]
    member _.DeletingADeck_RemovesOnlyThatDeckAndTypesAnUnknownOne() =
        let profile = createPopulatedProfile ()
        let deckId = value (DeckId.Create "snapshot-deck")
        let secondId = value (DeckId.Create "second-deck")

        let createdSecond =
            success (
                profile.CreateDeck(
                    secondId,
                    value (DeckName.Create "Second deck"),
                    legalCards profile,
                    authority.Value
                )
            )

        let withSecond = createdSecond.Profile

        let deleted = success (withSecond.DeleteDeck deckId)
        let missing = failure (deleted.Profile.DeleteDeck deckId)

        deleted.Deck.Name.Value.ShouldBe<string>("Revised deck")
        (deleted.Profile.SavedDecks.Keys |> List.ofSeq).ShouldBe([ secondId ])

        (deleted.Profile.CollectibleOwnership |> List.ofSeq)
            .ShouldBe(withSecond.CollectibleOwnership |> List.ofSeq)

        (deleted.Profile.PackReceipts.Keys
         |> Seq.map (fun receiptId -> receiptId.Value)
         |> orderedIds)
            .ShouldBe(
                withSecond.PackReceipts.Keys
                |> Seq.map (fun receiptId -> receiptId.Value)
                |> orderedIds
            )

        missing.ShouldBe(DeckDeleteFailure.NotFound)
        withSecond.SavedDecks.Count.ShouldBe(2)

    [<Test>]
    member _.SnapshotRestore_RehydratesReceiptsOwnershipAndRevisedDecks() =
        let profile = createPopulatedProfile ()
        let snapshot = profile.ToSnapshot()

        let restored = success (LocalProfile.Restore(snapshot, authority.Value))
        let restoredSnapshot = restored.ToSnapshot()
        let firstReceipt = snapshot.PackReceipts[0]
        let retryRandom = CountingRandomSource()

        let retried =
            success (
                restored.OpenPack(
                    value (CommandId.Create firstReceipt.CommandId),
                    value (PackReceiptId.Create "ignored-after-restore"),
                    authority.Value,
                    retryRandom
                )
            )

        restored.BoundAuthorityManifestVersion.ShouldBe<string>(snapshot.AuthorityManifestVersion)

        (sameItems restoredSnapshot.CollectibleOwnership snapshot.CollectibleOwnership)
            .ShouldBeTrue()

        restoredSnapshot.PackReceipts.Length.ShouldBe(2)

        for index in 0 .. snapshot.PackReceipts.Length - 1 do
            restoredSnapshot.PackReceipts[index]
                .ReceiptId.ShouldBe<string>(snapshot.PackReceipts[index].ReceiptId)

            restoredSnapshot.PackReceipts[index]
                .CommandId.ShouldBe<string>(snapshot.PackReceipts[index].CommandId)

            restoredSnapshot.PackReceipts[index].Sequence.ShouldBe(index + 1)

            (sameItems
                restoredSnapshot.PackReceipts[index].SampledCollectibleIds
                snapshot.PackReceipts[index].SampledCollectibleIds)
                .ShouldBeTrue()

        restoredSnapshot.SavedDecks.Length.ShouldBe(1)
        restoredSnapshot.SavedDecks[0].Revision.ShouldBe(2L)
        restoredSnapshot.SavedDecks[0].Name.ShouldBe<string>("Revised deck")

        (sameItems restoredSnapshot.SavedDecks[0].Cards snapshot.SavedDecks[0].Cards).ShouldBeTrue()

        retried.Disposition.ShouldBe(PackOpenDisposition.AlreadyOpened)

        (restored.PackReceipts.Values
         |> Seq.map (fun receipt -> receipt.Sequence)
         |> Seq.max)
            .ShouldBe(2)

        retryRandom.ConsumptionIndex.ShouldBe(0)

    [<Test>]
    member _.HistoricalProfile_PreservesPackBindingWhileDecksUseCurrentMechanics() =
        let snapshot = (createPopulatedProfile ()).ToSnapshot()
        let firstReceipt = snapshot.PackReceipts[0]
        let replacedCardId = firstReceipt.SampledCollectibleIds[0]
        let historicalCardId = "HISTORICAL-COLLECTIBLE"

        let historicalReceipt =
            { firstReceipt with
                SampledCollectibleIds =
                    firstReceipt.SampledCollectibleIds.SetItem(0, historicalCardId) }

        let ownership = snapshot.CollectibleOwnership.ToBuilder()

        let replacedOwnershipIndex =
            Seq.init ownership.Count id
            |> Seq.filter (fun index -> ownership[index].CardId = replacedCardId)
            |> Seq.exactlyOne

        ownership[replacedOwnershipIndex] <-
            { ownership[replacedOwnershipIndex] with
                Quantity = ownership[replacedOwnershipIndex].Quantity - 1 }

        ownership.Add(
            { CardId = historicalCardId
              Quantity = 1 }
            : CollectibleOwnershipSnapshot
        )

        let historicalDeck =
            { snapshot.SavedDecks[0] with
                Cards =
                    ImmutableArray.CreateRange
                        [ ({ CardId = snapshot.GuaranteedRegularCollectibleId
                             Quantity = 1 }
                          : SavedDeckCardSnapshot)
                          { CardId = "HISTORICAL-DECK-CARD"
                            Quantity = 59 } ] }

        let historicalSnapshot =
            { snapshot with
                AuthorityManifestVersion = "historical-manifest"
                CollectibleOwnership = ownership.ToImmutable()
                PackReceipts = snapshot.PackReceipts.SetItem(0, historicalReceipt)
                SavedDecks = snapshot.SavedDecks.SetItem(0, historicalDeck) }

        let restored = success (LocalProfile.Restore(historicalSnapshot, authority.Value))
        let restoredSnapshot = restored.ToSnapshot()
        let historicalDeckId = value (DeckId.Create historicalDeck.DeckId)
        let currentCards = legalCards restored

        let revised =
            success (
                restored.ReviseDeck(
                    historicalDeckId,
                    restored.SavedDecks[historicalDeckId].Revision,
                    value (DeckName.Create "Current deck"),
                    currentCards,
                    authority.Value
                )
            )

        let created =
            success (
                revised.Profile.CreateDeck(
                    value (DeckId.Create "current-deck"),
                    value (DeckName.Create "Another current deck"),
                    currentCards,
                    authority.Value
                )
            )

        let random = CountingRandomSource()

        let packFailure =
            failure (
                created.Profile.OpenPack(
                    value (CommandId.Create "blocked-command"),
                    value (PackReceiptId.Create "blocked-receipt"),
                    authority.Value,
                    random
                )
            )

        restored.BoundAuthorityManifestVersion.ShouldBe<string>("historical-manifest")

        (restoredSnapshot.CollectibleOwnership
         |> Seq.exists (fun item -> item.CardId = historicalCardId && item.Quantity = 1))
            .ShouldBeTrue()

        (restoredSnapshot.PackReceipts[0].SampledCollectibleIds
         |> Seq.exists (fun cardId -> cardId = historicalCardId))
            .ShouldBeTrue()

        (restoredSnapshot.SavedDecks[0].Cards
         |> Seq.exists (fun card -> card.CardId = "HISTORICAL-DECK-CARD" && card.Quantity = 59))
            .ShouldBeTrue()

        (revised.Deck.Cards.Keys |> Seq.map (fun cardId -> cardId.Value) |> orderedIds)
            .ShouldBe(
                currentCards |> Seq.map (fun selection -> selection.CardId.Value) |> orderedIds
            )

        revised.Deck.Revision.Value.ShouldBe(historicalDeck.Revision + 1L)

        (created.Deck.Cards.Keys |> Seq.map (fun cardId -> cardId.Value) |> orderedIds)
            .ShouldBe(revised.Deck.Cards.Keys |> Seq.map (fun cardId -> cardId.Value) |> orderedIds)

        created.Profile.BoundAuthorityManifestVersion.ShouldBe<string>("historical-manifest")
        packFailure.ShouldBe(PackOpenFailure.AuthorityVersionMismatch)
        random.ConsumptionIndex.ShouldBe(0)

    [<Test>]
    member _.SnapshotRestore_RejectsInvalidIdentityQuantitiesAndDuplicates() =
        let snapshot = (createPopulatedProfile ()).ToSnapshot()

        let invalidIdentity =
            failure (LocalProfile.Restore({ snapshot with ProfileId = " " }, authority.Value))

        let negativeQuantity =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        CollectibleOwnership =
                            snapshot.CollectibleOwnership.SetItem(
                                0,
                                { snapshot.CollectibleOwnership[0] with
                                    Quantity = -1 }
                            ) },
                    authority.Value
                )
            )

        let duplicateReceipt =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        PackReceipts =
                            snapshot.PackReceipts.SetItem(
                                1,
                                { snapshot.PackReceipts[1] with
                                    ReceiptId = snapshot.PackReceipts[0].ReceiptId }
                            ) },
                    authority.Value
                )
            )

        let duplicateCommand =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        PackReceipts =
                            snapshot.PackReceipts.SetItem(
                                1,
                                { snapshot.PackReceipts[1] with
                                    CommandId = snapshot.PackReceipts[0].CommandId }
                            ) },
                    authority.Value
                )
            )

        let duplicateSequence =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        PackReceipts =
                            snapshot.PackReceipts.SetItem(
                                1,
                                { snapshot.PackReceipts[1] with
                                    Sequence = snapshot.PackReceipts[0].Sequence }
                            ) },
                    authority.Value
                )
            )

        let nonPositiveSequence =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        PackReceipts =
                            snapshot.PackReceipts.SetItem(
                                0,
                                { snapshot.PackReceipts[0] with
                                    Sequence = 0 }
                            ) },
                    authority.Value
                )
            )

        let gappedSequence =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        PackReceipts =
                            snapshot.PackReceipts.SetItem(
                                1,
                                { snapshot.PackReceipts[1] with
                                    Sequence = 3 }
                            ) },
                    authority.Value
                )
            )

        invalidIdentity.IsInvalidId.ShouldBeTrue()
        negativeQuantity.IsNegativeQuantity.ShouldBeTrue()
        duplicateReceipt.IsDuplicateValue.ShouldBeTrue()
        (duplicateKind duplicateReceipt).ShouldBe(SnapshotDuplicateKind.PackReceiptId)
        (duplicateKind duplicateCommand).ShouldBe(SnapshotDuplicateKind.PackCommandId)
        duplicateSequence.IsInvalidPackSequence.ShouldBeTrue()
        nonPositiveSequence.IsInvalidPackSequence.ShouldBeTrue()
        gappedSequence.IsInvalidPackSequence.ShouldBeTrue()

    [<Test>]
    member _.SnapshotRestore_RejectsCurrentAuthorityAndDeckClaimsThatAreInvalid() =
        let snapshot = (createPopulatedProfile ()).ToSnapshot()

        let unknownCurrentCard =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        CollectibleOwnership =
                            snapshot.CollectibleOwnership.Add(
                                { CardId = "UNKNOWN-CURRENT-CARD"
                                  Quantity = 0 }
                                : CollectibleOwnershipSnapshot
                            ) },
                    authority.Value
                )
            )

        let invalidRevision =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        SavedDecks =
                            snapshot.SavedDecks.SetItem(
                                0,
                                { snapshot.SavedDecks[0] with
                                    Revision = 0L }
                            ) },
                    authority.Value
                )
            )

        let vimId = authority.Value.BasicVim[0].Id

        let illegalDeck =
            failure (
                LocalProfile.Restore(
                    { snapshot with
                        SavedDecks =
                            snapshot.SavedDecks.SetItem(
                                0,
                                { snapshot.SavedDecks[0] with
                                    Cards =
                                        ImmutableArray.CreateRange
                                            [ ({ CardId = vimId; Quantity = 60 }
                                              : SavedDeckCardSnapshot) ] }
                            ) },
                    authority.Value
                )
            )

        unknownCurrentCard.IsUnknownCard.ShouldBeTrue()
        invalidRevision.IsInvalidDeckRevision.ShouldBeTrue()
        illegalDeck.IsInvalidSavedDeck.ShouldBeTrue()

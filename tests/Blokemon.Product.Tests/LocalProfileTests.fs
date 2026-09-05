namespace Blokemon.Product.Tests

open System
open System.Collections.Immutable
open System.IO
open Blokemon.Core.SetDesign
open Blokemon.Product
open FsUnit
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
          { CardId = value (CardId.Create (authority.Value.BasicVim |> Array.find _.IsBasic).Id)
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
    member _.``display name creation should trim and accept thirty two characters``() =
        let boundary = String('a', DisplayName.MaximumLength)

        let result = success (DisplayName.Create $"  {boundary}\t")

        result.Value |> should equal boundary
        result.Value.Length |> should equal DisplayName.MaximumLength

    [<Test>]
    member _.``display name creation should reject missing and overlong values``() =
        let missing = failure (DisplayName.Create " \t ")

        let overlong =
            failure (DisplayName.Create(String('a', DisplayName.MaximumLength + 1)))

        missing |> should equal DisplayNameCreationFailure.Required
        overlong |> should equal DisplayNameCreationFailure.TooLong

    [<Test>]
    member _.``duplicate pack command should return persisted receipt without sampling or double applying``
        ()
        =
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

        first.Disposition |> should equal PackOpenDisposition.Opened
        retried.Disposition |> should equal PackOpenDisposition.AlreadyOpened
        obj.ReferenceEquals(retried.Profile, first.Profile) |> should be True
        obj.ReferenceEquals(retried.Receipt, first.Receipt) |> should be True
        retried.Receipt.Id.Value |> should equal "receipt-1"
        retried.Receipt.SampledCollectibleIds.Length |> should equal 11

        sameItems retried.Receipt.SampledCollectibleIds first.Receipt.SampledCollectibleIds
        |> should be True

        for cardId, drawn in first.Receipt.SampledCollectibleIds |> Seq.countBy id do
            let initialQuantity =
                if cardId = profile.GuaranteedRegularCollectibleId then
                    1
                else
                    0

            first.Profile.OwnedCollectibleQuantity cardId
            |> should equal (initialQuantity + drawn)

        retried.Profile.PackReceipts.Count |> should equal 1
        retryRandom.ConsumptionIndex |> should equal 0

    [<Test>]
    member _.``opening a pack should pull its Trainers into ownership alongside its Blokemon``() =
        let profile = createProfile ()
        let kitIds = authority.Value.Kits |> Seq.map (fun kit -> kit.Id) |> Set.ofSeq

        let opened =
            success (
                profile.OpenPack(
                    value (CommandId.Create "trainer-command"),
                    value (PackReceiptId.Create "trainer-receipt"),
                    authority.Value,
                    BlokemonSeededRandom 7UL
                )
            )

        let pulledTrainers =
            opened.Receipt.SampledCollectibleIds
            |> Seq.filter (fun cardId -> kitIds.Contains cardId.Value)
            |> List.ofSeq

        pulledTrainers.Length |> should be (greaterThanOrEqualTo 2)

        for cardId in pulledTrainers do
            opened.Profile.OwnedCollectibleQuantity cardId |> should equal 1

        let restored =
            success (LocalProfile.Restore(opened.Profile.ToSnapshot(), authority.Value))

        for cardId in pulledTrainers do
            restored.OwnedCollectibleQuantity cardId |> should equal 1

    [<Test>]
    member _.``a pack whose Trainer pool cannot fill a slot should be unavailable and change nothing``
        ()
        =
        let profile = createProfile ()
        let random = CountingRandomSource()

        let withoutRareTrainers =
            { authority.Value with
                Kits =
                    authority.Value.Kits
                    |> Array.filter (fun kit -> kit.ProductBucket <> BlokemonProductBucket.Rare) }

        let refused =
            failure (
                profile.OpenPack(
                    value (CommandId.Create "unavailable-command"),
                    value (PackReceiptId.Create "unavailable-receipt"),
                    withoutRareTrainers,
                    random
                )
            )

        refused |> should equal PackOpenFailure.ElevenCardPackUnavailable
        random.ConsumptionIndex |> should equal 0
        profile.PackReceipts.Count |> should equal 0
        profile.CollectibleOwnership.Count |> should equal 1

    [<Test>]
    member _.``opening packs beyond the former ten pack limit should keep granting samples in sequence``
        ()
        =
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

        profile.PackReceipts.Count |> should equal 12

        profile.PackReceipts.Values
        |> Seq.map (fun receipt -> receipt.Sequence)
        |> Seq.sort
        |> List.ofSeq
        |> should equal [ 1..12 ]

        profile.CollectibleOwnership.Values |> Seq.sum |> should equal (1 + 12 * 11)

    [<Test>]
    member _.``deck validation should use owned collectibles and owned Trainers with free basic vim``
        ()
        =
        let profile = createProfile ()
        let regular = profile.GuaranteedRegularCollectibleId

        let vim =
            value (CardId.Create (authority.Value.BasicVim |> Array.find _.IsBasic).Id)

        // One pack always deals at least two Trainers; the first of them is owned once.
        let opened =
            success (
                profile.OpenPack(
                    value (CommandId.Create "deck-command"),
                    value (PackReceiptId.Create "deck-receipt"),
                    authority.Value,
                    BlokemonSeededRandom 7UL
                )
            )

        let kitIds = authority.Value.Kits |> Seq.map (fun kit -> kit.Id) |> Set.ofSeq

        let kit =
            opened.Receipt.SampledCollectibleIds
            |> Seq.find (fun cardId -> kitIds.Contains cardId.Value)

        let unownedKit =
            authority.Value.Kits
            |> Array.map (fun card -> value (CardId.Create card.Id))
            |> Array.find (fun cardId -> opened.Profile.OwnedCollectibleQuantity cardId = 0)

        let validate = DeckValidator.Validate opened.Profile authority.Value

        let legal =
            validate
                [ { CardId = regular; Quantity = 1 }
                  { CardId = kit; Quantity = 1 }
                  { CardId = vim; Quantity = 58 } ]

        let overOwnedTrainer =
            invalidIssues (
                validate
                    [ { CardId = regular; Quantity = 1 }
                      { CardId = kit; Quantity = 2 }
                      { CardId = vim; Quantity = 57 } ]
            )

        let unownedTrainer =
            invalidIssues (
                validate
                    [ { CardId = regular; Quantity = 1 }
                      { CardId = unownedKit; Quantity = 1 }
                      { CardId = vim; Quantity = 58 } ]
            )

        let overOwnedCollectible =
            invalidIssues (
                validate [ { CardId = regular; Quantity = 2 }; { CardId = vim; Quantity = 58 } ]
            )

        let overMechanicalLimit =
            invalidIssues (
                validate
                    [ { CardId = regular; Quantity = 1 }
                      { CardId = kit; Quantity = 5 }
                      { CardId = vim; Quantity = 54 } ]
            )

        // Advanced Rulebook Version 1, p. 20: Double Colorless is not Basic Energy, so it
        // remains subject to the four-card name limit.
        let doubleColorless =
            authority.Value.BasicVim
            |> Array.find (fun energy -> not energy.IsBasic)
            |> _.Id
            |> CardId.Create
            |> value

        let tooManyDoubleColorless =
            invalidIssues (
                validate
                    [ { CardId = regular; Quantity = 1 }
                      { CardId = doubleColorless
                        Quantity = 5 }
                      { CardId = vim; Quantity = 54 } ]
            )

        legal.IsValid |> should be True

        overOwnedTrainer
        |> Seq.exists (fun issue ->
            match issue with
            | DeckValidationIssue.CollectibleQuantityNotOwned(cardId, requested, owned) ->
                cardId = kit && requested = 2L && owned = 1
            | _ -> false)
        |> should be True

        unownedTrainer
        |> Seq.exists (fun issue ->
            match issue with
            | DeckValidationIssue.CollectibleQuantityNotOwned(cardId, requested, owned) ->
                cardId = unownedKit && requested = 1L && owned = 0
            | _ -> false)
        |> should be True

        overOwnedCollectible
        |> Seq.exists (fun issue -> issue.IsCollectibleQuantityNotOwned)
        |> should be True

        overMechanicalLimit
        |> Seq.exists (fun issue -> issue.IsMechanicalCopyLimitExceeded)
        |> should be True

        tooManyDoubleColorless
        |> Seq.exists (fun issue -> issue.IsMechanicalCopyLimitExceeded)
        |> should be True

    [<Test>]
    member _.``a saved deck whose Trainers are no longer owned should restore intact and validate as short``
        ()
        =
        // A profile from before Trainers were pulled: bound to an older manifest, a deck that
        // lists a Trainer, and no Trainer anywhere in its ownership history.
        let profile = createProfile ()
        let regular = profile.GuaranteedRegularCollectibleId
        let kit = value (CardId.Create authority.Value.Kits[0].Id)

        let vim =
            value (CardId.Create (authority.Value.BasicVim |> Array.find _.IsBasic).Id)

        let staleSnapshot =
            { profile.ToSnapshot() with
                AuthorityManifestVersion = "historical-manifest"
                SavedDecks =
                    ImmutableArray.Create
                        { DeckId = "stale-deck"
                          Name = "Stale deck"
                          Revision = 1L
                          Cards =
                            ImmutableArray.CreateRange
                                [ ({ CardId = regular.Value; Quantity = 1 }: SavedDeckCardSnapshot)
                                  { CardId = kit.Value; Quantity = 2 }
                                  { CardId = vim.Value; Quantity = 57 } ] } }

        let restored = success (LocalProfile.Restore(staleSnapshot, authority.Value))
        let deck = restored.SavedDecks[value (DeckId.Create "stale-deck")]

        let issues =
            invalidIssues (
                DeckValidator.Validate
                    restored
                    authority.Value
                    (deck.Cards
                     |> Seq.map (fun entry ->
                         { CardId = entry.Key
                           Quantity = entry.Value }))
            )

        deck.Cards[kit] |> should equal 2
        deck.Cards.Count |> should equal 3

        issues
        |> Seq.exists (fun issue ->
            match issue with
            | DeckValidationIssue.CollectibleQuantityNotOwned(cardId, requested, owned) ->
                cardId = kit && requested = 2L && owned = 0
            | _ -> false)
        |> should be True

        restored.BoundAuthorityManifestVersion |> should equal "historical-manifest"

    [<Test>]
    member _.``deck validation should require exactly sixty cards and a regular collectible``() =
        let profile = createProfile ()

        let vim =
            value (CardId.Create (authority.Value.BasicVim |> Array.find _.IsBasic).Id)

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

        wrongCount |> Seq.exists (fun issue -> issue.IsWrongCardCount) |> should be True

        noRegular
        |> Seq.exists (fun issue -> issue.IsRegularCollectibleRequired)
        |> should be True

    [<Test>]
    member _.``stale deck revision should be rejected without overwriting saved deck``() =
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
            staleDeckId |> should equal deckId
            expectedRevision |> should equal created.Deck.Revision
            actualRevision |> should equal revised.Deck.Revision
        | other -> failwith $"Expected a stale revision, received {other}."

        revised.Profile.SavedDecks[deckId].Name.Value |> should equal "Second"
        revised.Profile.SavedDecks[deckId].Revision.Value |> should equal 2L

    [<Test>]
    member _.``deleting a deck should remove only that deck and type an unknown one``() =
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

        deleted.Deck.Name.Value |> should equal "Revised deck"
        deleted.Profile.SavedDecks.Keys |> List.ofSeq |> should equal [ secondId ]

        deleted.Profile.CollectibleOwnership
        |> List.ofSeq
        |> should equal (withSecond.CollectibleOwnership |> List.ofSeq)

        deleted.Profile.PackReceipts.Keys
        |> Seq.map (fun receiptId -> receiptId.Value)
        |> orderedIds
        |> should
            equal
            (withSecond.PackReceipts.Keys
             |> Seq.map (fun receiptId -> receiptId.Value)
             |> orderedIds)

        missing |> should equal DeckDeleteFailure.NotFound
        withSecond.SavedDecks.Count |> should equal 2

    [<Test>]
    member _.``snapshot restore should rehydrate receipts ownership and revised decks``() =
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

        restored.BoundAuthorityManifestVersion
        |> should equal snapshot.AuthorityManifestVersion

        sameItems restoredSnapshot.CollectibleOwnership snapshot.CollectibleOwnership
        |> should be True

        restoredSnapshot.PackReceipts.Length |> should equal 2

        for index in 0 .. snapshot.PackReceipts.Length - 1 do
            restoredSnapshot.PackReceipts[index].ReceiptId
            |> should equal snapshot.PackReceipts[index].ReceiptId

            restoredSnapshot.PackReceipts[index].CommandId
            |> should equal snapshot.PackReceipts[index].CommandId

            restoredSnapshot.PackReceipts[index].Sequence |> should equal (index + 1)

            sameItems
                restoredSnapshot.PackReceipts[index].SampledCollectibleIds
                snapshot.PackReceipts[index].SampledCollectibleIds
            |> should be True

        restoredSnapshot.SavedDecks.Length |> should equal 1
        restoredSnapshot.SavedDecks[0].Revision |> should equal 2L
        restoredSnapshot.SavedDecks[0].Name |> should equal "Revised deck"

        sameItems restoredSnapshot.SavedDecks[0].Cards snapshot.SavedDecks[0].Cards
        |> should be True

        retried.Disposition |> should equal PackOpenDisposition.AlreadyOpened

        restored.PackReceipts.Values
        |> Seq.map (fun receipt -> receipt.Sequence)
        |> Seq.max
        |> should equal 2

        retryRandom.ConsumptionIndex |> should equal 0

    [<Test>]
    member _.``historical profile should preserve pack binding while decks use current mechanics``
        ()
        =
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

        restored.BoundAuthorityManifestVersion |> should equal "historical-manifest"

        restoredSnapshot.CollectibleOwnership
        |> Seq.exists (fun item -> item.CardId = historicalCardId && item.Quantity = 1)
        |> should be True

        restoredSnapshot.PackReceipts[0].SampledCollectibleIds
        |> Seq.exists (fun cardId -> cardId = historicalCardId)
        |> should be True

        restoredSnapshot.SavedDecks[0].Cards
        |> Seq.exists (fun card -> card.CardId = "HISTORICAL-DECK-CARD" && card.Quantity = 59)
        |> should be True

        revised.Deck.Cards.Keys
        |> Seq.map (fun cardId -> cardId.Value)
        |> orderedIds
        |> should
            equal
            (currentCards |> Seq.map (fun selection -> selection.CardId.Value) |> orderedIds)

        revised.Deck.Revision.Value |> should equal (historicalDeck.Revision + 1L)

        created.Deck.Cards.Keys
        |> Seq.map (fun cardId -> cardId.Value)
        |> orderedIds
        |> should
            equal
            (revised.Deck.Cards.Keys |> Seq.map (fun cardId -> cardId.Value) |> orderedIds)

        created.Profile.BoundAuthorityManifestVersion
        |> should equal "historical-manifest"

        packFailure |> should equal PackOpenFailure.AuthorityVersionMismatch
        random.ConsumptionIndex |> should equal 0

    [<Test>]
    member _.``snapshot restore should reject invalid identity quantities and duplicates``() =
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

        invalidIdentity.IsInvalidId |> should be True
        negativeQuantity.IsNegativeQuantity |> should be True
        duplicateReceipt.IsDuplicateValue |> should be True

        duplicateKind duplicateReceipt
        |> should equal SnapshotDuplicateKind.PackReceiptId

        duplicateKind duplicateCommand
        |> should equal SnapshotDuplicateKind.PackCommandId

        duplicateSequence.IsInvalidPackSequence |> should be True
        nonPositiveSequence.IsInvalidPackSequence |> should be True
        gappedSequence.IsInvalidPackSequence |> should be True

    [<Test>]
    member _.``snapshot restore should reject current authority and deck claims that are invalid``
        ()
        =
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

        let vimId = (authority.Value.BasicVim |> Array.find _.IsBasic).Id

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

        unknownCurrentCard.IsUnknownCard |> should be True
        invalidRevision.IsInvalidDeckRevision |> should be True
        illegalDeck.IsInvalidSavedDeck |> should be True

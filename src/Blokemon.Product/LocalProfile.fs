namespace rec Blokemon.Product

open System
open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign

// GetValueOrDefault's nullability annotations do not carry a counted value type through
// F# nullness, so every counted lookup is spelled out.
[<AutoOpen>]
module private ProfileCounts =

    let ownedCount (owned: ImmutableDictionary<CardId, int>) (cardId: CardId) =
        match owned.TryGetValue cardId with
        | true, quantity -> quantity
        | _ -> 0

    let grantedCount (granted: Dictionary<CardId, int>) (cardId: CardId) =
        match granted.TryGetValue cardId with
        | true, quantity -> quantity
        | _ -> 0

    let selectedCount (selected: Dictionary<CardId, int64>) (cardId: CardId) =
        match selected.TryGetValue cardId with
        | true, quantity -> quantity
        | _ -> 0L

/// Why a profile could not be created.
type LocalProfileCreationFailure =
    | NoRegularCollectibleAvailable = 0

/// Why a pack could not be opened.
type PackOpenFailure =
    | ReceiptIdAlreadyUsed = 0
    | ElevenCardPackUnavailable = 1
    | AuthorityVersionMismatch = 2
    | PackAllowanceExhausted = 3

/// Whether opening a pack drew new product or replayed a saved one.
type PackOpenDisposition =
    | Opened = 0
    | AlreadyOpened = 1

/// One pack the profile opened, and what it drew.
[<Sealed>]
type PackReceipt
    internal
    (
        id: PackReceiptId,
        commandId: CommandId,
        sequence: int,
        sampledCollectibleIds: ImmutableArray<CardId>
    ) =

    member _.Id = id

    member _.CommandId = commandId

    member _.Sequence = sequence

    member _.SampledCollectibleIds = sampledCollectibleIds

/// The profile that opening a pack produced, with the receipt it wrote.
type PackOpenTransition =
    { Profile: LocalProfile
      Receipt: PackReceipt
      Disposition: PackOpenDisposition }

/// The profile that saving a deck produced, with the deck it wrote.
type DeckSaveTransition =
    { Profile: LocalProfile
      Deck: SavedDeck }

/// The profile that deleting a deck produced, with the deck it removed.
type DeckDeleteTransition =
    { Profile: LocalProfile
      Deck: SavedDeck }

/// The outcome of claiming a starter deck.
[<RequireQualifiedAccess>]
type StarterDeckClaimOutcome =
    | Claimed of Profile: LocalProfile * Claim: StarterDeckClaim
    | AlreadyClaimed of Profile: LocalProfile * Claim: StarterDeckClaim

/// Checks deck selections against the mechanical authority and what a profile owns.
module DeckValidator =

    /// Every legal deck holds exactly this many cards.
    [<Literal>]
    let RequiredCardCount = 60

    /// No deck may hold more copies of one card than this, whatever the card allows.
    [<Literal>]
    let MechanicalCopyLimit = 4

    let private byId (cards: 'card array) (identify: 'card -> string) =
        let index = Dictionary<string, 'card>(StringComparer.Ordinal)

        for card in cards do
            index.Add(identify card, card)

        index

    let private checkCopyLimit
        (issues: ImmutableArray<DeckValidationIssue>.Builder)
        (cardId: CardId)
        (quantity: int64)
        (cardCopyLimit: int)
        =
        let allowed = min cardCopyLimit MechanicalCopyLimit

        if quantity > int64 allowed then
            issues.Add(DeckValidationIssue.MechanicalCopyLimitExceeded(cardId, quantity, allowed))

    /// Validates deck selections for a profile against the current authority.
    let Validate
        (profile: LocalProfile)
        (authority: BlokemonRuntimeManifest)
        (selections: IEnumerable<DeckCardSelection>)
        =
        ArgumentNullException.ThrowIfNull(profile, nameof profile)
        ArgumentNullException.ThrowIfNull(authority, nameof authority)
        ArgumentNullException.ThrowIfNull(selections, nameof selections)

        let issues = ImmutableArray.CreateBuilder<DeckValidationIssue>()
        let quantities = Dictionary<CardId, int64>()

        for selection in selections do
            ArgumentNullException.ThrowIfNull(selection, nameof selections)
            ArgumentNullException.ThrowIfNull(selection.CardId, nameof selections)

            if selection.Quantity <= 0 then
                issues.Add(
                    DeckValidationIssue.QuantityMustBePositive(selection.CardId, selection.Quantity)
                )
            else
                quantities[selection.CardId] <-
                    selectedCount quantities selection.CardId + int64 selection.Quantity

        let cardCount = Seq.sum quantities.Values

        if cardCount <> int64 RequiredCardCount then
            issues.Add(DeckValidationIssue.WrongCardCount(cardCount, RequiredCardCount))

        let collectibles = byId authority.Collectibles (fun card -> card.Id)
        let kits = byId authority.Kits (fun card -> card.Id)
        let basicVim = byId authority.BasicVim (fun card -> card.Id)
        let mutable includesRegular = false

        for entry in quantities do
            let cardId = entry.Key
            let quantity = entry.Value

            match
                collectibles.TryGetValue cardId.Value,
                kits.TryGetValue cardId.Value,
                basicVim.TryGetValue cardId.Value
            with
            | (true, collectible), _, _ ->
                includesRegular <- includesRegular || collectible.Rank = BlokemonRank.Regular
                checkCopyLimit issues cardId quantity collectible.StackCopyLimit
                let owned = profile.OwnedCollectibleQuantity cardId

                if quantity > int64 owned then
                    issues.Add(
                        DeckValidationIssue.CollectibleQuantityNotOwned(cardId, quantity, owned)
                    )
            | _, (true, kit), _ ->
                if not kit.FreelyAvailable then
                    issues.Add(DeckValidationIssue.CatalogueCardNotFree cardId)

                checkCopyLimit issues cardId quantity kit.StackCopyLimit
            | _, _, (true, vim) ->
                if not vim.FreelyAvailable then
                    issues.Add(DeckValidationIssue.CatalogueCardNotFree cardId)
            | _ -> issues.Add(DeckValidationIssue.UnknownCard cardId)

        if not includesRegular then
            issues.Add DeckValidationIssue.RegularCollectibleRequired

        if issues.Count > 0 then
            DeckValidationResult.Invalid(issues.ToImmutable())
        else
            quantities
            |> Seq.map (fun entry -> KeyValuePair(entry.Key, int entry.Value))
            |> ImmutableDictionary.CreateRange
            |> ValidatedDeck
            |> DeckValidationResult.Valid

/// One machine's local player: what it owns, what it opened, and what it saved.
type LocalProfile =
    private
        { id: ProfileId
          displayName: DisplayName
          boundAuthorityManifestVersion: string
          guaranteedRegularCollectibleId: CardId
          economy: EconomyRules
          collectibleOwnership: ImmutableDictionary<CardId, int>
          receiptsByCommand: ImmutableDictionary<CommandId, PackReceipt>
          receiptsById: ImmutableDictionary<PackReceiptId, PackReceipt>
          savedDecks: ImmutableDictionary<DeckId, SavedDeck>
          starterDeckClaims: ImmutableArray<StarterDeckClaim> }

    member this.Id = this.id

    member this.DisplayName = this.displayName

    member this.BoundAuthorityManifestVersion = this.boundAuthorityManifestVersion

    member this.GuaranteedRegularCollectibleId = this.guaranteedRegularCollectibleId

    member this.Economy = this.economy

    member this.RemainingPackAllowance =
        EconomyRules.Remaining(this.economy.PackAllowance, this.receiptsById.Count)

    member this.RemainingStarterDeckClaimAllowance =
        EconomyRules.Remaining(
            this.economy.StarterDeckClaimAllowance,
            this.starterDeckClaims.Length
        )

    member this.CollectibleOwnership: IReadOnlyDictionary<CardId, int> =
        this.collectibleOwnership

    member this.PackReceipts: IReadOnlyDictionary<PackReceiptId, PackReceipt> =
        this.receiptsById

    member this.SavedDecks: IReadOnlyDictionary<DeckId, SavedDeck> = this.savedDecks

    member this.StarterDeckClaims = this.starterDeckClaims

    member this.LatestStarterDeckClaim: StarterDeckClaim | null =
        if this.starterDeckClaims.IsEmpty then
            null
        else
            this.starterDeckClaims[this.starterDeckClaims.Length - 1]

    /// A new profile bound to the current authority, playing under unlimited rules.
    static member Create
        (id: ProfileId, displayName: DisplayName, authority: BlokemonRuntimeManifest)
        =
        LocalProfile.Create(id, displayName, authority, EconomyRules.Unlimited)

    /// A new profile bound to the current authority.
    static member Create
        (
            id: ProfileId,
            displayName: DisplayName,
            authority: BlokemonRuntimeManifest,
            economy: EconomyRules | null
        ) =
        ArgumentNullException.ThrowIfNull(id, nameof id)
        ArgumentNullException.ThrowIfNull(displayName, nameof displayName)
        ArgumentNullException.ThrowIfNull(authority, nameof authority)

        let regular =
            authority.Collectibles
            |> Array.filter (fun card -> card.Rank = BlokemonRank.Regular)
            |> Array.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
            |> Array.tryHead

        match regular with
        | None -> DomainResult.Failed LocalProfileCreationFailure.NoRegularCollectibleAvailable
        | Some card ->
            let regularId = CardId.FromAuthority card.Id

            DomainResult.Succeeded
                { id = id
                  displayName = displayName
                  boundAuthorityManifestVersion = authority.ManifestVersion
                  guaranteedRegularCollectibleId = regularId
                  economy =
                    match economy with
                    | null -> EconomyRules.Unlimited
                    | rules -> rules
                  collectibleOwnership = ImmutableDictionary<CardId, int>.Empty.Add(regularId, 1)
                  receiptsByCommand = ImmutableDictionary<CommandId, PackReceipt>.Empty
                  receiptsById = ImmutableDictionary<PackReceiptId, PackReceipt>.Empty
                  savedDecks = ImmutableDictionary<DeckId, SavedDeck>.Empty
                  starterDeckClaims = ImmutableArray<StarterDeckClaim>.Empty }

    /// How many copies of a card the profile owns.
    member this.OwnedCollectibleQuantity(cardId: CardId) =
        ArgumentNullException.ThrowIfNull(cardId, nameof cardId)
        ownedCount this.collectibleOwnership cardId

    /// Opens an eleven-card pack, replaying a saved receipt when the command repeats.
    member this.OpenPack
        (
            commandId: CommandId,
            receiptId: PackReceiptId,
            authority: BlokemonRuntimeManifest,
            random: IBlokemonRandomSource
        ) =
        ArgumentNullException.ThrowIfNull(commandId, nameof commandId)
        ArgumentNullException.ThrowIfNull(receiptId, nameof receiptId)
        ArgumentNullException.ThrowIfNull(authority, nameof authority)
        ArgumentNullException.ThrowIfNull(random, nameof random)

        let packAllowance = Option.ofNullable this.economy.PackAllowance

        match this.receiptsByCommand.TryGetValue commandId with
        | true, replayed ->
            DomainResult.Succeeded
                { Profile = this
                  Receipt = replayed
                  Disposition = PackOpenDisposition.AlreadyOpened }
        | _ when
            not (
                String.Equals(
                    this.boundAuthorityManifestVersion,
                    authority.ManifestVersion,
                    StringComparison.Ordinal
                )
            )
            ->
            DomainResult.Failed PackOpenFailure.AuthorityVersionMismatch
        | _ when this.receiptsById.ContainsKey receiptId ->
            DomainResult.Failed PackOpenFailure.ReceiptIdAlreadyUsed
        | _ when packAllowance |> Option.exists (fun limit -> this.receiptsById.Count >= limit) ->
            DomainResult.Failed PackOpenFailure.PackAllowanceExhausted
        | _ when not (LocalProfile.CanSampleEleven authority) ->
            DomainResult.Failed PackOpenFailure.ElevenCardPackUnavailable
        | _ ->
            let sampledIds =
                BlokemonPackSampler.SampleEleven authority random
                |> Seq.map CardId.FromAuthority
                |> ImmutableArray.CreateRange

            let ownership =
                sampledIds
                |> Seq.fold
                    (fun (owned: ImmutableDictionary<CardId, int>) cardId ->
                        owned.SetItem(cardId, ownedCount owned cardId + 1))
                    this.collectibleOwnership

            let receipt =
                PackReceipt(receiptId, commandId, this.receiptsById.Count + 1, sampledIds)

            DomainResult.Succeeded
                { Profile =
                    { this with
                        collectibleOwnership = ownership
                        receiptsByCommand = this.receiptsByCommand.Add(commandId, receipt)
                        receiptsById = this.receiptsById.Add(receiptId, receipt) }
                  Receipt = receipt
                  Disposition = PackOpenDisposition.Opened }

    /// Saves a new deck.
    member this.CreateDeck
        (
            deckId: DeckId,
            name: DeckName,
            selections: IEnumerable<DeckCardSelection>,
            authority: BlokemonRuntimeManifest
        ) : DomainResult<DeckSaveTransition, DeckSaveFailure> =
        ArgumentNullException.ThrowIfNull(deckId, nameof deckId)
        ArgumentNullException.ThrowIfNull(name, nameof name)
        ArgumentNullException.ThrowIfNull(selections, nameof selections)
        ArgumentNullException.ThrowIfNull(authority, nameof authority)

        if this.savedDecks.ContainsKey deckId then
            DomainResult.Failed(DeckSaveFailure.AlreadyExists deckId)
        else
            match DeckValidator.Validate this authority selections with
            | DeckValidationResult.Valid validDeck ->
                this.SaveDeck(deckId, name, DeckRevision.Initial, validDeck)
            | DeckValidationResult.Invalid issues ->
                DomainResult.Failed(DeckSaveFailure.InvalidDeck issues)

    /// Replaces a saved deck, provided the caller holds its current revision.
    member this.ReviseDeck
        (
            deckId: DeckId,
            expectedRevision: DeckRevision,
            name: DeckName,
            selections: IEnumerable<DeckCardSelection>,
            authority: BlokemonRuntimeManifest
        ) : DomainResult<DeckSaveTransition, DeckSaveFailure> =
        ArgumentNullException.ThrowIfNull(deckId, nameof deckId)
        ArgumentNullException.ThrowIfNull(expectedRevision, nameof expectedRevision)
        ArgumentNullException.ThrowIfNull(name, nameof name)
        ArgumentNullException.ThrowIfNull(selections, nameof selections)
        ArgumentNullException.ThrowIfNull(authority, nameof authority)

        match this.savedDecks.TryGetValue deckId with
        | false, _ -> DomainResult.Failed(DeckSaveFailure.NotFound deckId)
        | _, current when current.Revision <> expectedRevision ->
            DomainResult.Failed(
                DeckSaveFailure.StaleRevision(deckId, expectedRevision, current.Revision)
            )
        | _, current ->
            match current.Revision.TryNext() with
            | None -> DomainResult.Failed(DeckSaveFailure.RevisionExhausted deckId)
            | Some nextRevision ->
                match DeckValidator.Validate this authority selections with
                | DeckValidationResult.Valid validDeck ->
                    this.SaveDeck(deckId, name, nextRevision, validDeck)
                | DeckValidationResult.Invalid issues ->
                    DomainResult.Failed(DeckSaveFailure.InvalidDeck issues)

    // Deleting a deck removes only the deck. Collectible ownership, pack receipts and
    // starter claims are permanent history and are left exactly as they were.
    /// Removes a saved deck.
    member this.DeleteDeck(deckId: DeckId) : DomainResult<DeckDeleteTransition, DeckDeleteFailure> =
        ArgumentNullException.ThrowIfNull(deckId, nameof deckId)

        match this.savedDecks.TryGetValue deckId with
        | false, _ -> DomainResult.Failed DeckDeleteFailure.NotFound
        | _, deck ->
            DomainResult.Succeeded
                { Profile =
                    { this with
                        savedDecks = this.savedDecks.Remove deckId }
                  Deck = deck }

    /// Claims a starter deck, granting its full collectible contents and saving its deck.
    member this.ClaimStarterDeck
        (
            commandId: CommandId,
            definition: StarterDeckDefinition,
            currentAuthority: BlokemonRuntimeManifest
        ) =
        ArgumentNullException.ThrowIfNull(commandId, nameof commandId)
        ArgumentNullException.ThrowIfNull(definition, nameof definition)
        ArgumentNullException.ThrowIfNull(currentAuthority, nameof currentAuthority)

        let existingClaim =
            this.starterDeckClaims |> Seq.tryFind (fun claim -> claim.CommandId = commandId)

        let claimAllowance = Option.ofNullable this.economy.StarterDeckClaimAllowance

        match existingClaim with
        | Some claim when definition.Id = claim.Id ->
            DomainResult.Succeeded(StarterDeckClaimOutcome.AlreadyClaimed(this, claim))
        | Some claim ->
            DomainResult.Failed(
                StarterDeckClaimFailure.CommandConflict(commandId, claim.Id, definition.Id)
            )
        | None when
            claimAllowance
            |> Option.exists (fun limit -> this.starterDeckClaims.Length >= limit)
            ->
            let claimedId =
                match this.LatestStarterDeckClaim with
                | null -> definition.Id
                | latest -> latest.Id

            DomainResult.Failed(
                StarterDeckClaimFailure.AllowanceExhausted(claimedId, definition.Id)
            )
        | None ->
            // Opening a starter always grants its full collectible contents, however many
            // copies of it were opened before.
            let authorityCollectibleIds =
                HashSet<string>(
                    currentAuthority.Collectibles |> Seq.map (fun card -> card.Id),
                    StringComparer.Ordinal
                )

            let grantQuantities = Dictionary<CardId, int>()

            for selection in definition.Cards do
                if authorityCollectibleIds.Contains selection.CardId.Value then
                    grantQuantities[selection.CardId] <-
                        grantedCount grantQuantities selection.CardId + selection.Quantity

            let ownership =
                grantQuantities
                |> Seq.fold
                    (fun (owned: ImmutableDictionary<CardId, int>) entry ->
                        owned.SetItem(entry.Key, ownedCount owned entry.Key + entry.Value))
                    this.collectibleOwnership

            let grants =
                grantQuantities
                |> Seq.sortWith (fun left right ->
                    String.CompareOrdinal(left.Key.Value, right.Key.Value))
                |> Seq.map (fun entry -> StarterCollectibleGrant(entry.Key, entry.Value))
                |> ImmutableArray.CreateRange

            let grantedProfile =
                { this with
                    collectibleOwnership = ownership }

            let selections = definition.Cards :> IEnumerable<DeckCardSelection>

            match DeckValidator.Validate grantedProfile currentAuthority selections with
            | DeckValidationResult.Invalid issues ->
                DomainResult.Failed(StarterDeckClaimFailure.InvalidDeck issues)
            | DeckValidationResult.Valid validatedDeck ->
                let deck =
                    SavedDeck(
                        definition.DeckId,
                        definition.DeckName,
                        DeckRevision.Initial,
                        validatedDeck.Cards
                    )

                let claim = StarterDeckClaim(definition.Id, commandId, grants)

                let profile =
                    { this with
                        collectibleOwnership = ownership
                        savedDecks =
                            if this.savedDecks.ContainsKey deck.Id then
                                this.savedDecks
                            else
                                this.savedDecks.Add(deck.Id, deck)
                        starterDeckClaims = this.starterDeckClaims.Add claim }

                DomainResult.Succeeded(StarterDeckClaimOutcome.Claimed(profile, claim))

    /// The persisted form of the profile.
    member this.ToSnapshot() : LocalProfileSnapshot =
        { AuthorityManifestVersion = this.boundAuthorityManifestVersion
          ProfileId = this.id.Value
          DisplayName = this.displayName.Value
          GuaranteedRegularCollectibleId = this.guaranteedRegularCollectibleId.Value
          CollectibleOwnership =
            this.collectibleOwnership
            |> Seq.sortWith (fun left right ->
                String.CompareOrdinal(left.Key.Value, right.Key.Value))
            |> Seq.map (fun entry ->
                ({ CardId = entry.Key.Value
                   Quantity = entry.Value }
                : CollectibleOwnershipSnapshot))
            |> ImmutableArray.CreateRange
          PackReceipts =
            this.receiptsById.Values
            |> Seq.sortBy (fun receipt -> receipt.Sequence)
            |> Seq.map (fun receipt ->
                { ReceiptId = receipt.Id.Value
                  CommandId = receipt.CommandId.Value
                  Sequence = receipt.Sequence
                  SampledCollectibleIds =
                    receipt.SampledCollectibleIds
                    |> Seq.map (fun cardId -> (cardId.Value: string | null))
                    |> ImmutableArray.CreateRange })
            |> ImmutableArray.CreateRange
          SavedDecks =
            this.savedDecks.Values
            |> Seq.sortWith (fun left right -> String.CompareOrdinal(left.Id.Value, right.Id.Value))
            |> Seq.map LocalProfile.ToSavedDeckSnapshot
            |> ImmutableArray.CreateRange
          StarterDeckClaims =
            this.starterDeckClaims
            |> Seq.map LocalProfile.ToStarterDeckClaimSnapshot
            |> ImmutableArray.CreateRange
          Economy = this.economy.Mode
          EconomyPackAllowance = this.economy.PersistedPackAllowance }

    /// The profile a persisted snapshot describes, or why it is not one.
    static member Restore
        (snapshot: LocalProfileSnapshot, currentAuthority: BlokemonRuntimeManifest)
        =
        LocalProfileRestoration.restore snapshot currentAuthority

    member private this.SaveDeck
        (deckId: DeckId, name: DeckName, revision: DeckRevision, validDeck: ValidatedDeck)
        : DomainResult<DeckSaveTransition, DeckSaveFailure> =
        let deck = SavedDeck(deckId, name, revision, validDeck.Cards)

        DomainResult.Succeeded
            { Profile =
                { this with
                    savedDecks = this.savedDecks.SetItem(deckId, deck) }
              Deck = deck }

    static member private CanSampleEleven(authority: BlokemonRuntimeManifest) =
        let eleven = authority.Products.Eleven

        eleven.Count = 11
        && (eleven.Slots |> Array.sumBy (fun slot -> int64 slot.Count)) = 11L
        && eleven.Slots
           |> Array.forall (fun slot ->
               slot.Count >= 0
               && (authority.Collectibles
                   |> Array.filter (fun card -> card.ProductBucket = slot.Bucket)
                   |> Array.length)
                  >= slot.Count)

    static member private ToSavedDeckSnapshot(deck: SavedDeck) : SavedDeckSnapshot =
        { DeckId = deck.Id.Value
          Name = deck.Name.Value
          Revision = deck.Revision.Value
          Cards =
            deck.Cards
            |> Seq.sortWith (fun left right ->
                String.CompareOrdinal(left.Key.Value, right.Key.Value))
            |> Seq.map (fun entry ->
                ({ CardId = entry.Key.Value
                   Quantity = entry.Value }
                : SavedDeckCardSnapshot))
            |> ImmutableArray.CreateRange }

    static member private ToStarterDeckClaimSnapshot
        (claim: StarterDeckClaim)
        : StarterDeckClaimSnapshot =
        { StarterDeckId = claim.Id.Value
          CommandId = claim.CommandId.Value
          CollectibleGrants =
            claim.CollectibleGrants
            |> Seq.sortWith (fun left right ->
                String.CompareOrdinal(left.CardId.Value, right.CardId.Value))
            |> Seq.map (fun grant ->
                ({ CardId = grant.CardId.Value
                   Quantity = grant.Quantity }
                : StarterCollectibleGrantSnapshot))
            |> ImmutableArray.CreateRange }

/// Rebuilds a profile from its persisted snapshot, refusing anything its own history
/// cannot account for.
module internal LocalProfileRestoration =

    // System.Text.Json can place a null inside an array whose element type forbids one,
    // so every persisted entry is checked before it is read.
    let inline private isMissing (value: 'T) = Object.ReferenceEquals(value, null)

    let private orEmpty (values: ImmutableArray<'T>) =
        if values.IsDefault then
            ImmutableArray<'T>.Empty
        else
            values

    let private countOf (counts: Map<CardId, int>) cardId =
        counts |> Map.tryFind cardId |> Option.defaultValue 0

    /// Re-labels a text failure as a restoration failure at a persisted path.
    let private atPath (path: string) (created: DomainResult<'T, TextValueFailure>) =
        match created with
        | DomainResult.Succeeded value -> DomainResult.Succeeded value
        | DomainResult.Failed failure ->
            DomainResult.Failed(LocalProfileRestorationFailure.InvalidId(path, failure))

    let private isUnknownCard
        (currentCollectibles: Dictionary<string, BlokemonCollectible> | null)
        (cardId: CardId)
        =
        match currentCollectibles with
        | null -> false
        | collectibles -> not (collectibles.ContainsKey cardId.Value)

    let private restoreGrant
        (authorityCollectibles: Dictionary<string, BlokemonCollectible>)
        (claimPath: string)
        (grants: StarterCollectibleGrant list, granted: Set<CardId>)
        (grantIndex: int)
        (grant: StarterCollectibleGrantSnapshot)
        =
        let path = $"{claimPath}.CollectibleGrants[{grantIndex}]"

        result {
            do! failWhen (isMissing grant) (LocalProfileRestorationFailure.MissingEntry path)

            do!
                failWhen
                    (grant.Quantity <= 0)
                    (LocalProfileRestorationFailure.NegativeQuantity(
                        $"{path}.Quantity",
                        grant.Quantity
                    ))

            let! cardId = atPath $"{path}.CardId" (CardId.Create grant.CardId)

            do!
                failWhen
                    (granted.Contains cardId)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.StarterGrantCardId,
                        cardId.Value
                    ))

            do!
                failWhen
                    (not (authorityCollectibles.ContainsKey cardId.Value))
                    (LocalProfileRestorationFailure.UnknownCard($"{path}.CardId", cardId))

            return StarterCollectibleGrant(cardId, grant.Quantity) :: grants, granted.Add cardId
        }

    let private restoreClaim
        (authorityCollectibles: Dictionary<string, BlokemonCollectible>)
        (claims: StarterDeckClaim list, commandIds: Set<CommandId>)
        (claimIndex: int)
        (claimSnapshot: StarterDeckClaimSnapshot)
        =
        let claimPath = $"StarterDeckClaims[{claimIndex}]"

        result {
            do!
                failWhen
                    (isMissing claimSnapshot)
                    (LocalProfileRestorationFailure.MissingEntry claimPath)

            let! starterDeckId =
                atPath
                    $"{claimPath}.StarterDeckId"
                    (StarterDeckId.Create claimSnapshot.StarterDeckId)

            let! commandId =
                atPath $"{claimPath}.CommandId" (CommandId.Create claimSnapshot.CommandId)

            do!
                failWhen
                    (commandIds.Contains commandId)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.StarterClaimCommandId,
                        commandId.Value
                    ))

            let! grants, _ =
                foldIndexed
                    (restoreGrant authorityCollectibles claimPath)
                    ([], Set.empty)
                    (orEmpty claimSnapshot.CollectibleGrants)

            let claim =
                StarterDeckClaim(
                    starterDeckId,
                    commandId,
                    grants |> List.rev |> ImmutableArray.CreateRange
                )

            return claim :: claims, commandIds.Add commandId
        }

    let private restoreOwnershipEntry
        (currentCollectibles: Dictionary<string, BlokemonCollectible> | null)
        (ownership: Map<CardId, int>)
        (index: int)
        (item: CollectibleOwnershipSnapshot)
        =
        let path = $"CollectibleOwnership[{index}]"

        result {
            do! failWhen (isMissing item) (LocalProfileRestorationFailure.MissingEntry path)

            do!
                failWhen
                    (item.Quantity < 0)
                    (LocalProfileRestorationFailure.NegativeQuantity(
                        $"{path}.Quantity",
                        item.Quantity
                    ))

            let! cardId = atPath $"{path}.CardId" (CardId.Create item.CardId)

            do!
                failWhen
                    (ownership.ContainsKey cardId)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.OwnershipCardId,
                        cardId.Value
                    ))

            do!
                failWhen
                    (isUnknownCard currentCollectibles cardId)
                    (LocalProfileRestorationFailure.UnknownCard($"{path}.CardId", cardId))

            return ownership.Add(cardId, item.Quantity)
        }

    let private restoreSampledCard
        (currentCollectibles: Dictionary<string, BlokemonCollectible> | null)
        (receiptPath: string)
        (sampled: CardId list, withinReceipt: Set<CardId>, expected: Map<CardId, int>)
        (cardIndex: int)
        (sampledId: string | null)
        =
        let path = $"{receiptPath}.SampledCollectibleIds[{cardIndex}]"

        result {
            let! cardId = atPath path (CardId.Create sampledId)

            do!
                failWhen
                    (withinReceipt.Contains cardId)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.SampledCardIdWithinReceipt,
                        cardId.Value
                    ))

            do!
                failWhen
                    (isUnknownCard currentCollectibles cardId)
                    (LocalProfileRestorationFailure.UnknownCard(path, cardId))

            return
                cardId :: sampled,
                withinReceipt.Add cardId,
                expected.Add(cardId, countOf expected cardId + 1)
        }

    [<NoComparison; NoEquality>]
    type private ReceiptHistory =
        { byId: Map<PackReceiptId, PackReceipt>
          byCommand: Map<CommandId, PackReceipt>
          sequences: Set<int>
          expectedOwnership: Map<CardId, int> }

    let private restoreReceipt
        (currentCollectibles: Dictionary<string, BlokemonCollectible> | null)
        (history: ReceiptHistory)
        (receiptIndex: int)
        (item: PackReceiptSnapshot)
        =
        let path = $"PackReceipts[{receiptIndex}]"

        result {
            do! failWhen (isMissing item) (LocalProfileRestorationFailure.MissingEntry path)
            let! receiptId = atPath $"{path}.ReceiptId" (PackReceiptId.Create item.ReceiptId)
            let! commandId = atPath $"{path}.CommandId" (CommandId.Create item.CommandId)

            do!
                failWhen
                    (history.byId.ContainsKey receiptId)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.PackReceiptId,
                        receiptId.Value
                    ))

            do!
                failWhen
                    (history.byCommand.ContainsKey commandId)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.PackCommandId,
                        commandId.Value
                    ))

            do!
                failWhen
                    (item.Sequence <= 0 || history.sequences.Contains item.Sequence)
                    (LocalProfileRestorationFailure.InvalidPackSequence(receiptId, item.Sequence))

            let sampledSnapshots = orEmpty item.SampledCollectibleIds

            do!
                failWhen
                    (sampledSnapshots.Length <> 11)
                    (LocalProfileRestorationFailure.InvalidPackCardCount(
                        receiptId,
                        sampledSnapshots.Length
                    ))

            let! sampled, _, expectedOwnership =
                foldIndexed
                    (restoreSampledCard currentCollectibles path)
                    ([], Set.empty, history.expectedOwnership)
                    sampledSnapshots

            let receipt =
                PackReceipt(
                    receiptId,
                    commandId,
                    item.Sequence,
                    sampled |> List.rev |> ImmutableArray.CreateRange
                )

            return
                { byId = history.byId.Add(receiptId, receipt)
                  byCommand = history.byCommand.Add(commandId, receipt)
                  sequences = history.sequences.Add item.Sequence
                  expectedOwnership = expectedOwnership }
        }

    let private checkSequenceRun (byId: Map<PackReceiptId, PackReceipt>) =
        byId
        |> Map.toSeq
        |> Seq.map snd
        |> Seq.sortBy (fun receipt -> receipt.Sequence)
        |> Seq.indexed
        |> Seq.tryPick (fun (index, receipt) ->
            if receipt.Sequence <> index + 1 then
                Some(
                    LocalProfileRestorationFailure.InvalidPackSequence(
                        receipt.Id,
                        receipt.Sequence
                    )
                )
            else
                None)
        |> function
            | Some failure -> DomainResult.Failed failure
            | None -> DomainResult.Succeeded()

    let private checkOwnershipHistory (ownership: Map<CardId, int>) (expected: Map<CardId, int>) =
        Seq.append (Map.keys ownership) (Map.keys expected)
        |> Seq.distinct
        |> Seq.sortWith (fun left right -> String.CompareOrdinal(left.Value, right.Value))
        |> Seq.tryPick (fun cardId ->
            let actual = countOf ownership cardId
            let wanted = countOf expected cardId

            if actual <> wanted then
                Some(
                    LocalProfileRestorationFailure.OwnershipHistoryMismatch(cardId, actual, wanted)
                )
            else
                None)
        |> function
            | Some failure -> DomainResult.Failed failure
            | None -> DomainResult.Succeeded()

    let private restoreDeckCard
        (deckPath: string)
        (selections: DeckCardSelection list, deckCards: Map<CardId, int>)
        (cardIndex: int)
        (card: SavedDeckCardSnapshot)
        =
        let path = $"{deckPath}.Cards[{cardIndex}]"

        result {
            do! failWhen (isMissing card) (LocalProfileRestorationFailure.MissingEntry path)

            do!
                failWhen
                    (card.Quantity < 0)
                    (LocalProfileRestorationFailure.NegativeQuantity(
                        $"{path}.Quantity",
                        card.Quantity
                    ))

            let! cardId = atPath $"{path}.CardId" (CardId.Create card.CardId)

            do!
                failWhen
                    (deckCards.ContainsKey cardId)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.DeckCardId,
                        cardId.Value
                    ))

            return
                ({ CardId = cardId
                   Quantity = card.Quantity }
                : DeckCardSelection)
                :: selections,
                deckCards.Add(cardId, card.Quantity)
        }

    let private restoreDeck
        (baseProfile: LocalProfile)
        (currentAuthority: BlokemonRuntimeManifest)
        (isCurrentAuthority: bool)
        (path: string)
        (item: SavedDeckSnapshot)
        : DomainResult<SavedDeck, LocalProfileRestorationFailure> =
        result {
            do! failWhen (isMissing item) (LocalProfileRestorationFailure.MissingEntry path)
            let! deckId = atPath $"{path}.DeckId" (DeckId.Create item.DeckId)

            let! deckName =
                match DeckName.Create item.Name with
                | DomainResult.Succeeded value -> DomainResult.Succeeded value
                | DomainResult.Failed failure ->
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidDeckName(deckId, failure)
                    )

            let! revision =
                match DeckRevision.Create item.Revision with
                | DomainResult.Succeeded value -> DomainResult.Succeeded value
                | DomainResult.Failed _ ->
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidDeckRevision(deckId, item.Revision)
                    )

            let! reversedSelections, deckCards =
                foldIndexed (restoreDeckCard path) ([], Map.empty) (orEmpty item.Cards)

            let selections = List.rev reversedSelections

            if isCurrentAuthority then
                match
                    DeckValidator.Validate baseProfile currentAuthority (List.toSeq selections)
                with
                | DeckValidationResult.Invalid issues ->
                    return!
                        DomainResult.Failed(
                            LocalProfileRestorationFailure.InvalidSavedDeck(deckId, issues)
                        )
                | DeckValidationResult.Valid validDeck ->
                    return SavedDeck(deckId, deckName, revision, validDeck.Cards)
            elif selections |> List.exists (fun selection -> selection.Quantity = 0) then
                let issues =
                    selections
                    |> List.filter (fun selection -> selection.Quantity = 0)
                    |> List.map (fun selection ->
                        DeckValidationIssue.QuantityMustBePositive(
                            selection.CardId,
                            selection.Quantity
                        ))
                    |> ImmutableArray.CreateRange

                return!
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidSavedDeck(deckId, issues)
                    )
            else
                return
                    SavedDeck(deckId, deckName, revision, ImmutableDictionary.CreateRange deckCards)
        }

    let private restoreDeckAt
        (baseProfile: LocalProfile)
        (currentAuthority: BlokemonRuntimeManifest)
        (isCurrentAuthority: bool)
        (decks: Map<DeckId, SavedDeck>)
        (deckIndex: int)
        (item: SavedDeckSnapshot)
        =
        result {
            let! deck =
                restoreDeck
                    baseProfile
                    currentAuthority
                    isCurrentAuthority
                    $"SavedDecks[{deckIndex}]"
                    item

            do!
                failWhen
                    (decks.ContainsKey deck.Id)
                    (LocalProfileRestorationFailure.DuplicateValue(
                        SnapshotDuplicateKind.DeckId,
                        deck.Id.Value
                    ))

            return decks.Add(deck.Id, deck)
        }

    let restore (snapshot: LocalProfileSnapshot) (currentAuthority: BlokemonRuntimeManifest) =
        ArgumentNullException.ThrowIfNull(snapshot, nameof snapshot)
        ArgumentNullException.ThrowIfNull(currentAuthority, nameof currentAuthority)

        let authorityCollectibles =
            Dictionary<string, BlokemonCollectible>(StringComparer.Ordinal)

        for card in currentAuthority.Collectibles do
            authorityCollectibles.Add(card.Id, card)

        result {
            let! manifestVersion =
                match snapshot.AuthorityManifestVersion with
                | null ->
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidId(
                            "AuthorityManifestVersion",
                            TextValueFailure.Required
                        )
                    )
                | version when String.IsNullOrWhiteSpace version ->
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.InvalidId(
                            "AuthorityManifestVersion",
                            TextValueFailure.Required
                        )
                    )
                | version -> DomainResult.Succeeded version

            let! profileId = atPath "ProfileId" (ProfileId.Create snapshot.ProfileId)

            let! displayName =
                match DisplayName.Create snapshot.DisplayName with
                | DomainResult.Succeeded value -> DomainResult.Succeeded value
                | DomainResult.Failed failure ->
                    DomainResult.Failed(LocalProfileRestorationFailure.InvalidDisplayName failure)

            let! starterId =
                atPath
                    "GuaranteedRegularCollectibleId"
                    (CardId.Create snapshot.GuaranteedRegularCollectibleId)

            let! economy =
                match EconomyRules.Create(snapshot.Economy, snapshot.EconomyPackAllowance) with
                | DomainResult.Succeeded value -> DomainResult.Succeeded value
                | DomainResult.Failed failure ->
                    let unknownMode = failure = EconomyRulesFailure.UnknownMode

                    DomainResult.Failed(
                        LocalProfileRestorationFailure.EconomyRuleViolation(
                            (if unknownMode then
                                 EconomyViolationKind.UnknownMode
                             else
                                 EconomyViolationKind.InvalidPackAllowance),
                            (if unknownMode then
                                 int snapshot.Economy
                             else
                                 snapshot.EconomyPackAllowance),
                            0
                        )
                    )

            let isCurrentAuthority =
                String.Equals(
                    manifestVersion,
                    currentAuthority.ManifestVersion,
                    StringComparison.Ordinal
                )

            let currentCollectibles: Dictionary<string, BlokemonCollectible> | null =
                if isCurrentAuthority then authorityCollectibles else null

            do!
                if not isCurrentAuthority then
                    DomainResult.Succeeded()
                else
                    match authorityCollectibles.TryGetValue starterId.Value with
                    | true, starter when starter.Rank = BlokemonRank.Regular ->
                        DomainResult.Succeeded()
                    | true, _ ->
                        DomainResult.Failed(
                            LocalProfileRestorationFailure.StarterNotRegular starterId
                        )
                    | _ ->
                        DomainResult.Failed(
                            LocalProfileRestorationFailure.UnknownCard(
                                "GuaranteedRegularCollectibleId",
                                starterId
                            )
                        )

            let! reversedClaims, _ =
                foldIndexed
                    (restoreClaim authorityCollectibles)
                    ([], Set.empty)
                    (orEmpty snapshot.StarterDeckClaims)

            let claims = List.rev reversedClaims

            let! ownership =
                foldIndexed
                    (restoreOwnershipEntry currentCollectibles)
                    Map.empty
                    (orEmpty snapshot.CollectibleOwnership)

            let! receipts =
                foldIndexed
                    (restoreReceipt currentCollectibles)
                    { byId = Map.empty
                      byCommand = Map.empty
                      sequences = Set.empty
                      expectedOwnership = Map.empty.Add(starterId, 1) }
                    (orEmpty snapshot.PackReceipts)

            do! checkSequenceRun receipts.byId

            let expectedOwnership =
                claims
                |> Seq.collect (fun claim -> claim.CollectibleGrants)
                |> Seq.fold
                    (fun (counts: Map<CardId, int>) (grant: StarterCollectibleGrant) ->
                        counts.Add(grant.CardId, countOf counts grant.CardId + grant.Quantity))
                    receipts.expectedOwnership

            do! checkOwnershipHistory ownership expectedOwnership

            do!
                match Option.ofNullable economy.PackAllowance with
                | Some limit when receipts.byId.Count > limit ->
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.EconomyRuleViolation(
                            EconomyViolationKind.PackAllowanceExceeded,
                            receipts.byId.Count,
                            limit
                        )
                    )
                | _ -> DomainResult.Succeeded()

            do!
                match Option.ofNullable economy.StarterDeckClaimAllowance with
                | Some limit when List.length claims > limit ->
                    DomainResult.Failed(
                        LocalProfileRestorationFailure.EconomyRuleViolation(
                            EconomyViolationKind.StarterDeckClaimAllowanceExceeded,
                            List.length claims,
                            limit
                        )
                    )
                | _ -> DomainResult.Succeeded()

            let baseProfile =
                { id = profileId
                  displayName = displayName
                  boundAuthorityManifestVersion = manifestVersion
                  guaranteedRegularCollectibleId = starterId
                  economy = economy
                  collectibleOwnership = ImmutableDictionary.CreateRange ownership
                  receiptsByCommand = ImmutableDictionary.CreateRange receipts.byCommand
                  receiptsById = ImmutableDictionary.CreateRange receipts.byId
                  savedDecks = ImmutableDictionary<DeckId, SavedDeck>.Empty
                  starterDeckClaims = ImmutableArray<StarterDeckClaim>.Empty }

            let! savedDecks =
                foldIndexed
                    (restoreDeckAt baseProfile currentAuthority isCurrentAuthority)
                    Map.empty
                    (orEmpty snapshot.SavedDecks)

            return
                { baseProfile with
                    savedDecks = ImmutableDictionary.CreateRange savedDecks
                    starterDeckClaims = ImmutableArray.CreateRange claims }
        }

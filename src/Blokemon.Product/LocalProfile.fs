namespace rec Blokemon.Product

open System
open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign

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

// The type carries the whole of a profile and none of the work: every operation below hands
// its state to the module that owns that concern and wraps whatever comes back.
/// One machine's local player: what it owns, what it opened, and what it saved.
type LocalProfile =
    private
        { state: LocalProfileState }

    member this.Id = this.state.id

    member this.DisplayName = this.state.displayName

    member this.BoundAuthorityManifestVersion = this.state.boundAuthorityManifestVersion

    member this.GuaranteedRegularCollectibleId = this.state.guaranteedRegularCollectibleId

    member this.Economy = this.state.economy

    member this.RemainingPackAllowance =
        EconomyRules.Remaining(this.state.economy.PackAllowance, this.state.receiptsById.Count)

    member this.RemainingStarterDeckClaimAllowance =
        EconomyRules.Remaining(
            this.state.economy.StarterDeckClaimAllowance,
            this.state.starterDeckClaims.Length
        )

    member this.CollectibleOwnership: IReadOnlyDictionary<CardId, int> =
        this.state.collectibleOwnership

    member this.PackReceipts: IReadOnlyDictionary<PackReceiptId, PackReceipt> =
        this.state.receiptsById

    member this.SavedDecks: IReadOnlyDictionary<DeckId, SavedDeck> = this.state.savedDecks

    member this.StarterDeckClaims = this.state.starterDeckClaims

    member this.LatestStarterDeckClaim: StarterDeckClaim | null =
        this.state.LatestStarterDeckClaim

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
                { state =
                    { id = id
                      displayName = displayName
                      boundAuthorityManifestVersion = authority.ManifestVersion
                      historicalAuthorityManifestVersions = ImmutableArray<string>.Empty
                      unavailableHistoricalCardIds = Set.empty
                      guaranteedRegularCollectibleId = regularId
                      economy =
                        match economy with
                        | null -> EconomyRules.Unlimited
                        | rules -> rules
                      collectibleOwnership =
                        ImmutableDictionary<CardId, int>.Empty.Add(regularId, 1)
                      receiptsByCommand = ImmutableDictionary<CommandId, PackReceipt>.Empty
                      receiptsById = ImmutableDictionary<PackReceiptId, PackReceipt>.Empty
                      savedDecks = ImmutableDictionary<DeckId, SavedDeck>.Empty
                      starterDeckClaims = ImmutableArray<StarterDeckClaim>.Empty } }

    /// How many copies of a card the profile owns.
    member this.OwnedCollectibleQuantity(cardId: CardId) =
        ArgumentNullException.ThrowIfNull(cardId, nameof cardId)
        this.state.OwnedQuantity cardId

    /// Rebinds a restored profile to the checked-out authority without changing its product state.
    member this.MigrateAuthority(currentAuthority: BlokemonRuntimeManifest) =
        ArgumentNullException.ThrowIfNull(currentAuthority, nameof currentAuthority)

        if
            String.Equals(
                this.state.boundAuthorityManifestVersion,
                currentAuthority.ManifestVersion,
                StringComparison.Ordinal
            )
        then
            this
        else
            let currentCardIds =
                Seq.concat
                    [ currentAuthority.Collectibles |> Seq.map _.Id
                      currentAuthority.Kits |> Seq.map _.Id
                      currentAuthority.BasicVim |> Seq.map _.Id ]
                |> Set.ofSeq

            let unavailableCardIds =
                seq {
                    yield this.state.guaranteedRegularCollectibleId
                    yield! this.state.collectibleOwnership.Keys

                    for receipt in this.state.receiptsById.Values do
                        yield! receipt.SampledCollectibleIds

                    for deck in this.state.savedDecks.Values do
                        yield! deck.Cards.Keys

                    for claim in this.state.starterDeckClaims do
                        yield! claim.CollectibleGrants |> Seq.map _.CardId
                }
                |> Seq.filter (fun cardId -> not (currentCardIds.Contains cardId.Value))
                |> Seq.fold
                    (fun historical cardId -> historical |> Set.add cardId)
                    this.state.unavailableHistoricalCardIds

            { state =
                { this.state with
                    boundAuthorityManifestVersion = currentAuthority.ManifestVersion
                    historicalAuthorityManifestVersions =
                        if
                            this.state.historicalAuthorityManifestVersions.Contains(
                                this.state.boundAuthorityManifestVersion
                            )
                        then
                            this.state.historicalAuthorityManifestVersions
                        else
                            this.state.historicalAuthorityManifestVersions.Add(
                                this.state.boundAuthorityManifestVersion
                            )
                    unavailableHistoricalCardIds = unavailableCardIds } }

    /// Opens an eleven-card pack, replaying a saved receipt when the command repeats.
    member this.OpenPack
        (
            commandId: CommandId,
            receiptId: PackReceiptId,
            authority: BlokemonRuntimeManifest,
            random: IBlokemonRandomSource
        ) : DomainResult<PackOpenTransition, PackOpenFailure> =
        match ProfilePacks.openPack this.state commandId receiptId authority random with
        | DomainResult.Failed failure -> DomainResult.Failed failure
        | DomainResult.Succeeded(ReplayedPack receipt) ->
            DomainResult.Succeeded
                { Profile = this
                  Receipt = receipt
                  Disposition = PackOpenDisposition.AlreadyOpened }
        | DomainResult.Succeeded(DrewPack(opened, receipt)) ->
            DomainResult.Succeeded
                { Profile = { state = opened }
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
        match ProfileDecks.createDeck this.state deckId name selections authority with
        | DomainResult.Failed failure -> DomainResult.Failed failure
        | DomainResult.Succeeded(saved, deck) ->
            DomainResult.Succeeded
                { Profile = { state = saved }
                  Deck = deck }

    /// Replaces a saved deck, provided the caller holds its current revision.
    member this.ReviseDeck
        (
            deckId: DeckId,
            expectedRevision: DeckRevision,
            name: DeckName,
            selections: IEnumerable<DeckCardSelection>,
            authority: BlokemonRuntimeManifest
        ) : DomainResult<DeckSaveTransition, DeckSaveFailure> =
        match
            ProfileDecks.reviseDeck this.state deckId expectedRevision name selections authority
        with
        | DomainResult.Failed failure -> DomainResult.Failed failure
        | DomainResult.Succeeded(saved, deck) ->
            DomainResult.Succeeded
                { Profile = { state = saved }
                  Deck = deck }

    /// Removes a saved deck.
    member this.DeleteDeck(deckId: DeckId) : DomainResult<DeckDeleteTransition, DeckDeleteFailure> =
        match ProfileDecks.deleteDeck this.state deckId with
        | DomainResult.Failed failure -> DomainResult.Failed failure
        | DomainResult.Succeeded(remaining, deck) ->
            DomainResult.Succeeded
                { Profile = { state = remaining }
                  Deck = deck }

    /// Claims a starter deck, granting its full collectible contents and saving its deck.
    member this.ClaimStarterDeck
        (
            commandId: CommandId,
            definition: StarterDeckDefinition,
            currentAuthority: BlokemonRuntimeManifest
        ) : DomainResult<StarterDeckClaimOutcome, StarterDeckClaimFailure> =
        match ProfileClaims.claimStarterDeck this.state commandId definition currentAuthority with
        | DomainResult.Failed failure -> DomainResult.Failed failure
        | DomainResult.Succeeded(AlreadyHeldStarter claim) ->
            DomainResult.Succeeded(StarterDeckClaimOutcome.AlreadyClaimed(this, claim))
        | DomainResult.Succeeded(GrantedStarter(granted, claim)) ->
            DomainResult.Succeeded(StarterDeckClaimOutcome.Claimed({ state = granted }, claim))

    /// The persisted form of the profile.
    member this.ToSnapshot() : LocalProfileSnapshot =
        ProfileSnapshotProjection.toSnapshot this.state

    /// The profile a persisted snapshot describes, or why it is not one.
    static member Restore
        (snapshot: LocalProfileSnapshot, currentAuthority: BlokemonRuntimeManifest)
        : DomainResult<LocalProfile, LocalProfileRestorationFailure> =
        match ProfileRestoration.restore snapshot currentAuthority with
        | DomainResult.Failed failure -> DomainResult.Failed failure
        | DomainResult.Succeeded restored -> DomainResult.Succeeded { state = restored }

namespace Blokemon.Product

open System
open System.Collections.Immutable

/// Projects a profile's state onto its persisted form. Every collection is ordered here, so
/// the same profile always writes the same document.
module internal ProfileSnapshotProjection =

    let private toSavedDeckSnapshot (deck: SavedDeck) : SavedDeckSnapshot =
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

    let private toStarterDeckClaimSnapshot (claim: StarterDeckClaim) : StarterDeckClaimSnapshot =
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

    /// The persisted form of a profile's state.
    let toSnapshot (state: LocalProfileState) : LocalProfileSnapshot =
        { AuthorityManifestVersion = state.boundAuthorityManifestVersion
          ProfileId = state.id.Value
          DisplayName = state.displayName.Value
          GuaranteedRegularCollectibleId = state.guaranteedRegularCollectibleId.Value
          CollectibleOwnership =
            state.collectibleOwnership
            |> Seq.sortWith (fun left right ->
                String.CompareOrdinal(left.Key.Value, right.Key.Value))
            |> Seq.map (fun entry ->
                ({ CardId = entry.Key.Value
                   Quantity = entry.Value }
                : CollectibleOwnershipSnapshot))
            |> ImmutableArray.CreateRange
          PackReceipts =
            state.receiptsById.Values
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
            state.savedDecks.Values
            |> Seq.sortWith (fun left right -> String.CompareOrdinal(left.Id.Value, right.Id.Value))
            |> Seq.map toSavedDeckSnapshot
            |> ImmutableArray.CreateRange
          StarterDeckClaims =
            state.starterDeckClaims
            |> Seq.map toStarterDeckClaimSnapshot
            |> ImmutableArray.CreateRange
          Economy = state.economy.Mode
          EconomyPackAllowance = state.economy.PersistedPackAllowance }

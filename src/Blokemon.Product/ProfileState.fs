namespace Blokemon.Product

open System.Collections.Generic
open System.Collections.Immutable

// GetValueOrDefault's nullability annotations do not carry a counted value type through
// F# nullness, so every counted lookup is spelled out.
[<AutoOpen>]
module internal ProfileCounts =

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

// Everything one machine's local player holds. LocalProfile is a thin wrapper over this
// record: the operations that change a profile take and return the state, so each of them
// lives in a module of its own below the type rather than inside it.
type internal LocalProfileState =
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

    member this.LatestStarterDeckClaim: StarterDeckClaim | null =
        if this.starterDeckClaims.IsEmpty then
            null
        else
            this.starterDeckClaims[this.starterDeckClaims.Length - 1]

    /// How many copies of a card the state owns.
    member this.OwnedQuantity(cardId: CardId) =
        ownedCount this.collectibleOwnership cardId

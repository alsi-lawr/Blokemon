namespace Blokemon.Product

open System
open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign

// The rules a set of deck selections has to satisfy. Ownership arrives as a lookup rather
// than as a profile, which keeps the rules below the LocalProfile type instead of recursive
// with it: the profile's own operations validate against their own state, and the public
// DeckValidator hands the rules a profile's lookup.
module internal DeckRules =

    [<Literal>]
    let RequiredCardCount = 60

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

    /// Validates deck selections against the current authority and an ownership lookup.
    let validate
        (ownedQuantity: CardId -> int)
        (authority: BlokemonRuntimeManifest)
        (selections: IEnumerable<DeckCardSelection>)
        =
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
                let owned = ownedQuantity cardId

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

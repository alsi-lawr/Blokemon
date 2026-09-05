namespace Blokemon.Product

open System
open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign

/// What claiming a starter deck did: found the claim already made, or granted it.
type internal StarterClaimStep =
    | AlreadyHeldStarter of Claim: StarterDeckClaim
    | GrantedStarter of State: LocalProfileState * Claim: StarterDeckClaim

/// Claiming a starter deck: the collectibles it grants, and the deck it saves.
module internal ProfileClaims =

    /// Claims a starter deck, granting its full collectible contents and saving its deck.
    let claimStarterDeck
        (state: LocalProfileState)
        (commandId: CommandId)
        (definition: StarterDeckDefinition)
        (currentAuthority: BlokemonRuntimeManifest)
        =
        ArgumentNullException.ThrowIfNull(commandId, nameof commandId)
        ArgumentNullException.ThrowIfNull(definition, nameof definition)
        ArgumentNullException.ThrowIfNull(currentAuthority, nameof currentAuthority)

        let existingClaim =
            state.starterDeckClaims
            |> Seq.tryFind (fun claim -> claim.CommandId = commandId)

        let claimAllowance = Option.ofNullable state.economy.StarterDeckClaimAllowance

        match existingClaim with
        | Some claim when definition.Id = claim.Id ->
            DomainResult.Succeeded(AlreadyHeldStarter claim)
        | Some claim ->
            DomainResult.Failed(
                StarterDeckClaimFailure.CommandConflict(commandId, claim.Id, definition.Id)
            )
        | None when
            claimAllowance
            |> Option.exists (fun limit -> state.starterDeckClaims.Length >= limit)
            ->
            let claimedId =
                match state.LatestStarterDeckClaim with
                | null -> definition.Id
                | latest -> latest.Id

            DomainResult.Failed(
                StarterDeckClaimFailure.AllowanceExhausted(claimedId, definition.Id)
            )
        | None ->
            // Opening a starter always grants its full collectible contents, Blokemon and
            // Trainers alike, however many copies of it were opened before.
            let authorityOwnedIds =
                HashSet<string>(
                    Seq.append
                        (currentAuthority.Collectibles |> Seq.map (fun card -> card.Id))
                        (currentAuthority.Kits |> Seq.map (fun card -> card.Id)),
                    StringComparer.Ordinal
                )

            let grantQuantities = Dictionary<CardId, int>()

            for selection in definition.Cards do
                if authorityOwnedIds.Contains selection.CardId.Value then
                    grantQuantities[selection.CardId] <-
                        grantedCount grantQuantities selection.CardId + selection.Quantity

            let ownership =
                grantQuantities
                |> Seq.fold
                    (fun (owned: ImmutableDictionary<CardId, int>) entry ->
                        owned.SetItem(entry.Key, ownedCount owned entry.Key + entry.Value))
                    state.collectibleOwnership

            let grants =
                grantQuantities
                |> Seq.sortWith (fun left right ->
                    String.CompareOrdinal(left.Key.Value, right.Key.Value))
                |> Seq.map (fun entry -> StarterCollectibleGrant(entry.Key, entry.Value))
                |> ImmutableArray.CreateRange

            let grantedState =
                { state with
                    collectibleOwnership = ownership }

            let selections = definition.Cards :> IEnumerable<DeckCardSelection>

            match DeckRules.validate grantedState.OwnedQuantity currentAuthority selections with
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

                DomainResult.Succeeded(
                    GrantedStarter(
                        { state with
                            collectibleOwnership = ownership
                            savedDecks =
                                if state.savedDecks.ContainsKey deck.Id then
                                    state.savedDecks
                                else
                                    state.savedDecks.Add(deck.Id, deck)
                            starterDeckClaims = state.starterDeckClaims.Add claim },
                        claim
                    )
                )

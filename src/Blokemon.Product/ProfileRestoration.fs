namespace Blokemon.Product

open System
open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Product.ProfileRestorationSteps
open Blokemon.Product.ProfileRestorationHistory
open Blokemon.Product.ProfileRestorationDecks

/// Rebuilds a profile's state from its persisted snapshot, refusing anything its own history
/// cannot account for.
module internal ProfileRestoration =

    let restore
        (snapshot: LocalProfileSnapshot)
        (currentAuthority: BlokemonRuntimeManifest)
        : DomainResult<LocalProfileState, LocalProfileRestorationFailure> =
        ArgumentNullException.ThrowIfNull(snapshot, nameof snapshot)
        ArgumentNullException.ThrowIfNull(currentAuthority, nameof currentAuthority)

        let authorityCollectibles =
            Dictionary<string, BlokemonCollectible>(StringComparer.Ordinal)

        for card in currentAuthority.Collectibles do
            authorityCollectibles.Add(card.Id, card)

        // What a pack can put into a profile: every collectible and every pulled Trainer.
        let authorityPulledIds = HashSet<string>(StringComparer.Ordinal)

        for card in currentAuthority.Collectibles do
            authorityPulledIds.Add card.Id |> ignore

        for card in currentAuthority.Kits |> Array.filter (fun card -> card.Pulled) do
            authorityPulledIds.Add card.Id |> ignore

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

            let currentPulledIds: HashSet<string> | null =
                if isCurrentAuthority then authorityPulledIds else null

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
                    (restoreClaim authorityPulledIds)
                    ([], Set.empty)
                    (orEmpty snapshot.StarterDeckClaims)

            let claims = List.rev reversedClaims

            let! ownership =
                foldIndexed
                    (restoreOwnershipEntry currentPulledIds)
                    Map.empty
                    (orEmpty snapshot.CollectibleOwnership)

            let! receipts =
                foldIndexed
                    (restoreReceipt currentPulledIds)
                    (openingHistory starterId)
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

            let baseState =
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
                    (restoreDeckAt baseState currentAuthority isCurrentAuthority)
                    Map.empty
                    (orEmpty snapshot.SavedDecks)

            return
                { baseState with
                    savedDecks = ImmutableDictionary.CreateRange savedDecks
                    starterDeckClaims = ImmutableArray.CreateRange claims }
        }

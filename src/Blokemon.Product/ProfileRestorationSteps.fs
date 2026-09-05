namespace Blokemon.Product

open System
open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign

/// Reading one persisted entry at a time: the checks every restored value goes through, and
/// the ownership entries and starter claims built out of them.
module internal ProfileRestorationSteps =

    // System.Text.Json can place a null inside an array whose element type forbids one,
    // so every persisted entry is checked before it is read.
    let inline isMissing (value: 'T) = Object.ReferenceEquals(value, null)

    let orEmpty (values: ImmutableArray<'T>) =
        if values.IsDefault then
            ImmutableArray<'T>.Empty
        else
            values

    let countOf (counts: Map<CardId, int>) cardId =
        counts |> Map.tryFind cardId |> Option.defaultValue 0

    /// Re-labels a text failure as a restoration failure at a persisted path.
    let atPath (path: string) (created: DomainResult<'T, TextValueFailure>) =
        match created with
        | DomainResult.Succeeded value -> DomainResult.Succeeded value
        | DomainResult.Failed failure ->
            DomainResult.Failed(LocalProfileRestorationFailure.InvalidId(path, failure))

    let isUnknownCard (currentPulledIds: HashSet<string> | null) (cardId: CardId) =
        match currentPulledIds with
        | null -> false
        | pulledIds -> not (pulledIds.Contains cardId.Value)

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

    let restoreClaim
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

    let restoreOwnershipEntry
        (currentPulledIds: HashSet<string> | null)
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
                    (isUnknownCard currentPulledIds cardId)
                    (LocalProfileRestorationFailure.UnknownCard($"{path}.CardId", cardId))

            return ownership.Add(cardId, item.Quantity)
        }

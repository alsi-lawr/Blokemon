namespace Blokemon.Product

open System
open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Product.ProfileRestorationSteps

/// The pack history a persisted profile claims: its receipts, the run of sequence numbers
/// they have to form, and the ownership that history accounts for.
module internal ProfileRestorationHistory =

    [<NoComparison; NoEquality>]
    type ReceiptHistory =
        { byId: Map<PackReceiptId, PackReceipt>
          byCommand: Map<CommandId, PackReceipt>
          sequences: Set<int>
          expectedOwnership: Map<CardId, int> }

    /// The history a profile starts from: no receipts, and the one guaranteed collectible.
    let openingHistory (starterId: CardId) =
        { byId = Map.empty
          byCommand = Map.empty
          sequences = Set.empty
          expectedOwnership = Map.empty.Add(starterId, 1) }

    let private restoreSampledCard
        (currentPulledIds: HashSet<string> | null)
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
                    (isUnknownCard currentPulledIds cardId)
                    (LocalProfileRestorationFailure.UnknownCard(path, cardId))

            return
                cardId :: sampled,
                withinReceipt.Add cardId,
                expected.Add(cardId, countOf expected cardId + 1)
        }

    let restoreReceipt
        (currentPulledIds: HashSet<string> | null)
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
                    (restoreSampledCard currentPulledIds path)
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

    let checkSequenceRun (byId: Map<PackReceiptId, PackReceipt>) =
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

    let checkOwnershipHistory (ownership: Map<CardId, int>) (expected: Map<CardId, int>) =
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

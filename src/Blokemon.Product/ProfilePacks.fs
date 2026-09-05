namespace Blokemon.Product

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign

/// What opening a pack did: replayed a saved receipt, or drew a new one.
type internal PackOpenStep =
    | ReplayedPack of Receipt: PackReceipt
    | DrewPack of State: LocalProfileState * Receipt: PackReceipt

/// Opening product: the eleven-card pack a profile may draw, and the receipt it writes.
module internal ProfilePacks =

    // Any position of a bucket may be a Blokemon or a Trainer, so both of the bucket's pools have
    // to be able to fill every position of that bucket; a pool that cannot leaves the pack
    // unavailable rather than substituting from elsewhere.
    let private canSampleEleven (authority: BlokemonRuntimeManifest) =
        let eleven = authority.Products.Eleven
        let trainers = eleven.Trainers

        eleven.Count = 11
        && (eleven.Slots |> Array.sumBy (fun slot -> int64 slot.Count)) = 11L
        && trainers.GuaranteedPerPack >= 0
        && trainers.GuaranteedPerPack <= eleven.Count
        && trainers.RemainingSlotOdds.Denominator > 0
        && eleven.Slots
           |> Array.forall (fun slot ->
               slot.Count >= 0
               && (BlokemonPackSampler.Pool authority slot.Bucket BlokemonPackCardKind.Blokemon)
                   .Length
                  >= slot.Count
               && (BlokemonPackSampler.Pool authority slot.Bucket BlokemonPackCardKind.Trainer)
                   .Length
                  >= slot.Count)

    /// Opens an eleven-card pack, replaying a saved receipt when the command repeats.
    let openPack
        (state: LocalProfileState)
        (commandId: CommandId)
        (receiptId: PackReceiptId)
        (authority: BlokemonRuntimeManifest)
        (random: IBlokemonRandomSource)
        =
        ArgumentNullException.ThrowIfNull(commandId, nameof commandId)
        ArgumentNullException.ThrowIfNull(receiptId, nameof receiptId)
        ArgumentNullException.ThrowIfNull(authority, nameof authority)
        ArgumentNullException.ThrowIfNull(random, nameof random)

        let packAllowance = Option.ofNullable state.economy.PackAllowance

        match state.receiptsByCommand.TryGetValue commandId with
        | true, replayed -> DomainResult.Succeeded(ReplayedPack replayed)
        | _ when
            not (
                String.Equals(
                    state.boundAuthorityManifestVersion,
                    authority.ManifestVersion,
                    StringComparison.Ordinal
                )
            )
            ->
            DomainResult.Failed PackOpenFailure.AuthorityVersionMismatch
        | _ when state.receiptsById.ContainsKey receiptId ->
            DomainResult.Failed PackOpenFailure.ReceiptIdAlreadyUsed
        | _ when packAllowance |> Option.exists (fun limit -> state.receiptsById.Count >= limit) ->
            DomainResult.Failed PackOpenFailure.PackAllowanceExhausted
        | _ when not (canSampleEleven authority) ->
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
                    state.collectibleOwnership

            let receipt =
                PackReceipt(receiptId, commandId, state.receiptsById.Count + 1, sampledIds)

            DomainResult.Succeeded(
                DrewPack(
                    { state with
                        collectibleOwnership = ownership
                        receiptsByCommand = state.receiptsByCommand.Add(commandId, receipt)
                        receiptsById = state.receiptsById.Add(receiptId, receipt) },
                    receipt
                )
            )

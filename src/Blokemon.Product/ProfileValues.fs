namespace Blokemon.Product

open System.Collections.Immutable

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

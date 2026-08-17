namespace Blokemon.CardGen.Domain

open System.Collections.Immutable

/// One card face printed in a given atmosphere.
type Card =
    {
        /// The canonical identifier of the card.
        Id: CardId

        /// The regions printed on the face, in print order.
        Regions: ImmutableArray<CardRegion>

        /// The stylesheet token of the card's atmosphere, absent on unprinted stock.
        ThemeToken: string option
    }

    /// The type printed on the card, absent when the card has no type disc.
    member this.PrintedType =
        this.Regions
        |> Seq.tryPick (function
            | CardRegion.Vitality(printedType = printedType) -> Some printedType
            | _ -> None)

    /// The name printed on the card, falling back to its identifier.
    member this.DisplayName =
        this.Regions
        |> Seq.tryPick (function
            | CardRegion.Nameplate(name = name) -> Some name
            | _ -> None)
        |> Option.defaultValue this.Id.Value

namespace Blokemon.CardGen.Domain

open System.Collections.Immutable

/// Where an illustration prints on a card face.
[<RequireQualifiedAccess>]
type IllustrationPlacement =
    /// Inside the printed art frame.
    | Framed

    /// Across the printed field, behind the card furniture.
    | Field

    /// Across the whole card, covering the border and field.
    | FullBleed

/// A printed region of a card face.
[<RequireQualifiedAccess>]
type CardRegion =
    /// The coloured stock the face prints onto.
    | PrintedField

    /// The classification label and card name, with no label when the card has none.
    | Nameplate of classification: string option * name: string

    /// The HP plate and main type disc, with no HP when the card has none.
    | Vitality of points: HitPoints option * printedType: BlokemonType

    /// The evolution burst and Evolves-from strip.
    | Lineage of previous: PreviousStage

    /// The bound illustration.
    | Illustration of art: Artwork * placement: IllustrationPlacement

    /// The printed denomination symbol.
    | Denomination of printedType: BlokemonType

    /// The identity strip under the illustration.
    | IdentityStrip of identity: string

    /// The flowing mechanics region, in print order.
    | Mechanics of entries: ImmutableArray<CardEntry>

    /// The printed Weakness, Resistance and Retreat row, each absent when the card has none.
    | Affinities of
        weakness: TypeAffinity option *
        resistance: TypeAffinity option *
        retreat: RetreatCost

    /// The flavour plate and imprint row, each part absent when the card has none.
    | Colophon of
        flavour: string option *
        index: string option *
        rarity: Rarity option *
        number: CollectorNumber option

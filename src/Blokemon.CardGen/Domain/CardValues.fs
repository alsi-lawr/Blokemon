namespace Blokemon.CardGen.Domain

open System
open System.Globalization

/// The canonical identifier of a card.
[<Struct>]
type CardId =
    private
        { Identifier: string }

    /// The identifier text.
    member this.Value = this.Identifier

    /// The identifier text.
    override this.ToString() = this.Identifier

/// The canonical identifier of a card.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CardId =

    /// Creates a card identifier.
    let create (value: string) =
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof value)
        { Identifier = value }

/// The identifier of a mechanical entry.
[<Struct>]
type MechanicalId =
    private
        { Identifier: string }

    /// The identifier text.
    member this.Value = this.Identifier

    /// The identifier text.
    override this.ToString() = this.Identifier

/// The identifier of a mechanical entry.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module MechanicalId =

    /// Creates a mechanical identifier.
    let create (value: string) =
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof value)
        { Identifier = value }

/// The printed HP of a collectible.
[<Struct>]
type HitPoints =
    private
        { Points: int }

    /// The HP amount.
    member this.Value = this.Points

    /// The HP amount as printed.
    override this.ToString() =
        this.Points.ToString(CultureInfo.InvariantCulture)

/// The printed HP of a collectible.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HitPoints =

    /// Creates a printed HP amount.
    let create value =
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0, nameof value)
        { Points = value }

/// The printed Damage of an Attack.
[<Struct>]
type Damage =
    private
        { Amount: int }

    /// The Damage amount.
    member this.Value = this.Amount

    /// The Damage amount as printed.
    override this.ToString() =
        this.Amount.ToString(CultureInfo.InvariantCulture)

/// The printed Damage of an Attack.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Damage =

    /// Creates a printed Damage amount.
    let create value =
        ArgumentOutOfRangeException.ThrowIfNegative(value, nameof value)
        { Amount = value }

/// The printed Retreat cost of a collectible.
[<Struct>]
type RetreatCost =
    private
        { Energy: int }

    /// The number of Energy required.
    member this.Value = this.Energy

/// The printed Retreat cost of a collectible.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module RetreatCost =

    /// Creates a printed Retreat cost.
    let create value =
        ArgumentOutOfRangeException.ThrowIfNegative(value, nameof value)
        { Energy = value }

/// The Prize Cards taken when a collectible is Knocked Out.
[<Struct>]
type PrizeCards =
    private
        { Cards: int }

    /// The number of Prize Cards.
    member this.Value = this.Cards

    /// The Prize Card line printed on the flavour plate.
    member this.PrintedLabel() =
        if this.Cards = 1 then
            "1 Prize Card when Knocked Out"
        else
            $"{this.Cards} Prize Cards when Knocked Out"

/// The Prize Cards taken when a collectible is Knocked Out.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PrizeCards =

    /// Creates a Prize Card count.
    let create value =
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0, nameof value)
        { Cards = value }

/// The collector number printed in the imprint row.
[<Struct>]
type CollectorNumber =
    private
        { Position: int
          Run: int }

    /// The position within the printed run.
    member this.Value = this.Position

    /// The size of the printed run.
    member this.Total = this.Run

    /// The collector number as printed.
    member this.PrintedLabel() =
        let position = this.Position.ToString("D3", CultureInfo.InvariantCulture)
        let run = this.Run.ToString("D3", CultureInfo.InvariantCulture)
        $"{position}/{run}"

/// The collector number printed in the imprint row.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CollectorNumber =

    /// Creates a collector number.
    let create value total =
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0, nameof value)
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, total, nameof value)
        { Position = value; Run = total }

/// The whole-object multiplier applied to a canonical card.
[<Struct>]
type CardScale =
    private
        { Multiplier: float }

    /// The multiplier.
    member this.Value = this.Multiplier

    /// The multiplier as a CSS custom property value.
    member this.ToCssValue() =
        this.Multiplier.ToString("0.##########", CultureInfo.InvariantCulture)

/// The whole-object multiplier applied to a canonical card.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CardScale =

    /// Creates a card scale.
    let create value =
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0.0, nameof value)
        { Multiplier = value }

    /// The unscaled card.
    let canonical = create 1.0

/// An illustration bound to a card.
type Artwork =
    {
        /// The illustration file name.
        FileName: string

        /// The illustration alternative text.
        AltText: string
    }

/// A printed Weakness or Resistance.
type TypeAffinity =
    {
        /// The affected type.
        Type: BlokemonType

        /// The printed modifier.
        Modifier: string
    }

    /// The affinity as printed in the stat row.
    member this.PrintedValue() = $"{this.Type} {this.Modifier}"

/// The immediately previous evolution stage.
type PreviousStage =
    {
        /// The identifier of the previous card.
        Id: CardId

        /// The name of the previous card.
        Name: string

        /// The thumbnail shown in the evolution burst.
        Art: Artwork
    }

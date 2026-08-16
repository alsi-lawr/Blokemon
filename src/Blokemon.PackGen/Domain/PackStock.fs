namespace Blokemon.PackGen.Domain

open System

/// The stock finish a deployment prints its packaging on.
type PackStock =
    /// Metallised film.
    | Gloss = 0

    /// Uncoated board.
    | Kraft = 1

/// The material a pack is printed on.
type PackMaterial =
    /// Metallised film.
    | Gloss = 0

    /// Uncoated board.
    | Kraft = 1

    /// The premium gold colourway.
    | Gold = 2

/// Printed properties of materials.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PackMaterial =

    /// The style token a material prints under.
    let token material =
        match material with
        | PackMaterial.Gloss -> "m-gloss"
        | PackMaterial.Kraft -> "m-kraft"
        | PackMaterial.Gold -> "m-gold"
        | _ -> raise (ArgumentOutOfRangeException(nameof material))

    /// Whether a material carries a visible fibre grain.
    let hasFibre material = material = PackMaterial.Kraft

/// Printed properties of stocks.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PackStock =

    /// The material a stock prints as.
    let asMaterial stock =
        match stock with
        | PackStock.Gloss -> PackMaterial.Gloss
        | PackStock.Kraft -> PackMaterial.Kraft
        | _ -> raise (ArgumentOutOfRangeException(nameof stock))

    /// The label a stock is described by.
    let printedLabel stock =
        match stock with
        | PackStock.Gloss -> "Gloss foil"
        | PackStock.Kraft -> "Kraft board"
        | _ -> raise (ArgumentOutOfRangeException(nameof stock))

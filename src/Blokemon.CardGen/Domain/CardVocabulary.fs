namespace Blokemon.CardGen.Domain

open System

/// The approved public type labels.
type BlokemonType =
    | Blazed = 0
    | Beer = 1
    | Curry = 2
    | Dodgy = 3
    | Geeked = 4
    | Lairy = 5
    | Legend = 6
    | Local = 7
    | Roadie = 8
    | Sober = 9

/// The evolution stage of a collectible.
type Stage =
    | Basic = 0
    | StageOne = 1
    | StageTwo = 2

/// The printed rarity of a card.
type Rarity =
    | Common = 0
    | Uncommon = 1
    | Rare = 2
    | RareHolo = 3

/// The printed forms of the type vocabulary.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module BlokemonType =

    /// The two-letter code for a type.
    let typeCode (printedType: BlokemonType) =
        printedType.ToString().Substring(0, 2).ToUpperInvariant()

/// The printed forms of the stage vocabulary.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Stage =

    /// The printed name of a stage.
    let printedLabel stage =
        match stage with
        | Stage.Basic -> "Basic"
        | Stage.StageOne -> "Stage 1"
        | Stage.StageTwo -> "Stage 2"
        | _ -> raise (ArgumentOutOfRangeException(nameof stage))

    /// The classification label printed in the card header.
    let classificationLabel stage isEvolved =
        if isEvolved then
            (printedLabel stage).ToUpperInvariant()
        else
            $"{printedLabel stage} Blokemon"

/// The printed forms of the rarity vocabulary.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Rarity =

    /// The mark printed in the rarity imprint.
    let mark rarity =
        match rarity with
        | Rarity.Common -> "●"
        | Rarity.Uncommon -> "◆"
        | Rarity.Rare
        | Rarity.RareHolo -> "★"
        | _ -> raise (ArgumentOutOfRangeException(nameof rarity))

    /// The printed name of a rarity.
    let printedLabel rarity =
        match rarity with
        | Rarity.Common -> "Common"
        | Rarity.Uncommon -> "Uncommon"
        | Rarity.Rare -> "Rare"
        | Rarity.RareHolo -> "Rare Holo"
        | _ -> raise (ArgumentOutOfRangeException(nameof rarity))

    /// Whether a rarity prints with a holographic field.
    let isHolo rarity = rarity = Rarity.RareHolo

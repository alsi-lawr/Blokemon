namespace Blokemon.PackGen.Rendering

open System
open Blokemon.PackGen.Domain

/// The printed colours and surface response of a material.
type Palette =
    {
        /// The stops of the stock's own gradient.
        Stock: Stop list

        /// The CSS angle the stock gradient runs at.
        StockAngle: float

        /// The lit edge of a crimped seal.
        SealTop: string

        /// The shaded edge of a crimped seal.
        SealBottom: string

        /// The colour printed text takes.
        Print: string

        /// The wordmark fill.
        WordmarkFill: string

        /// The wordmark outline.
        WordmarkLine: string

        /// The strength of the travelling glint.
        Specular: float

        /// The strength of the crease overlay.
        Crinkle: float

        /// Whether the surface shows a board grain.
        Fibre: bool
    }

/// The printed colours and surface response of a material.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Palette =

    let private gloss =
        { Stock =
            [ Stop.solid 0.0 "#0a2e1f"
              Stop.solid 0.26 "#1e6b49"
              Stop.solid 0.46 "#0f4531"
              Stop.solid 0.62 "#2c7f57"
              Stop.solid 1.0 "#0a2e1f" ]
          StockAngle = 94.0
          SealTop = "#1b5a3e"
          SealBottom = "#0a2e1f"
          Print = "#ffffff"
          WordmarkFill = "#ffe14f"
          WordmarkLine = "#0b3122"
          Specular = 0.5
          Crinkle = 0.7
          Fibre = false }

    let private gold =
        { Stock =
            [ Stop.solid 0.0 "#6b4a0c"
              Stop.solid 0.26 "#d8ad3c"
              Stop.solid 0.46 "#8a6414"
              Stop.solid 0.62 "#f0cc63"
              Stop.solid 1.0 "#6b4a0c" ]
          StockAngle = 94.0
          SealTop = "#a87e1c"
          SealBottom = "#5c3f08"
          Print = "#2a1c02"
          WordmarkFill = "#fff6d2"
          WordmarkLine = "#5c3f08"
          Specular = 0.5
          Crinkle = 0.7
          Fibre = false }

    // Board takes no specular at all and will not hold a fold the way film does, so the crease
    // overlay drops with it rather than being tuned independently.
    let private kraft =
        { Stock =
            [ Stop.solid 0.0 "#bd9360"
              Stop.solid 0.55 "#ac804c"
              Stop.solid 1.0 "#916838" ]
          StockAngle = 180.0
          SealTop = "#ac804c"
          SealBottom = "#7d5628"
          Print = "#33200a"
          WordmarkFill = "#f7e7bd"
          WordmarkLine = "#5f3f16"
          Specular = 0.0
          Crinkle = 0.2
          Fibre = true }

    /// The palette a material prints with.
    let forMaterial material =
        match material with
        | PackMaterial.Gloss -> gloss
        | PackMaterial.Gold -> gold
        | PackMaterial.Kraft -> kraft
        | _ -> raise (ArgumentOutOfRangeException(nameof material))

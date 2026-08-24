namespace Blokemon.Core.SetDesign

open System
open System.IO

/// The approved player-facing label for an internal mechanical type.
module BlokemonMechanicalDisplay =

    let ApprovedLabel (manifest: BlokemonRuntimeManifest) (mechanicalType: BlokemonMechanicalType) =
        ArgumentNullException.ThrowIfNull(manifest, nameof manifest)

        match
            manifest.ApprovedMechanicalDisplayMap
            |> Array.tryFind (fun mapping -> mapping.MechanicalType = mechanicalType)
        with
        | Some mapping -> mapping.ApprovedLabel
        | None ->
            raise (
                InvalidDataException(
                    $"The mechanical display map has no approved label for {nameof BlokemonMechanicalType} value {mechanicalType}."
                )
            )

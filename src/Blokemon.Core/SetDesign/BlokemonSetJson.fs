namespace Blokemon.Core.SetDesign

open System.Text.Json

/// Reads and writes the mechanical authority through its source-generated codec.
module BlokemonSetJson =

    /// Reads the mechanical authority document.
    let RuntimeManifest (json: string) =
        // Serialised straight against the context's own JsonTypeInfo: the output formatting comes
        // entirely from the [JsonSourceGenerationOptions] attribute, with no options object.
        match
            JsonSerializer.Deserialize(json, BlokemonSetJsonContext.Default.BlokemonRuntimeManifest)
        with
        | null -> raise (JsonException($"Could not deserialize {nameof BlokemonRuntimeManifest}."))
        | manifest -> manifest

    /// Writes the mechanical authority document.
    let Serialize (document: BlokemonRuntimeManifest) =
        JsonSerializer.Serialize(document, BlokemonSetJsonContext.Default.BlokemonRuntimeManifest)

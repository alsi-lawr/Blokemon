namespace Blokemon.Core.PublicContent

open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Serialization

/// Reads and writes the public content authority through its source-generated codec.
module BlokemonPublicContentJson =

    // This codec pins its own formatting: the checked-in authority is two-space indented with
    // newline line endings, relaxed escaping, and a trailing newline. UseStringEnumConverter is a
    // source-generator-only option, so the runtime converter is added by hand.
    let private options =
        let options =
            JsonSerializerOptions(
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                IndentCharacter = ' ',
                IndentSize = 2,
                NewLine = "\n",
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                RespectRequiredConstructorParameters = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                WriteIndented = true,
                TypeInfoResolver = BlokemonPublicContentJsonContext.Default
            )

        options.Converters.Add(JsonStringEnumConverter())
        options

    /// Reads the public content authority document.
    let Manifest (json: string) =
        match JsonSerializer.Deserialize<BlokemonPublicContentManifest>(json, options) with
        | null ->
            raise (JsonException($"Could not deserialize {nameof BlokemonPublicContentManifest}."))
        | manifest -> manifest

    /// Writes the public content authority document.
    let Serialize (document: BlokemonPublicContentManifest) =
        JsonSerializer.Serialize<BlokemonPublicContentManifest>(document, options)
        + "\n"

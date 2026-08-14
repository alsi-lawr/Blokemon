using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blokemon.Core.PublicContent;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    WriteIndented = true,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(BlokemonPublicContentManifest))]
internal sealed partial class BlokemonPublicContentJsonContext : JsonSerializerContext;

public static class BlokemonPublicContentJson
{
    private static readonly JsonSerializerOptions _options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        TypeInfoResolver = BlokemonPublicContentJsonContext.Default,
    };

    static BlokemonPublicContentJson() => _options.Converters.Add(new JsonStringEnumConverter());

    public static BlokemonPublicContentManifest Manifest(string json) =>
        Deserialize<BlokemonPublicContentManifest>(json);

    public static string Serialize(BlokemonPublicContentManifest document) =>
        SerializeDocument(document);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, _options)
        ?? throw new JsonException($"Could not deserialize {typeof(T).Name}.");

    private static string SerializeDocument<T>(T document) =>
        JsonSerializer.Serialize(document, _options) + "\n";
}

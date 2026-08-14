using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Blokemon.Core.SetDesign;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    WriteIndented = true,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(BlokemonRuntimeManifest))]
internal sealed partial class BlokemonSetJsonContext : JsonSerializerContext;

public static class BlokemonSetJson
{
    public static BlokemonRuntimeManifest RuntimeManifest(string json) =>
        Deserialize(json, BlokemonSetJsonContext.Default.BlokemonRuntimeManifest);

    public static string Serialize(BlokemonRuntimeManifest document) =>
        JsonSerializer.Serialize(document, BlokemonSetJsonContext.Default.BlokemonRuntimeManifest);

    private static T Deserialize<T>(string json, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(json, typeInfo)
        ?? throw new JsonException($"Could not deserialize {typeof(T).Name}.");
}

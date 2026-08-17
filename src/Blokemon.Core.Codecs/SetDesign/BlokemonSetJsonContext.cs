using System.Text.Json.Serialization;

namespace Blokemon.Core.SetDesign;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    WriteIndented = true,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(BlokemonRuntimeManifest))]
public sealed partial class BlokemonSetJsonContext : JsonSerializerContext;

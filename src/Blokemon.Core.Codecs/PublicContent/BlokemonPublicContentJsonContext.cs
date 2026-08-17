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
public sealed partial class BlokemonPublicContentJsonContext : JsonSerializerContext;

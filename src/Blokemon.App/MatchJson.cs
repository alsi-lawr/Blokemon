using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blokemon.Game;

namespace Blokemon.App;

internal static class MatchJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(ConfigurePolymorphism);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = resolver,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FrozenListJsonConverterFactory());
        return options;
    }

    private static void ConfigurePolymorphism(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(MatchCommand))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$command",
                IgnoreUnrecognizedTypeDiscriminators = false,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new(typeof(MatchCommand.ChooseMulliganBonus), "chooseMulliganBonus"),
                    new(typeof(MatchCommand.ChooseOpening), "chooseOpening"),
                    new(typeof(MatchCommand.AttachVim), "attachVim"),
                    new(typeof(MatchCommand.PlayBloke), "playBloke"),
                    new(typeof(MatchCommand.Promote), "promote"),
                    new(typeof(MatchCommand.PlayKit), "playKit"),
                    new(typeof(MatchCommand.Taxi), "taxi"),
                    new(typeof(MatchCommand.UsePartyTrick), "usePartyTrick"),
                    new(typeof(MatchCommand.Attack), "attack"),
                    new(typeof(MatchCommand.ChuckFossil), "chuckFossil"),
                    new(typeof(MatchCommand.EndRound), "endRound"),
                    new(typeof(MatchCommand.ChooseReplacement), "chooseReplacement"),
                    new(typeof(MatchCommand.ResolveEffectChoice), "resolveEffectChoice"),
                    new(typeof(MatchCommand.ResolveKnockoutTrigger), "resolveKnockoutTrigger"),
                    new(typeof(MatchCommand.ResolveBarChitTrigger), "resolveBarChitTrigger"),
                    new(typeof(MatchCommand.Resign), "resign"),
                },
            };
            return;
        }

        if (typeInfo.Type == typeof(EffectChoice))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$choice",
                IgnoreUnrecognizedTypeDiscriminators = false,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new(typeof(EffectChoice.Optional), "optional"),
                    new(typeof(EffectChoice.Amount), "amount"),
                    new(typeof(EffectChoice.Cards), "cards"),
                    new(typeof(EffectChoice.MechanicalType), "mechanicalType"),
                    new(typeof(EffectChoice.Attack), "attack"),
                    new(typeof(EffectChoice.Distribution), "distribution"),
                    new(typeof(EffectChoice.Attachments), "attachments"),
                },
            };
        }
    }
}

internal sealed class FrozenListJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(FrozenList<>);

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (JsonConverter)
            Activator.CreateInstance(
                typeof(FrozenListJsonConverter<>).MakeGenericType(
                    typeToConvert.GetGenericArguments()[0]
                )
            )!;

    private sealed class FrozenListJsonConverter<T> : JsonConverter<FrozenList<T>>
    {
        public override FrozenList<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("A frozen list must be a JSON array.");
            }

            var values = new List<T>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var value = JsonSerializer.Deserialize<T>(ref reader, options);
                if (value is null)
                {
                    throw new JsonException("Frozen lists cannot contain null values.");
                }
                values.Add(value);
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("The frozen list JSON array is incomplete.");
            }

            return FrozenList<T>.Create(values);
        }

        public override void Write(
            Utf8JsonWriter writer,
            FrozenList<T> value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartArray();
            foreach (var item in value)
            {
                JsonSerializer.Serialize(writer, item, options);
            }
            writer.WriteEndArray();
        }
    }
}

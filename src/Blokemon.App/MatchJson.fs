namespace Blokemon.App

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open Blokemon.Game

type internal FrozenListJsonConverter<'T>() =
    inherit JsonConverter<FrozenList<'T>>()

    override _.Read
        (reader: byref<Utf8JsonReader>, _typeToConvert: Type, options: JsonSerializerOptions)
        =
        if reader.TokenType <> JsonTokenType.StartArray then
            raise (JsonException "A frozen list must be a JSON array.")

        let values = List<'T>()
        let mutable reading = reader.Read()

        while reading && reader.TokenType <> JsonTokenType.EndArray do
            // The non-generic overload is deliberate: Deserialize<'T> returns ''T | null' for an
            // unconstrained 'T, which the null pattern cannot narrow because 'T may be a struct.
            match JsonSerializer.Deserialize(&reader, typeof<'T>, options) with
            | null -> raise (JsonException "Frozen lists cannot contain null values.")
            | value -> values.Add(value :?> 'T)

            reading <- reader.Read()

        if reader.TokenType <> JsonTokenType.EndArray then
            raise (JsonException "The frozen list JSON array is incomplete.")

        FrozenList<'T>.Create(values :> IEnumerable<'T>)

    override _.Write
        (writer: Utf8JsonWriter, value: FrozenList<'T>, options: JsonSerializerOptions)
        =
        writer.WriteStartArray()

        for item in value do
            JsonSerializer.Serialize(writer, item, options)

        writer.WriteEndArray()

type internal FrozenListJsonConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(typeToConvert: Type) =
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() = typedefof<FrozenList<_>>

    override _.CreateConverter(typeToConvert: Type, _options: JsonSerializerOptions) =
        let converter =
            typedefof<FrozenListJsonConverter<_>>
                .MakeGenericType(typeToConvert.GetGenericArguments()[0])
            |> Activator.CreateInstance

        match converter with
        | null -> raise (JsonException "The frozen list converter could not be created.")
        | instance -> instance :?> JsonConverter

/// The options the saved battle and its history are written with: the command and choice
/// hierarchies are Blokemon.Game's, so their polymorphism is registered by hand.
module internal MatchJson =

    let private configurePolymorphism (typeInfo: JsonTypeInfo) =
        if typeInfo.Type = typeof<MatchCommand> then
            let polymorphism =
                JsonPolymorphismOptions(
                    TypeDiscriminatorPropertyName = "$command",
                    IgnoreUnrecognizedTypeDiscriminators = false,
                    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
                )

            for derived in
                [ JsonDerivedType(typeof<MatchCommand.ChooseMulliganBonus>, "chooseMulliganBonus")
                  JsonDerivedType(typeof<MatchCommand.ChooseOpening>, "chooseOpening")
                  JsonDerivedType(typeof<MatchCommand.AttachVim>, "attachVim")
                  JsonDerivedType(typeof<MatchCommand.PlayBloke>, "playBloke")
                  JsonDerivedType(typeof<MatchCommand.Promote>, "promote")
                  JsonDerivedType(typeof<MatchCommand.PlayKit>, "playKit")
                  JsonDerivedType(typeof<MatchCommand.Taxi>, "taxi")
                  JsonDerivedType(typeof<MatchCommand.UsePartyTrick>, "usePartyTrick")
                  JsonDerivedType(typeof<MatchCommand.Attack>, "attack")
                  JsonDerivedType(typeof<MatchCommand.ChuckFossil>, "chuckFossil")
                  JsonDerivedType(typeof<MatchCommand.EndRound>, "endRound")
                  JsonDerivedType(typeof<MatchCommand.ChooseReplacement>, "chooseReplacement")
                  JsonDerivedType(typeof<MatchCommand.ResolveEffectChoice>, "resolveEffectChoice")
                  JsonDerivedType(
                      typeof<MatchCommand.ResolveKnockoutTrigger>,
                      "resolveKnockoutTrigger"
                  )
                  JsonDerivedType(
                      typeof<MatchCommand.ResolveBarChitTrigger>,
                      "resolveBarChitTrigger"
                  )
                  JsonDerivedType(typeof<MatchCommand.Resign>, "resign") ] do
                polymorphism.DerivedTypes.Add derived

            typeInfo.PolymorphismOptions <- polymorphism
        elif typeInfo.Type = typeof<EffectChoice> then
            let polymorphism =
                JsonPolymorphismOptions(
                    TypeDiscriminatorPropertyName = "$choice",
                    IgnoreUnrecognizedTypeDiscriminators = false,
                    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
                )

            for derived in
                [ JsonDerivedType(typeof<EffectChoice.Optional>, "optional")
                  JsonDerivedType(typeof<EffectChoice.Amount>, "amount")
                  JsonDerivedType(typeof<EffectChoice.Cards>, "cards")
                  JsonDerivedType(typeof<EffectChoice.MechanicalType>, "mechanicalType")
                  JsonDerivedType(typeof<EffectChoice.Attack>, "attack")
                  JsonDerivedType(typeof<EffectChoice.Distribution>, "distribution")
                  JsonDerivedType(typeof<EffectChoice.Attachments>, "attachments") ] do
                polymorphism.DerivedTypes.Add derived

            typeInfo.PolymorphismOptions <- polymorphism

    let private createOptions () =
        let resolver = DefaultJsonTypeInfoResolver()
        resolver.Modifiers.Add(Action<JsonTypeInfo> configurePolymorphism)

        let options =
            JsonSerializerOptions(
                JsonSerializerDefaults.Web,
                TypeInfoResolver = resolver,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            )

        options.Converters.Add(JsonStringEnumConverter())
        options.Converters.Add(FrozenListJsonConverterFactory())
        options

    let Options = createOptions ()

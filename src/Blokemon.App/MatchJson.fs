namespace Blokemon.App

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
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

/// The options the saved battle and its history are written with: Blokemon.Game's command and
/// choice unions are F#, which System.Text.Json cannot serialize on its own, so each rides a
/// hand-written converter that writes the ruled $command / $choice payload shape.
module internal MatchJson =

    let private createOptions () =
        let options =
            JsonSerializerOptions(
                JsonSerializerDefaults.Web,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            )

        options.Converters.Add(JsonStringEnumConverter())
        options.Converters.Add(FrozenListJsonConverterFactory())
        options.Converters.Add(EffectChoiceJsonConverter())
        options.Converters.Add(MatchActionJsonConverter())
        options

    let Options = createOptions ()

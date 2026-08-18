namespace Blokemon.App

open System.Text.Json
open System.Text.Json.Serialization

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
        options.Converters.Add(EffectChoiceJsonConverter())
        options.Converters.Add(MatchActionJsonConverter())
        options

    let Options = createOptions ()

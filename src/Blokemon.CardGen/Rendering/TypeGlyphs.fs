namespace Blokemon.CardGen.Rendering

open System
open Blokemon.CardGen.Domain

/// The drawn type symbols.
module TypeGlyphs =

    // Rolling smoke cloud with a rising wisp.
    let private blazed =
        "M6.6 20.4c-2.7 0-4.6-1.9-4.6-4.3 0-2 1.4-3.7 3.3-4.1.3-2.7 2.5-4.7 5.2-4.7 1.9 0 3.6 1 4.5 2.6.5-.2 1-.3 1.6-.3 2.4 0 4.4 2 4.4 4.5 0 3.4-2.4 6.3-6.1 6.3zM13.9 7c1.4-.9 1.8-2 1-3.4-.3-.5-.8-.9-1.5-1.2 2.4-.2 4 .9 4.3 2.6.3 1.6-.7 3-2.5 3.5z"

    // Pint with a foam head.
    let private beer =
        "M5.4 3.4c1-.9 2.4-1 3.5-.3.9-1.3 2.7-1.6 4-.7.5.3.9.8 1.1 1.3 1.4-.5 2.9.2 3.4 1.6.1.4.2.8.1 1.2h1.1L17 20.1c-.2 1.2-1.2 2.1-2.4 2.1H9.4c-1.2 0-2.2-.9-2.4-2.1L5.4 6.5h.9c-.5-1-.4-2.2.5-3.1z"

    // Chilli pod with a stalk.
    let private curry =
        "M11.2 2.2c.5 1 .3 1.9-.4 2.7 1.5-.3 2.6.1 3.4 1.2 2.9 1 4.7 3.7 4.7 7 0 5-3.9 8.7-9.1 8.7-3.2 0-5.8-1.2-7.4-3.2 4.5.9 8-.4 9.6-3.1 1.5-2.5 1.1-5.7-1-7.9-.9.7-2 .8-3.1.3 1.3-1.7 1.5-3.4.6-5.3.9-.5 1.9-.4 2.7.6z"

    // Watchful eye under a heavy brow.
    let private dodgy =
        "M1.8 10.6C4.6 6 8.1 3.7 12 3.7s7.4 2.3 10.2 6.9l-1.6 1.5c-2.6 3.3-5.5 5-8.6 5s-6-1.7-8.6-5zM12 9.4a3.4 3.4 0 1 0 0 6.8 3.4 3.4 0 0 0 0-6.8z"

    // Spiralling stare.
    let private geeked =
        "M12 1.6C6.3 1.6 1.6 6.3 1.6 12S6.3 22.4 12 22.4 22.4 17.7 22.4 12 17.7 1.6 12 1.6zm.3 3.2c3.6 0 6.4 2.6 6.4 6 0 3-2.2 5.2-5 5.2-2.4 0-4.2-1.7-4.2-3.9 0-1.9 1.4-3.3 3.1-3.3 1.5 0 2.6 1.1 2.6 2.5 0 1-.7 1.8-1.6 1.8-.5 0-.9-.3-1-.7-.5.3-.8.9-.8 1.6 0 1.3 1.1 2.3 2.6 2.3 1.9 0 3.3-1.5 3.3-3.6 0-2.6-2.2-4.6-5.1-4.6-3.4 0-6 2.6-6 6.1 0 1 .2 1.9.5 2.7l-2.4 1.5A9.9 9.9 0 0 1 4 12.4c0-4.4 3.5-7.6 8.3-7.6z"

    // Raised fist.
    let private lairy =
        "M6.2 8.5V5.9a2 2 0 0 1 4 0v2.2h.8V4.4a2 2 0 0 1 4 0v3.7h.8V6.2a2 2 0 0 1 4 0v6.5c0 5-2.8 8.5-7.4 8.5-4.3 0-7.6-3.1-7.6-7.3v-3c0-1.2.5-2.2 1.4-2.4z"

    // Crown with a set stone.
    let private legend =
        "M2 5.6l5.3 4.2L12 2.6l4.7 7.2L22 5.6l-1.6 12.8H3.6zM3.4 19.8h17.2v2.2H3.4z"

    // The local terrace.
    let private local =
        "M12 2.2 23 12.4h-3.1v9.4h-4.6v-6.2H8.7v6.2H4.1v-9.4H1zM16.4 3.8h3.2v3.4l-3.2-3z"

    // Gaffered flight case.
    let private roadie =
        "M2.4 5.4h19.2v13.2H2.4zm2.3 2.2v8.8h14.6V7.6zm4.4 3.2h5.8v2.4H9.1z"

    // Mug of tea.
    let private sober =
        "M3.4 4.6h13.4v3h1.8c2.6 0 4.4 1.9 4.4 4.6s-1.8 4.6-4.4 4.6h-2.2c-.7 1.6-2.3 2.6-4.2 2.6H8.2c-2.6 0-4.8-2-4.8-4.6zm13.4 5.2v5.4h1.4c1.2 0 2-.9 2-2.4 0-1.4-.8-2.3-2-2.3z"

    let private path printedType =
        match printedType with
        | BlokemonType.Blazed -> blazed
        | BlokemonType.Beer -> beer
        | BlokemonType.Curry -> curry
        | BlokemonType.Dodgy -> dodgy
        | BlokemonType.Geeked -> geeked
        | BlokemonType.Lairy -> lairy
        | BlokemonType.Legend -> legend
        | BlokemonType.Local -> local
        | BlokemonType.Roadie -> roadie
        | BlokemonType.Sober -> sober
        | _ -> raise (ArgumentOutOfRangeException(nameof printedType))

    let private slug (printedType: BlokemonType) =
        printedType.ToString().ToLowerInvariant()

    let private symbol (printedType: BlokemonType) drawn =
        $"""<symbol id="blokemon-type-{slug printedType}" viewBox="0 0 24 24"><path fill="currentColor" fill-rule="evenodd" d="{drawn}"/></symbol>"""

    /// A reference to a type symbol in the sprite.
    let reference (printedType: BlokemonType) =
        $"""<svg xmlns="http://www.w3.org/2000/svg" class="type-glyph" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><use href="#blokemon-type-{slug printedType}"></use></svg>"""

    /// The sprite holding every type symbol.
    let sprite () =
        [ """<svg xmlns="http://www.w3.org/2000/svg" class="type-glyph-sprite" aria-hidden="true" width="0" height="0" focusable="false"><defs>"""
          symbol BlokemonType.Blazed blazed
          symbol BlokemonType.Beer beer
          symbol BlokemonType.Curry curry
          symbol BlokemonType.Dodgy dodgy
          symbol BlokemonType.Geeked geeked
          symbol BlokemonType.Lairy lairy
          symbol BlokemonType.Legend legend
          symbol BlokemonType.Local local
          symbol BlokemonType.Roadie roadie
          symbol BlokemonType.Sober sober
          "</defs></svg>" ]
        |> String.concat ""

    /// One type symbol as its own embeddable object.
    let glyph (printedType: BlokemonType) =
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24" role="img" aria-label="{printedType} type" data-generated-by="Blokemon.CardGen"><title>{printedType}</title><path fill="#11170c" fill-rule="evenodd" d="{path printedType}"/></svg>"""

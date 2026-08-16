namespace Blokemon.PackGen.Rendering

open Blokemon.PackGen.Domain

/// The drawn artwork of a pack.
module PackArt =

    /// Draws a pack under a profile.
    let Draw (pack: Pack) (profile: PackProfile) =
        match pack.Format with
        | PackFormat.Wrapper(size, resealable) -> WrapperArt.Draw pack profile size resealable
        | PackFormat.Carton -> CartonArt.Draw pack profile

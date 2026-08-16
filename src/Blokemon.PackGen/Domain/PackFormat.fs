namespace Blokemon.PackGen.Domain

/// The printed size of a wrapper.
[<RequireQualifiedAccess>]
type WrapperSize =
    /// The 460 by 830 booster wrapper.
    | Booster

    /// The 340 by 600 channel-pack wrapper.
    | Small

/// The construction a pack is built as.
[<RequireQualifiedAccess>]
type PackFormat =
    /// A flexible wrapper crimped at both ends, carrying a hang tab and a zip when resealable.
    | Wrapper of size: WrapperSize * resealable: bool

    /// A rigid carton standing in perspective.
    | Carton

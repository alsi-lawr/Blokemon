namespace Blokemon.PackGen.Domain

open System
open System.Globalization

/// The identity of an approved packaging object.
type PackKey =
    /// The eleven-card booster.
    | Booster = 0

    /// The sixty-card starter deck carton.
    | StarterDeck = 1

    /// The single-card channel pack.
    | OneForTheRoad = 2

    /// The three-card channel pack.
    | RoundOfThree = 3

    /// The guaranteed-holo channel pack.
    | LockIn = 4

    /// The resealable pouch of three boosters.
    | Session = 5

/// Printed properties of pack identities.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PackKey =

    /// The file-safe name of a pack identity.
    let slug (key: PackKey) =
        key.ToString()
        |> Seq.mapi (fun index letter ->
            if Char.IsUpper letter && index > 0 then
                $"-{Char.ToLowerInvariant letter}"
            else
                string (Char.ToLowerInvariant letter))
        |> String.concat ""

/// The contents printed on a pack foot.
type PackContents =
    {
        /// The contents line printed large.
        Count: string

        /// The declaration printed small above the copyright.
        Declaration: string

        /// Whether the copyright line carries a not-for-resale notice.
        NotForResale: bool
    }

    /// The copyright line printed under the declaration.
    member this.Copyright(noun: string) =
        if this.NotForResale then
            $"not for resale \u00a9 {noun}"
        else
            $"\u00a9 {noun}"

/// The offset a pack enters the shared glint cycle at.
type GlintDelay =
    private
        { Offset: float }

    /// The offset in seconds.
    member this.Seconds = this.Offset

    /// The offset as a CSS duration.
    member this.ToCssValue() =
        $"""{this.Offset.ToString("0.##", CultureInfo.InvariantCulture)}s"""

/// The offset a pack enters the shared glint cycle at.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module GlintDelay =

    /// The length of the shared glint cycle in seconds.
    let cycleSeconds = 5.5

    /// The offset a pack enters the shared glint cycle at.
    let create seconds =
        // Offsets beyond the cycle length are not wrong so much as unreadable: two packs an
        // exact cycle apart glint together while their declared delays look unrelated.
        if seconds < 0.0 || seconds >= cycleSeconds then
            raise (
                ArgumentOutOfRangeException(
                    nameof seconds,
                    seconds,
                    $"a glint offset falls within the {cycleSeconds}s cycle"
                )
            )

        { Offset = seconds }

/// An approved packaging object.
type Pack =
    {
        /// The identity its configured name is looked up by.
        Key: PackKey

        /// The construction it is built as.
        Format: PackFormat

        /// The contents printed on its foot.
        Contents: PackContents

        /// The offset it enters the shared glint cycle at.
        Glint: GlintDelay

        /// The material it always prints on, absent when it follows the configured stock.
        FixedMaterial: PackMaterial option
    }

    /// The material this pack prints on under a stock.
    member this.MaterialUnder(stock: PackStock) =
        this.FixedMaterial |> Option.defaultValue (PackStock.asMaterial stock)

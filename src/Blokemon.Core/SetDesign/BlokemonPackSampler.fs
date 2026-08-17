namespace Blokemon.Core.SetDesign

open System
open System.Collections.Generic

/// A deterministic source of bounded draws.
type IBlokemonRandomSource =
    /// The number of draws consumed so far.
    abstract member ConsumptionIndex: int

    /// Draws a value in [0, exclusiveMaximum).
    abstract member NextInt: exclusiveMaximum: int -> int

/// A SplitMix64 draw source over a fixed seed.
type BlokemonSeededRandom(seed: uint64) =
    let mutable state = seed
    let mutable consumptionIndex = 0

    let nextUInt64 () =
        state <- state + 0x9E3779B97F4A7C15UL
        let mutable value = state
        value <- (value ^^^ (value >>> 30)) * 0xBF58476D1CE4E5B9UL
        value <- (value ^^^ (value >>> 27)) * 0x94D049BB133111EBUL
        value ^^^ (value >>> 31)

    /// The number of draws consumed so far.
    member _.ConsumptionIndex = consumptionIndex

    /// Draws a value in [0, exclusiveMaximum).
    member _.NextInt(exclusiveMaximum: int) =
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum, nameof exclusiveMaximum)
        let bound = uint64 exclusiveMaximum
        let threshold = (0UL - bound) % bound
        let mutable drawn = 0
        let mutable settled = false

        while not settled do
            let value = nextUInt64 ()

            if value >= threshold then
                consumptionIndex <- consumptionIndex + 1
                drawn <- int (value % bound)
                settled <- true

        drawn

    // F# interface implementations are explicit, and C# call sites across four test suites hold
    // this type concretely, so the interface forwards to the concrete members above.
    interface IBlokemonRandomSource with
        member this.ConsumptionIndex = this.ConsumptionIndex
        member this.NextInt(exclusiveMaximum) = this.NextInt(exclusiveMaximum)

/// Draws product from the mechanical authority.
module BlokemonPackSampler =

    /// Draws the one-card product.
    let SampleSingle (manifest: BlokemonRuntimeManifest) (random: IBlokemonRandomSource) : string =
        ArgumentNullException.ThrowIfNull(manifest, nameof manifest)
        ArgumentNullException.ThrowIfNull(random, nameof random)
        manifest.Collectibles[random.NextInt(manifest.Collectibles.Length)].Id

    /// Draws the eleven-card product.
    let SampleEleven
        (manifest: BlokemonRuntimeManifest)
        (random: IBlokemonRandomSource)
        : IReadOnlyList<string> =
        ArgumentNullException.ThrowIfNull(manifest, nameof manifest)
        ArgumentNullException.ThrowIfNull(random, nameof random)
        let result = ResizeArray<string>(11)

        for slot in manifest.Products.Eleven.Slots do
            let available =
                manifest.Collectibles
                |> Array.filter (fun card -> card.ProductBucket = slot.Bucket)
                |> Array.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
                |> Array.map (fun card -> card.Id)
                |> ResizeArray

            for _ in 1 .. slot.Count do
                let index = random.NextInt(available.Count)
                result.Add(available[index])
                available.RemoveAt(index)

        result

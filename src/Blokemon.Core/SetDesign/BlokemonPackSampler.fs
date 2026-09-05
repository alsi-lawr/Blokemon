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

/// Which library a pack position draws from.
type BlokemonPackCardKind =
    | Blokemon = 0
    | Trainer = 1

/// Draws product from the mechanical authority.
module BlokemonPackSampler =

    /// The identities of one bucket and kind, in the order the sampler indexes them.
    let Pool
        (manifest: BlokemonRuntimeManifest)
        (bucket: BlokemonProductBucket)
        (kind: BlokemonPackCardKind)
        : string array =
        ArgumentNullException.ThrowIfNull(manifest, nameof manifest)

        let ids =
            match kind with
            | BlokemonPackCardKind.Blokemon ->
                manifest.Collectibles
                |> Array.filter (fun card -> card.ProductBucket = bucket)
                |> Array.map (fun card -> card.Id)
            | BlokemonPackCardKind.Trainer ->
                manifest.Kits
                |> Array.filter (fun card -> card.ProductBucket = bucket)
                |> Array.map (fun card -> card.Id)
            | _ -> raise (ArgumentOutOfRangeException(nameof kind))

        ids |> Array.sortWith (fun left right -> String.CompareOrdinal(left, right))

    /// The bucket of every position of the eleven-card product, in the order the pack is dealt.
    let private positions (eleven: BlokemonElevenProduct) =
        [| for slot in eleven.Slots do
               for _ in 1 .. slot.Count do
                   yield slot.Bucket |]

    /// Which positions are Trainers: the guaranteed positions placed uniformly among all of them,
    /// then each remaining position at the remaining-slot odds. Every draw comes from the source
    /// in a fixed order, so a seed reproduces the pack.
    let private trainerPositions (eleven: BlokemonElevenProduct) (random: IBlokemonRandomSource) =
        let count = eleven.Count
        let rules = eleven.Trainers
        let isTrainer = Array.zeroCreate<bool> count
        let undecided = ResizeArray<int>(seq { 0 .. count - 1 })

        for _ in 1 .. rules.GuaranteedPerPack do
            let chosen = random.NextInt(undecided.Count)
            isTrainer[undecided[chosen]] <- true
            undecided.RemoveAt(chosen)

        for position in undecided do
            if
                random.NextInt(rules.RemainingSlotOdds.Denominator) < rules.RemainingSlotOdds.Numerator
            then
                isTrainer[position] <- true

        isTrainer

    /// Draws the one-card product.
    let SampleSingle (manifest: BlokemonRuntimeManifest) (random: IBlokemonRandomSource) : string =
        ArgumentNullException.ThrowIfNull(manifest, nameof manifest)
        ArgumentNullException.ThrowIfNull(random, nameof random)
        manifest.Collectibles[random.NextInt(manifest.Collectibles.Length)].Id

    /// Draws the eleven-card product: every position keeps its bucket, the Trainer positions are
    /// decided first, and each card is then drawn without replacement from its bucket-and-kind
    /// pool.
    let SampleEleven
        (manifest: BlokemonRuntimeManifest)
        (random: IBlokemonRandomSource)
        : IReadOnlyList<string> =
        ArgumentNullException.ThrowIfNull(manifest, nameof manifest)
        ArgumentNullException.ThrowIfNull(random, nameof random)
        let eleven = manifest.Products.Eleven
        let buckets = positions eleven
        let isTrainer = trainerPositions eleven random

        let pools =
            Dictionary<struct (BlokemonProductBucket * BlokemonPackCardKind), ResizeArray<string>>()

        let result = ResizeArray<string>(buckets.Length)

        for position in 0 .. buckets.Length - 1 do
            let kind =
                if isTrainer[position] then
                    BlokemonPackCardKind.Trainer
                else
                    BlokemonPackCardKind.Blokemon

            let key = struct (buckets[position], kind)

            let available =
                match pools.TryGetValue key with
                | true, pool -> pool
                | _ ->
                    let pool = ResizeArray(Pool manifest buckets[position] kind)
                    pools[key] <- pool
                    pool

            let index = random.NextInt(available.Count)
            result.Add(available[index])
            available.RemoveAt(index)

        result

/// The odds the eleven-card layout implies, computed from the manifest's own slots and Trainer
/// rules rather than read from it.
module BlokemonPackOdds =

    /// Reduces a ratio to lowest terms.
    let private reduced (numerator: int64) (denominator: int64) =
        let rec gcd (a: int64) (b: int64) = if b = 0L then abs a else gcd b (a % b)
        let divisor = gcd numerator denominator
        struct (numerator / divisor, denominator / divisor)

    /// Whether two odds name the same ratio, whatever terms each is written in.
    let SameRatio (left: BlokemonOdds) (right: BlokemonOdds) =
        int64 left.Numerator * int64 right.Denominator = int64 right.Numerator
                                                         * int64 left.Denominator

    /// The expected number of Trainer positions per pack, as a ratio over the pack: the guaranteed
    /// positions plus the remaining positions at their odds.
    let ExpectedTrainersPerPack (eleven: BlokemonElevenProduct) : BlokemonOdds =
        let rules = eleven.Trainers
        let odds = rules.RemainingSlotOdds

        let struct (numerator, denominator) =
            reduced
                (int64 rules.GuaranteedPerPack * int64 odds.Denominator
                 + int64 (eleven.Count - rules.GuaranteedPerPack) * int64 odds.Numerator)
                (int64 odds.Denominator)

        { Numerator = int numerator
          Denominator = int denominator
          Probability = Nullable() }

    /// The chance one named identity of the slot's bucket and kind appears in a pack: the expected
    /// number of that kind drawn in the bucket, over the pool it is drawn from.
    let NamedIdentityInclusion
        (eleven: BlokemonElevenProduct)
        (slot: BlokemonProductSlot)
        (kind: BlokemonPackCardKind)
        : BlokemonOdds =
        let expected = ExpectedTrainersPerPack eleven

        let struct (kindNumerator, pool) =
            match kind with
            | BlokemonPackCardKind.Trainer ->
                struct (int64 expected.Numerator, slot.TrainerPoolSize)
            | BlokemonPackCardKind.Blokemon ->
                struct (int64 eleven.Count * int64 expected.Denominator - int64 expected.Numerator,
                        slot.BlokemonPoolSize)
            | _ -> raise (ArgumentOutOfRangeException(nameof kind))

        let struct (numerator, denominator) =
            reduced
                (int64 slot.Count * kindNumerator)
                (int64 eleven.Count * int64 expected.Denominator * int64 pool)

        { Numerator = int numerator
          Denominator = int denominator
          Probability = Nullable(float numerator / float denominator) }

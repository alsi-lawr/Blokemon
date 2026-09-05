namespace Blokemon.Core.Tests

open System
open System.Collections.Generic
open System.IO
open System.Linq
open Blokemon.Core.SetDesign
open FsUnit
open TUnit.Core

module private PackAuthorities =

    let mechanics =
        lazy
            (BlokemonSetJson.RuntimeManifest(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")
                )
            ))

    let validationCodes manifest =
        BlokemonSetValidator.ValidateRuntime(manifest).Issues |> Seq.map _.Code

    let withEleven
        (manifest: BlokemonRuntimeManifest)
        (change: BlokemonElevenProduct -> BlokemonElevenProduct)
        =
        { manifest with
            Products =
                { manifest.Products with
                    Eleven = change manifest.Products.Eleven } }

    let withOdds (odds: BlokemonOdds) numerator denominator =
        { odds with
            Numerator = numerator
            Denominator = denominator
            Probability = Nullable(float numerator / float denominator) }

    /// Every card a pack can hold, by id: its bucket and whether it is a Trainer.
    let cards (manifest: BlokemonRuntimeManifest) =
        Seq.append
            (manifest.Collectibles
             |> Seq.map (fun card -> card.Id, (card.ProductBucket, false)))
            (manifest.Kits |> Seq.map (fun card -> card.Id, (card.ProductBucket, true)))
        |> dict

    let bucketOdds (odds: BlokemonBucketOdds) bucket =
        match bucket with
        | BlokemonProductBucket.Rare -> odds.Rare
        | BlokemonProductBucket.Uncommon -> odds.Uncommon
        | BlokemonProductBucket.Common -> odds.Common
        | _ -> raise (ArgumentOutOfRangeException(nameof bucket))

    let sameRatio (left: BlokemonOdds) (right: BlokemonOdds) =
        int64 left.Numerator * int64 right.Denominator = int64 right.Numerator
                                                         * int64 left.Denominator

type ElevenCardPackTests() =

    // BLOKEMON-D-044: one Rare, four Uncommon and six Common across Blokemon and Trainers, at
    // least two Trainers, three on average, and more than three in some packs.
    [<Test>]
    member _.``ten thousand seeded packs should each deal the ruled rarities with at least two Trainers averaging three``
        ()
        =
        let manifest = PackAuthorities.mechanics.Value
        let cards = PackAuthorities.cards manifest
        let packs = 10_000
        let mutable trainersDealt = 0
        let mutable packsAboveThree = 0
        let trainersByBucket = Dictionary<BlokemonProductBucket, int>()

        let dealtIn bucket =
            match trainersByBucket.TryGetValue bucket with
            | true, dealt -> dealt
            | _ -> 0

        for seed in 1..packs do
            let pack =
                BlokemonPackSampler.SampleEleven manifest (BlokemonSeededRandom(uint64 seed))

            let dealt = pack |> Seq.map (fun id -> cards[id]) |> Seq.toArray

            pack.Count |> should equal 11
            pack.Distinct(StringComparer.Ordinal).Count() |> should equal 11

            let bucketCount bucket =
                dealt |> Seq.filter (fun (dealtBucket, _) -> dealtBucket = bucket) |> Seq.length

            bucketCount BlokemonProductBucket.Rare |> should equal 1
            bucketCount BlokemonProductBucket.Uncommon |> should equal 4
            bucketCount BlokemonProductBucket.Common |> should equal 6

            let trainers = dealt |> Seq.filter snd |> Seq.length
            trainers |> should be (greaterThanOrEqualTo 2)
            trainersDealt <- trainersDealt + trainers

            if trainers > 3 then
                packsAboveThree <- packsAboveThree + 1

            for bucket, isTrainer in dealt do
                if isTrainer then
                    trainersByBucket[bucket] <- dealtIn bucket + 1

        let meanTrainers = float trainersDealt / float packs
        abs (meanTrainers - 3.0) |> should be (lessThan 0.1)
        packsAboveThree |> should be (greaterThan 0)

        // The mechanism is blind to rarity: every position is a Trainer three times in eleven,
        // so each bucket's Trainer share sits at that fraction of its positions.
        for slot in manifest.Products.Eleven.Slots do
            let share = float (dealtIn slot.Bucket) / float (packs * slot.Count)

            abs (share - 3.0 / 11.0) |> should be (lessThan 0.03)

    [<Test>]
    member _.``the same seed should deal the same pack and consume the same draws``() =
        let manifest = PackAuthorities.mechanics.Value

        for seed in 1..64 do
            let first = BlokemonSeededRandom(uint64 seed)
            let replay = BlokemonSeededRandom(uint64 seed)
            let pack = BlokemonPackSampler.SampleEleven manifest first
            let repeated = BlokemonPackSampler.SampleEleven manifest replay
            pack.SequenceEqual(repeated) |> should be True
            first.ConsumptionIndex |> should equal replay.ConsumptionIndex

    // A named identity's chance of appearing is the expected number of its kind drawn in its
    // bucket over the pool that draw comes from, computed here from the slots and Trainer rules
    // alone and compared with what the manifest states.
    [<Test>]
    member _.``named identity inclusion odds should follow from the layout and match the manifest``
        ()
        =
        let eleven = PackAuthorities.mechanics.Value.Products.Eleven
        let rules = eleven.Trainers
        let odds = rules.RemainingSlotOdds

        // Expected Trainers per pack, over the odds' denominator.
        let expectedTrainers =
            rules.GuaranteedPerPack * odds.Denominator
            + (eleven.Count - rules.GuaranteedPerPack) * odds.Numerator

        expectedTrainers |> should equal (rules.ExpectedPerPack * odds.Denominator)

        for slot in eleven.Slots do
            let positions = eleven.Count * odds.Denominator

            let trainer =
                { Numerator = slot.Count * expectedTrainers
                  Denominator = positions * slot.TrainerPoolSize
                  Probability = Nullable() }

            let blokemon =
                { Numerator = slot.Count * (positions - expectedTrainers)
                  Denominator = positions * slot.BlokemonPoolSize
                  Probability = Nullable() }

            let statedTrainer =
                PackAuthorities.bucketOdds eleven.NamedIdentityInclusionOdds.Trainers slot.Bucket

            let statedBlokemon =
                PackAuthorities.bucketOdds eleven.NamedIdentityInclusionOdds.Blokemon slot.Bucket

            PackAuthorities.sameRatio statedTrainer trainer |> should be True
            PackAuthorities.sameRatio statedBlokemon blokemon |> should be True

            abs (
                statedTrainer.Probability.Value
                - float trainer.Numerator / float trainer.Denominator
            )
            |> should be (lessThan 1e-12)

            abs (
                statedBlokemon.Probability.Value
                - float blokemon.Numerator / float blokemon.Denominator
            )
            |> should be (lessThan 1e-12)

    [<Test>]
    member _.``runtime validation should reject a layout other than one Rare four Uncommon and six Common``
        ()
        =
        PackAuthorities.withEleven PackAuthorities.mechanics.Value (fun eleven ->
            { eleven with
                Slots =
                    eleven.Slots
                    |> Array.map (fun slot ->
                        match slot.Bucket with
                        | BlokemonProductBucket.Uncommon -> { slot with Count = 3 }
                        | BlokemonProductBucket.Common -> { slot with Count = 7 }
                        | _ -> slot) })
        |> PackAuthorities.validationCodes
        |> should contain "runtime.product-slots"

    [<Test>]
    member _.``runtime validation should reject a pool size that differs from the ruled one``() =
        PackAuthorities.withEleven PackAuthorities.mechanics.Value (fun eleven ->
            { eleven with
                Slots =
                    eleven.Slots
                    |> Array.map (fun slot ->
                        { slot with
                            TrainerPoolSize = slot.TrainerPoolSize + 1 }) })
        |> PackAuthorities.validationCodes
        |> should contain "runtime.product-slots"

    [<Test>]
    member _.``runtime validation should reject a kit whose bucket leaves its pool short``() =
        let manifest = PackAuthorities.mechanics.Value

        let moved =
            manifest.Kits
            |> Array.find (fun kit -> kit.ProductBucket = BlokemonProductBucket.Common)

        { manifest with
            Kits =
                manifest.Kits
                |> Array.map (fun kit ->
                    if kit.Id = moved.Id then
                        { kit with
                            ProductBucket = BlokemonProductBucket.Rare }
                    else
                        kit) }
        |> PackAuthorities.validationCodes
        |> should contain "runtime.product-pool"

    [<Test>]
    member _.``runtime validation should reject fewer than two guaranteed Trainers``() =
        PackAuthorities.withEleven PackAuthorities.mechanics.Value (fun eleven ->
            { eleven with
                Trainers =
                    { eleven.Trainers with
                        GuaranteedPerPack = 1 } })
        |> PackAuthorities.validationCodes
        |> should contain "runtime.product-trainers"

    [<Test>]
    member _.``runtime validation should reject remaining slot odds that do not average three Trainers``
        ()
        =
        PackAuthorities.withEleven PackAuthorities.mechanics.Value (fun eleven ->
            { eleven with
                Trainers =
                    { eleven.Trainers with
                        RemainingSlotOdds =
                            PackAuthorities.withOdds eleven.Trainers.RemainingSlotOdds 1 8 } })
        |> PackAuthorities.validationCodes
        |> should contain "runtime.product-trainers"

    [<Test>]
    member _.``runtime validation should reject an expected Trainer count other than three``() =
        PackAuthorities.withEleven PackAuthorities.mechanics.Value (fun eleven ->
            { eleven with
                Trainers =
                    { eleven.Trainers with
                        ExpectedPerPack = 4 } })
        |> PackAuthorities.validationCodes
        |> should contain "runtime.product-trainers"

    [<Test>]
    member _.``runtime validation should reject inclusion odds that do not follow the layout``() =
        PackAuthorities.withEleven PackAuthorities.mechanics.Value (fun eleven ->
            let stated = eleven.NamedIdentityInclusionOdds

            { eleven with
                NamedIdentityInclusionOdds =
                    { stated with
                        Trainers =
                            { stated.Trainers with
                                Rare = PackAuthorities.withOdds stated.Trainers.Rare 1 49 } } })
        |> PackAuthorities.validationCodes
        |> should contain "runtime.product-odds"

    [<Test>]
    member _.``runtime validation should reject a kit that is not pulled``() =
        let manifest = PackAuthorities.mechanics.Value

        { manifest with
            Kits =
                manifest.Kits
                |> Array.mapi (fun index kit ->
                    if index = 0 then { kit with Pulled = false } else kit) }
        |> PackAuthorities.validationCodes
        |> should contain "runtime.kit-boundary"

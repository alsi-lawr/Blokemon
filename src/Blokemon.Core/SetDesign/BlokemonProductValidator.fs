namespace Blokemon.Core.SetDesign

open System.Collections.Generic

/// Locks the products to the ruled composition: the one-card product over all 151 identities,
/// and the eleven-card pack at one Rare, four Uncommon and six Common across Blokemon and
/// Trainers, with two Trainers guaranteed and three expected.
module internal BlokemonProductValidator =

    let private check = BlokemonValidation.check

    let private expectedSlots =
        [| { Bucket = BlokemonProductBucket.Rare
             Count = 1
             BlokemonPoolSize = 49
             TrainerPoolSize = 10 }
           { Bucket = BlokemonProductBucket.Uncommon
             Count = 4
             BlokemonPoolSize = 49
             TrainerPoolSize = 12 }
           { Bucket = BlokemonProductBucket.Common
             Count = 6
             BlokemonPoolSize = 53
             TrainerPoolSize = 10 } |]

    let private expectedTrainers =
        { GuaranteedPerPack = 2
          RemainingSlotOdds =
            { Numerator = 1
              Denominator = 9
              Probability = System.Nullable() }
          ExpectedPerPack = 3 }

    let private sameTrainerRules (actual: BlokemonTrainerSlotRules) =
        actual.GuaranteedPerPack = expectedTrainers.GuaranteedPerPack
        && actual.ExpectedPerPack = expectedTrainers.ExpectedPerPack
        && BlokemonPackOdds.SameRatio actual.RemainingSlotOdds expectedTrainers.RemainingSlotOdds

    let private oddsFollowLayout (eleven: BlokemonElevenProduct) =
        let bucketOdds (odds: BlokemonBucketOdds) bucket =
            match bucket with
            | BlokemonProductBucket.Rare -> odds.Rare
            | BlokemonProductBucket.Uncommon -> odds.Uncommon
            | BlokemonProductBucket.Common -> odds.Common
            | _ -> raise (System.ArgumentOutOfRangeException(nameof bucket))

        let matches (stated: BlokemonOdds) (computed: BlokemonOdds) =
            BlokemonPackOdds.SameRatio stated computed
            && stated.Probability.HasValue
            && abs (stated.Probability.Value - computed.Probability.Value) < 1e-12

        eleven.Slots
        |> Array.forall (fun slot ->
            matches
                (bucketOdds eleven.NamedIdentityInclusionOdds.Blokemon slot.Bucket)
                (BlokemonPackOdds.NamedIdentityInclusion eleven slot BlokemonPackCardKind.Blokemon)
            && matches
                (bucketOdds eleven.NamedIdentityInclusionOdds.Trainers slot.Bucket)
                (BlokemonPackOdds.NamedIdentityInclusion eleven slot BlokemonPackCardKind.Trainer))

    let validate
        (manifest: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonValidationIssue>)
        =
        let products = manifest.Products
        let eleven = products.Eleven

        check
            (products.Single.Count = 1
             && products.Single.NamedIdentityOdds.Numerator = 1
             && products.Single.NamedIdentityOdds.Denominator = 151)
            "runtime.single-product"
            "The one-card product must be uniform across all 151 identities."
            issues

        check
            (eleven.Count = 11
             && eleven.WithoutReplacementWithinPack
             && not eleven.Pity
             && eleven.DuplicatesAcrossPacks)
            "runtime.eleven-product"
            "The eleven-card product must be no-pity and without replacement within one pack."
            issues

        check
            (eleven.Slots = expectedSlots)
            "runtime.product-slots"
            "The eleven-card product must use one Rare, four Uncommon and six Common slots across Blokemon and Trainers, each drawn from the 49, 49 and 53 Blokemon and 10, 12 and 10 Trainer pools."
            issues

        for slot in expectedSlots do
            check
                ((BlokemonPackSampler.Pool manifest slot.Bucket BlokemonPackCardKind.Blokemon)
                    .Length = slot.BlokemonPoolSize
                 && (BlokemonPackSampler.Pool manifest slot.Bucket BlokemonPackCardKind.Trainer)
                     .Length = slot.TrainerPoolSize)
                "runtime.product-pool"
                $"The {slot.Bucket} product pool must contain {slot.BlokemonPoolSize} Blokemon and {slot.TrainerPoolSize} Trainers."
                issues

        check
            (sameTrainerRules eleven.Trainers
             && BlokemonPackOdds.SameRatio
                 (BlokemonPackOdds.ExpectedTrainersPerPack eleven)
                 { Numerator = expectedTrainers.ExpectedPerPack
                   Denominator = 1
                   Probability = System.Nullable() })
            "runtime.product-trainers"
            "Every eleven-card pack must guarantee two Trainers, give each of the other nine positions a one-in-nine Trainer chance, and so average three Trainers."
            issues

        check
            (oddsFollowLayout eleven)
            "runtime.product-odds"
            "The named-identity inclusion odds must be the ones the eleven-card layout implies for each bucket of Blokemon and of Trainers."
            issues

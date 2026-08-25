namespace Blokemon.Core.SetDesign

open System
open System.Collections.Generic
open System.Linq

module internal BlokemonSetContentValidator =

    let private check = BlokemonValidation.check

    let rec private flatten (instructions: BlokemonEffectInstruction array) =
        seq {
            for instruction in instructions do
                yield instruction
                yield! flatten instruction.Then
                yield! flatten instruction.Otherwise
        }

    let rec private instructionIsClosed (instruction: BlokemonEffectInstruction) =
        instruction.TargetCount >= 0
        && instruction.Predicates |> Array.forall (fun predicate -> predicate.Value >= 0)
        && instruction.Then |> Array.forall instructionIsClosed
        && instruction.Otherwise |> Array.forall instructionIsClosed

    let private programsOf
        (partyTricks: BlokemonPartyTrick array)
        (attacks: BlokemonAttack array)
        (houseRules: BlokemonHouseRule array)
        =
        seq {
            for trick in partyTricks do
                yield trick.Program

            for attack in attacks do
                yield attack.Program

            for rule in houseRules do
                yield rule.Program
        }

    let private validateCollectibles
        (manifest: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonValidationIssue>)
        =
        check
            (manifest.Collectibles
             |> Array.map (fun card -> card.Id)
             |> Array.distinct
             |> Array.length = manifest.Collectibles.Length)
            "runtime.collectible-id"
            "Collectible mechanical IDs must be unique."
            issues

        check
            (manifest.Collectibles
             |> Array.forall (fun card ->
                 card.PresentationStatus = BlokemonPresentationStatus.Accepted))
            "runtime.collectible-presentation"
            "Every collectible must carry accepted presentation."
            issues

        let mechanicalTypeCount = Enum.GetValues<BlokemonMechanicalType>().Length
        let approvedLabelCount = Enum.GetValues<BlokemonApprovedMechanicalLabel>().Length

        check
            (manifest.ApprovedMechanicalDisplayMap.Length = mechanicalTypeCount
             && manifest.ApprovedMechanicalDisplayMap
                |> Array.map (fun value -> value.MechanicalType)
                |> Array.distinct
                |> Array.length = mechanicalTypeCount
             && manifest.ApprovedMechanicalDisplayMap
                |> Array.map (fun value -> value.ApprovedLabel)
                |> Array.distinct
                |> Array.length = approvedLabelCount
             && manifest.ApprovedMechanicalDisplayMap
                 .Single(fun value -> value.MechanicalType = BlokemonMechanicalType.Metal)
                 .ApprovedLabel = BlokemonApprovedMechanicalLabel.Roadie)
            "runtime.mechanical-display-map"
            "Every internal mechanical type must have one approved display label and Metal must display as Roadie."
            issues

        let roadieSoftSpots =
            manifest.Collectibles
            |> Array.filter (fun card ->
                card.SoftSpots
                |> Array.exists (fun modifier ->
                    modifier.MechanicalType = BlokemonMechanicalType.Metal))
            |> Array.map (fun card -> card.Id)
            |> Array.sortWith (fun left right -> String.CompareOrdinal(left, right))

        check
            (roadieSoftSpots = [| "BLK-035"; "BLK-036"; "BLK-124" |])
            "runtime.roadie-soft-spots"
            "Internal Metal must appear on exactly the three D-224 Roadie soft-spot surfaces."
            issues

        let roadieSelector =
            manifest.Collectibles.SingleOrDefault(fun card -> card.Id = "BLK-137")

        let selectableMetal =
            match roadieSelector with
            | null -> false
            | selector ->
                programsOf selector.PartyTricks selector.Attacks selector.HouseRules
                |> Seq.collect flatten
                |> Seq.exists (fun instruction ->
                    instruction.MechanicalTypes |> Array.contains BlokemonMechanicalType.Metal)

        check
            selectableMetal
            "runtime.roadie-selection"
            "BLK-137 must retain internal Metal in the D-224 Roadie selectable-type mechanic."
            issues

        let programs =
            [| for card in manifest.Collectibles do
                   yield! programsOf card.PartyTricks card.Attacks card.HouseRules
               for card in manifest.Kits do
                   yield! programsOf card.PartyTricks card.Attacks card.HouseRules |]

        check
            (programs.Length = 298)
            "runtime.program-count"
            "The typed manifest must structurally define all 298 mechanical programs."
            issues

        check
            (programs |> Array.forall (fun program -> program.Length > 0))
            "runtime.program-empty"
            "Every mechanical program must contain at least one typed instruction."
            issues

        check
            (programs |> Seq.collect flatten |> Seq.forall instructionIsClosed)
            "runtime.program-shape"
            "Every instruction must use a finite, internally consistent typed shape."
            issues

    let private validateSupport
        (manifest: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonValidationIssue>)
        =
        check
            (manifest.Kits
             |> Array.forall (fun card ->
                 card.PresentationStatus = BlokemonPresentationStatus.Accepted
                 && card.FreelyAvailable
                 && not card.Owned
                 && not card.Pulled
                 && not card.Traded))
            "runtime.kit-boundary"
            "Kits must be free, non-owned, non-pulled, non-traded and accepted for presentation."
            issues

        check
            (manifest.BasicVim
             |> Array.forall (fun card ->
                 card.PresentationStatus = BlokemonPresentationStatus.Accepted
                 && card.FreelyAvailable
                 && not card.Owned
                 && not card.Pulled
                 && not card.Traded))
            "runtime.vim-boundary"
            "Basic Vim must be free, non-owned, non-pulled, non-traded and accepted for presentation."
            issues

    let private validateProducts
        (manifest: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonValidationIssue>)
        =
        let products = manifest.Products

        check
            (products.Single.Count = 1
             && products.Single.NamedIdentityOdds.Numerator = 1
             && products.Single.NamedIdentityOdds.Denominator = 151)
            "runtime.single-product"
            "The one-card product must be uniform across all 151 identities."
            issues

        check
            (products.Eleven.Count = 11
             && products.Eleven.WithoutReplacementWithinPack
             && not products.Eleven.Pity
             && products.Eleven.DuplicatesAcrossPacks)
            "runtime.eleven-product"
            "The eleven-card product must be no-pity and without replacement within one pack."
            issues

        let expected =
            [| { Bucket = BlokemonProductBucket.Rare
                 Count = 1
                 PoolSize = 49 }
               { Bucket = BlokemonProductBucket.Uncommon
                 Count = 3
                 PoolSize = 49 }
               { Bucket = BlokemonProductBucket.Common
                 Count = 7
                 PoolSize = 53 } |]

        check
            (products.Eleven.Slots = expected)
            "runtime.product-slots"
            "The eleven-card product must use one Rare, three Uncommon and seven Common slots."
            issues

        for slot in expected do
            check
                (manifest.Collectibles
                 |> Array.filter (fun card -> card.ProductBucket = slot.Bucket)
                 |> Array.length = slot.PoolSize)
                "runtime.product-pool"
                $"The {slot.Bucket} product pool must contain {slot.PoolSize} identities."
                issues


    let validate manifest issues =
        validateCollectibles manifest issues
        validateSupport manifest issues
        validateProducts manifest issues

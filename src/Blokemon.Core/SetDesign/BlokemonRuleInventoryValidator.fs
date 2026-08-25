namespace Blokemon.Core.SetDesign

open System
open System.Collections.Generic

module internal BlokemonRuleInventoryValidator =

    let private check = BlokemonValidation.check

    let private validateResolutionOrder<'Step when 'Step: equality>
        (name: string)
        (code: string)
        (order: 'Step array)
        (issues: ResizeArray<BlokemonValidationIssue>)
        =
        let expected = Enum.GetValues(typeof<'Step>) |> Seq.cast<'Step> |> Seq.toArray

        let actual =
            if obj.ReferenceEquals(order, null) then
                Array.empty
            else
                order

        let unknown =
            actual |> Array.filter (fun step -> not (Array.contains step expected))

        let unknownNames = String.Join(", ", unknown)

        check
            (unknown.Length = 0)
            $"runtime.{code}-unknown"
            $"{name} contains unknown values: {unknownNames}."
            issues

        let duplicates =
            actual
            |> Array.countBy id
            |> Array.filter (fun (_, count) -> count > 1)
            |> Array.map fst

        let duplicateNames = String.Join(", ", duplicates)

        check
            (duplicates.Length = 0)
            $"runtime.{code}-duplicate"
            $"{name} contains duplicate steps: {duplicateNames}."
            issues

        let missing =
            expected |> Array.filter (fun step -> not (Array.contains step actual))

        let missingNames = String.Join(", ", missing)

        check
            (missing.Length = 0)
            $"runtime.{code}-omission"
            $"{name} omits required steps: {missingNames}."
            issues

        if unknown.Length = 0 && duplicates.Length = 0 && missing.Length = 0 then
            check
                (actual = expected)
                $"runtime.{code}-unsupported-order"
                $"{name} changes the supported resolution order."
                issues

    let validate
        (manifest: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonValidationIssue>)
        =
        let rules = manifest.BaseRules

        let opcodes = Enum.GetValues<BlokemonOpcode>()

        check
            (rules.OpcodeInventory |> Array.distinct |> Array.length = opcodes.Length
             && Array.sort rules.OpcodeInventory = Array.sort opcodes)
            "runtime.opcode-inventory"
            "The runtime rules must list every finite opcode exactly once."
            issues

        validateResolutionOrder<BlokemonAttackResolutionStep>
            "AttackOrder"
            "attack-order"
            rules.AttackOrder
            issues

        validateResolutionOrder<BlokemonDamageResolutionStep>
            "DamageOrder"
            "damage-order"
            rules.DamageOrder
            issues

        let expectedBigHitters =
            [| "BLK-003"
               "BLK-006"
               "BLK-009"
               "BLK-024"
               "BLK-038"
               "BLK-065"
               "BLK-076"
               "BLK-115"
               "BLK-124"
               "BLK-145"
               "BLK-151" |]

        let listedBigHitters =
            if obj.ReferenceEquals(rules.BigHitters.BlokeIds, null) then
                Array.empty
            else
                rules.BigHitters.BlokeIds

        let unknownBigHitters =
            listedBigHitters
            |> Array.filter (fun id -> not (Array.contains id expectedBigHitters))

        let unknownBigHitterNames = String.Join(", ", unknownBigHitters)

        check
            (unknownBigHitters.Length = 0)
            "runtime.big-hitter-ids-unknown"
            $"BigHitters contains unsupported identities: {unknownBigHitterNames}."
            issues

        let duplicateBigHitters =
            listedBigHitters
            |> Array.countBy id
            |> Array.choose (fun (id, count) -> if count > 1 then Some id else None)

        let duplicateBigHitterNames = String.Join(", ", duplicateBigHitters)

        check
            (duplicateBigHitters.Length = 0)
            "runtime.big-hitter-ids-duplicate"
            $"BigHitters contains duplicate identities: {duplicateBigHitterNames}."
            issues

        let omittedBigHitters =
            expectedBigHitters
            |> Array.filter (fun id -> not (Array.contains id listedBigHitters))

        let omittedBigHitterNames = String.Join(", ", omittedBigHitters)

        check
            (omittedBigHitters.Length = 0)
            "runtime.big-hitter-ids-omission"
            $"BigHitters omits required identities: {omittedBigHitterNames}."
            issues

        if
            unknownBigHitters.Length = 0
            && duplicateBigHitters.Length = 0
            && omittedBigHitters.Length = 0
        then
            check
                (listedBigHitters = expectedBigHitters)
                "runtime.big-hitter-ids-unsupported-order"
                "BigHitters changes the supported canonical identity order."
                issues

        let expectedBigHitterSet = Set.ofArray expectedBigHitters

        check
            (manifest.Collectibles
             |> Array.forall (fun card ->
                 card.BarChitsWhenSentHome = if expectedBigHitterSet.Contains card.Id then
                                                 rules.SendHome.BigHitterBarChits
                                             else
                                                 rules.SendHome.NormalBarChits))
            "runtime.collectible-send-home-awards"
            "Every collectible award must agree with the independently fixed Big Hitter identities and SendHome awards."
            issues

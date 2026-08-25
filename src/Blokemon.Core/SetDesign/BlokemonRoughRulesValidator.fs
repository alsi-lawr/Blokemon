namespace Blokemon.Core.SetDesign

open System
open System.Collections.Generic

module internal BlokemonRoughRulesValidator =

    let private check = BlokemonValidation.check

    let validate (rules: BlokemonBaseRules) (issues: ResizeArray<BlokemonValidationIssue>) =
        let expectedCheckupOrder =
            [| BlokemonRoughState.DodgyPint
               BlokemonRoughState.Singed
               BlokemonRoughState.NoddedOff
               BlokemonRoughState.Legless |]

        let checkupOrder =
            if obj.ReferenceEquals(rules.Checkup.RoughStateOrder, null) then
                Array.empty
            else
                rules.Checkup.RoughStateOrder

        check
            (checkupOrder = expectedCheckupOrder)
            "runtime.checkup-order"
            "Checkup must resolve DodgyPint, Singed, NoddedOff and Legless in the published order."
            issues

        check
            rules.Checkup.OtherEffectsOutsideWholeBlock
            "runtime.checkup-other-effects-boundary"
            "Other effects resolve outside the whole rough-state checkup block."
            issues

        check
            rules.Checkup.CannotInterleave
            "runtime.checkup-no-interleave"
            "The rough-state checkup block cannot be interleaved."
            issues

        check
            rules.Checkup.SendHomeAfterBothChecks
            "runtime.checkup-send-home-order"
            "Send-home is checked after both rough-state checks."
            issues

        let expectedRoughStates = Enum.GetValues<BlokemonRoughState>()

        let roughStates =
            if obj.ReferenceEquals(rules.RoughStates, null) then
                Array.empty
            else
                rules.RoughStates

        let roughStateOrderIsValid =
            (roughStates |> Array.map _.State) = expectedRoughStates

        check
            roughStateOrderIsValid
            "runtime.rough-state-order"
            "RoughStates must define every rough state exactly once in canonical order."
            issues

        check
            (roughStates |> Array.forall _.OcheOnly)
            "runtime.rough-state-location"
            "Only the Oche Bloke can carry rough states."
            issues

        check
            (roughStates |> Array.forall (fun rule -> rule.CheckupDamageCounters >= 0))
            "runtime.rough-state-checkup-damage-range"
            "Rough-state checkup damage counters cannot be negative."
            issues

        check
            (roughStates
             |> Array.forall (fun rule -> not rule.BadgeSideRecovers || rule.CheckupBeerMat))
            "runtime.rough-state-badge-requires-beer-mat"
            "Badge-side recovery requires a checkup beer-mat toss."
            issues

        check
            (roughStates
             |> Array.forall (fun rule ->
                 not rule.RecoversAfterOwnersNextRound.HasValue
                 || rule.RecoversAfterOwnersNextRound.Value))
            "runtime.rough-state-round-recovery-value"
            "Round recovery is either enabled or absent; false is unsupported."
            issues

        check
            (roughStates
             |> Array.forall (fun rule ->
                 not rule.BeforeAttackBeerMat.HasValue || rule.BeforeAttackBeerMat.Value))
            "runtime.rough-state-before-attack-value"
            "Before-attack beer-mat resolution is either enabled or absent; false is unsupported."
            issues

        check
            (roughStates
             |> Array.forall (fun rule ->
                 rule.BeforeAttackBeerMat.GetValueOrDefault() = rule.BlankSideCancelsAndSelfDamageCounters.HasValue))
            "runtime.rough-state-before-attack-pair"
            "Before-attack beer-mat resolution requires a blank-side self-damage amount and vice versa."
            issues

        check
            (roughStates
             |> Array.forall (fun rule ->
                 not rule.BlankSideCancelsAndSelfDamageCounters.HasValue
                 || rule.BlankSideCancelsAndSelfDamageCounters.Value > 0))
            "runtime.rough-state-self-damage-range"
            "Blank-side self-damage counters must be positive when present."
            issues

        if roughStateOrderIsValid then
            let muddled = roughStates[int BlokemonRoughState.Muddled]

            check
                (muddled.CheckupDamageCounters = 0)
                "runtime.rough-state-muddled-checkup-damage"
                "Muddled does not resolve checkup damage."
                issues

            check
                (not muddled.CheckupBeerMat)
                "runtime.rough-state-muddled-checkup-beer-mat"
                "Muddled does not resolve a checkup beer-mat toss."
                issues

            check
                (not muddled.BadgeSideRecovers)
                "runtime.rough-state-muddled-badge-recovery"
                "Muddled has no checkup badge-side recovery."
                issues

            check
                (not muddled.RecoversAfterOwnersNextRound.HasValue)
                "runtime.rough-state-muddled-round-recovery"
                "Muddled does not recover after its owner's next round."
                issues

        let expectedRotated =
            [| BlokemonRoughState.NoddedOff
               BlokemonRoughState.Muddled
               BlokemonRoughState.Legless |]

        check
            (rules.RoughStateCoexistence.RotatedGroup = expectedRotated)
            "runtime.rough-state-rotated-group"
            "The rotated rough-state group must retain its supported canonical membership."
            issues

        let expectedMarkers = [| BlokemonRoughState.Singed; BlokemonRoughState.DodgyPint |]

        check
            (rules.RoughStateCoexistence.MarkerGroup = expectedMarkers)
            "runtime.rough-state-marker-group"
            "The marker rough-state group must retain its supported canonical membership."
            issues

        check
            rules.RoughStateCoexistence.MarkersCoexistWithEachOtherAndRotatedGroup
            "runtime.rough-state-marker-coexistence"
            "Rough-state markers coexist with each other and with the rotated state."
            issues

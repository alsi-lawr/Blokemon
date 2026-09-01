namespace Blokemon.Core.SetDesign

open System.Collections.Generic

module internal BlokemonRoughRulesValidator =

    let private check = BlokemonValidation.check

    let validate (rules: BlokemonBaseRules) (issues: ResizeArray<BlokemonValidationIssue>) =
        let expectedCheckupOrder =
            [| BlokemonRoughState.DodgyPint
               BlokemonRoughState.NoddedOff
               BlokemonRoughState.Legless |]

        check
            (rules.Checkup.RoughStateOrder = expectedCheckupOrder
             && rules.Checkup.OtherEffectsOutsideWholeBlock
             && rules.Checkup.CannotInterleave
             && rules.Checkup.SendHomeAfterBothChecks)
            "runtime.condition-checkup"
            "Between turns, Poison, Sleep, and Paralysis must resolve as one uninterrupted block before Knock Outs."
            issues

        let expectedStates =
            [| BlokemonRoughState.DodgyPint
               BlokemonRoughState.NoddedOff
               BlokemonRoughState.Legless
               BlokemonRoughState.Muddled |]

        check
            (rules.RoughStates |> Array.map _.State = expectedStates)
            "runtime.condition-inventory"
            "The active condition rules must contain exactly Poisoned, Asleep, Paralyzed, and Confused."
            issues

        let ruleFor state =
            rules.RoughStates |> Array.tryFind (fun rule -> rule.State = state)

        let matches state expected = ruleFor state = Some expected

        check
            (matches
                BlokemonRoughState.DodgyPint
                { State = BlokemonRoughState.DodgyPint
                  OcheOnly = true
                  CheckupDamageCounters = 1
                  CheckupBeerMat = false
                  BadgeSideRecovers = false
                  PreventsAttack = false
                  PreventsTaxi = false
                  RecoversAfterOwnersNextRound = System.Nullable()
                  BeforeAttackBeerMat = System.Nullable()
                  BlankSideCancelsAndSelfDamageCounters = System.Nullable()
                  BeforeTaxiBeerMat = System.Nullable()
                  BlankSideConsumesTaxi = System.Nullable() })
            "runtime.condition-poisoned"
            "Poisoned must place one damage counter after every turn and must not stack."
            issues

        check
            (matches
                BlokemonRoughState.NoddedOff
                { State = BlokemonRoughState.NoddedOff
                  OcheOnly = true
                  CheckupDamageCounters = 0
                  CheckupBeerMat = true
                  BadgeSideRecovers = true
                  PreventsAttack = true
                  PreventsTaxi = true
                  RecoversAfterOwnersNextRound = System.Nullable()
                  BeforeAttackBeerMat = System.Nullable()
                  BlankSideCancelsAndSelfDamageCounters = System.Nullable()
                  BeforeTaxiBeerMat = System.Nullable()
                  BlankSideConsumesTaxi = System.Nullable() })
            "runtime.condition-asleep"
            "Asleep must prevent attacking and retreating and recover on the between-turn coin flip."
            issues

        check
            (matches
                BlokemonRoughState.Legless
                { State = BlokemonRoughState.Legless
                  OcheOnly = true
                  CheckupDamageCounters = 0
                  CheckupBeerMat = false
                  BadgeSideRecovers = false
                  PreventsAttack = true
                  PreventsTaxi = true
                  RecoversAfterOwnersNextRound = System.Nullable true
                  BeforeAttackBeerMat = System.Nullable()
                  BlankSideCancelsAndSelfDamageCounters = System.Nullable()
                  BeforeTaxiBeerMat = System.Nullable()
                  BlankSideConsumesTaxi = System.Nullable() })
            "runtime.condition-paralyzed"
            "Paralyzed must prevent attacking and retreating until after the owner's next turn."
            issues

        check
            (matches
                BlokemonRoughState.Muddled
                { State = BlokemonRoughState.Muddled
                  OcheOnly = true
                  CheckupDamageCounters = 0
                  CheckupBeerMat = false
                  BadgeSideRecovers = false
                  PreventsAttack = false
                  PreventsTaxi = false
                  RecoversAfterOwnersNextRound = System.Nullable()
                  BeforeAttackBeerMat = System.Nullable true
                  BlankSideCancelsAndSelfDamageCounters = System.Nullable 2
                  BeforeTaxiBeerMat = System.Nullable true
                  BlankSideConsumesTaxi = System.Nullable true })
            "runtime.condition-confused"
            "Confused must gate attacks and retreats by a coin flip and deal two self-damage counters after a failed attack."
            issues

        let expectedRotated =
            [| BlokemonRoughState.NoddedOff
               BlokemonRoughState.Muddled
               BlokemonRoughState.Legless |]

        check
            (rules.RoughStateCoexistence.RotatedGroup = expectedRotated
             && rules.RoughStateCoexistence.LatestRotatedStateReplacesPrevious
             && rules.RoughStateCoexistence.MarkerGroup = [| BlokemonRoughState.DodgyPint |]
             && rules.RoughStateCoexistence.MarkersCoexistWithEachOtherAndRotatedGroup)
            "runtime.condition-coexistence"
            "Asleep, Confused, and Paralyzed replace one another while Poisoned coexists."
            issues

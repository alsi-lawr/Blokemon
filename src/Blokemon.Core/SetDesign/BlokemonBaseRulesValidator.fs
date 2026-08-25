namespace Blokemon.Core.SetDesign

open System
open System.Collections.Generic

module internal BlokemonBaseRulesValidator =

    let private check = BlokemonValidation.check

    let validate
        (manifest: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonValidationIssue>)
        =
        let rules = manifest.BaseRules

        check
            (StringComparer.Ordinal.Equals(
                rules.RulesVersion,
                "blokemon-base-rules-1.0.0-candidate.9"
            ))
            "runtime.rules-version"
            "RulesVersion is the supported load-time identity blokemon-base-rules-1.0.0-candidate.9."
            issues

        check
            (rules.Stack.CardCount > 0
             && rules.Stack.MechanicalCopyLimit > 0
             && rules.Stack.MechanicalCopyLimit <= rules.Stack.CardCount)
            "runtime.stack-range"
            "Stack card and mechanical-copy counts must be positive and internally possible."
            issues

        check
            rules.Stack.RequiresRegularBloke
            "runtime.stack-regular-required"
            "The repeating opening mulligan requires every Stack to contain a Regular Bloke."
            issues

        check
            (rules.Opening.MittSize > 0
             && rules.Opening.BoothLimit >= 0
             && rules.Opening.BarChitCount > 0)
            "runtime.opening-size-range"
            "Opening setup sizes must be positive where required and otherwise non-negative."
            issues

        check
            rules.Opening.OpeningParticipantSampledBeforeShuffle
            "runtime.opening-participant-sampling"
            "The opening participant must be sampled before either Stack is shuffled."
            issues

        check
            (rules.Opening.OcheRegularCount = 1)
            "runtime.opening-oche-count"
            "Opening setup supports exactly one Oche Regular."
            issues

        check
            (StringComparer.Ordinal.Equals(rules.Opening.Mulligans, "RepeatUntilRegular"))
            "runtime.opening-mulligan-mode"
            "Opening mulligans repeat until a Regular is available."
            issues

        check
            rules.Opening.BothMulliganNoBonus
            "runtime.opening-both-mulligan-bonus"
            "Neither side receives a bonus draw when both sides mulligan."
            issues

        check
            rules.Opening.OtherSideBonusPerExtraMulligan
            "runtime.opening-extra-mulligan-bonus"
            "The other side receives one bonus opportunity per excess mulligan."
            issues

        check
            rules.Opening.OtherSideBonusOptional
            "runtime.opening-mulligan-bonus-optional"
            "Excess-mulligan bonus draws are optional."
            issues

        check
            rules.Round.PartyTricksAreNotAttacks
            "runtime.round-party-tricks"
            "Party Tricks are not Attacks and cannot inherit Attack round-ending behavior."
            issues

        check
            (rules.Vim.NormalAttachmentPerRound >= 0)
            "runtime.vim-attachment-range"
            "The normal Vim attachment limit cannot be negative."
            issues

        check
            rules.Vim.CostNotChuckedUnlessSpecified
            "runtime.vim-cost-retention"
            "Vim paid for a cost stays attached unless an effect explicitly says to chuck it."
            issues

        check
            rules.Vim.LocalSatisfiedByAnyVim
            "runtime.vim-local-cost"
            "Any Vim satisfies a Local Vim symbol."
            issues

        check
            (StringComparer.Ordinal.Equals(rules.Kit.BarBitsPerRound, "Unlimited"))
            "runtime.kit-bar-bits-per-round"
            "Bar Bits use the supported Unlimited per-round contract."
            issues

        check
            (StringComparer.Ordinal.Equals(rules.Kit.BarKitsPerRound, "Unlimited"))
            "runtime.kit-bar-kits-per-round"
            "Bar Kits use the supported Unlimited per-round contract."
            issues

        check
            (rules.Kit.BarKitsPerBloke >= 0
             && rules.Kit.MatesPerRound >= 0
             && rules.Kit.LocalsPerRound >= 0)
            "runtime.kit-range"
            "Kit per-round and per-Bloke limits must be non-negative."
            issues

        check
            rules.Taxi.RequiresBooth
            "runtime.taxi-source"
            "Taxi requires an incoming Booth Bloke."
            issues

        check
            (rules.Taxi.PerRound >= 0)
            "runtime.taxi-range"
            "The per-round Taxi limit cannot be negative."
            issues

        check
            (not rules.Damage.BoothDamageUsesSoftSpotOrStubbornStreak)
            "runtime.damage-booth-modifiers"
            "Booth damage bypasses Soft Spots and Stubborn Streaks."
            issues

        check
            (not rules.Damage.PlacedCountersUseDamageModifiers)
            "runtime.damage-placed-counter-modifiers"
            "Placed damage counters do not enter damage calculation."
            issues

        check
            (StringComparer.Ordinal.Equals(
                rules.SelectionRules.UpToCount,
                "ChooseOneThroughCountExceptDrawMayChooseZero"
            ))
            "runtime.selection-up-to-count"
            "Up-to selection chooses one through the count except that draws may choose zero."
            issues

        check
            (StringComparer.Ordinal.Equals(rules.SelectionRules.AnyAmountOrNumber, "MayChooseZero"))
            "runtime.selection-any-amount"
            "Any-amount and any-number selection may choose zero."
            issues

        check
            (StringComparer.Ordinal.Equals(rules.SelectionRules.Optional, "MayDecline"))
            "runtime.selection-optional"
            "Optional selection may be declined."
            issues

        check
            (rules.EffectDrawFromShortStack = BlokemonEffectDrawFromShortStack.DrawAvailableCardsWithoutLosing)
            "runtime.effect-draw-short-stack"
            "Effect draws from short stacks take available cards without losing."
            issues

        check
            (rules.RequiredRoundDrawFromEmptyStack = BlokemonRequiredRoundDrawFromEmptyStack.LoseBout)
            "runtime.required-round-draw"
            "A failed required round draw loses the bout."
            issues

        BlokemonRoughRulesValidator.validate rules issues

        check
            rules.SendHome.DamageAtLeastStayingPower
            "runtime.send-home-threshold"
            "Damage equal to or above Staying Power sends a Bloke home."
            issues

        check
            rules.SendHome.ChuckBlokeAndAttachedCards
            "runtime.send-home-chuck-pile"
            "Sending a Bloke home chucks that Bloke and its attached cards."
            issues

        check
            rules.SendHome.OwnerPromotesFromBooth
            "runtime.send-home-replacement"
            "The owner replaces a sent-home Bloke from the Booth."
            issues

        check
            (rules.SendHome.NormalBarChits >= 0 && rules.SendHome.BigHitterBarChits >= 0)
            "runtime.send-home-award-range"
            "Send-home Bar Chit awards cannot be negative."
            issues

        let expectedWinConditions =
            [| "TakeLastBarChit"; "LeaveOtherSideNoBloke"; "OtherSideFailsRequiredDraw" |]

        check
            (rules.Win.Conditions = expectedWinConditions)
            "runtime.win-conditions"
            "Win conditions must retain the three supported terminal methods in canonical order."
            issues

        check
            (StringComparer.Ordinal.Equals(rules.Win.OneMethodEach, "SuddenDeath"))
            "runtime.win-one-method-each"
            "One terminal method for each side starts sudden death."
            issues

        check
            (StringComparer.Ordinal.Equals(rules.Win.MoreMethodsWins, "Immediate"))
            "runtime.win-more-methods"
            "A side with more terminal methods wins immediately."
            issues

        check
            (rules.Win.SuddenDeathBarChits > 0)
            "runtime.win-sudden-death-range"
            "Sudden death requires a positive Bar Chit count."
            issues

        check
            rules.Win.RepeatUntilWinner
            "runtime.win-repeat-sudden-death"
            "Tied sudden death repeats until there is one winner."
            issues

        check
            (rules.FossilKits.KitIds = [| "KIT-001"; "KIT-002"; "KIT-003" |])
            "runtime.fossil-kit-ids"
            "Fossil Kits must name KIT-001 through KIT-003 exactly once in canonical order."
            issues

        check
            (rules.FossilKits.PlayAsRegularLocalStayingPower > 0)
            "runtime.fossil-staying-power"
            "Fossil Kit staying power must be positive."
            issues

        check
            rules.FossilKits.CannotTaxi
            "runtime.fossil-taxi"
            "Fossil Kits have no Taxi fare and cannot use the Taxi command."
            issues

        BlokemonRuleInventoryValidator.validate manifest issues

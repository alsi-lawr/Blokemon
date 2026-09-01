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
                "wotc-advanced-rulebook-v1-1999-candidate.1"
            ))
            "runtime.rules-version"
            "RulesVersion must identify the pinned Wizards Advanced Rulebook Version 1 rules."
            issues

        check
            (rules.Stack.CardCount = 60
             && rules.Stack.MechanicalCopyLimit = 4
             && rules.Stack.BasicVimExempt
             && rules.Stack.RequiresRegularBloke)
            "runtime.stack-rules"
            "A Deck must contain exactly 60 cards, no more than four cards with the same name except Basic Energy, and at least one Basic Pokemon."
            issues

        let opening = rules.Opening

        check
            (opening.OpeningParticipantSampledBeforeShuffle
             && opening.MittSize = 7
             && opening.OcheRegularCount = 1
             && opening.BoothLimit = 5
             && opening.PrizeCardCount = 6
             && opening.OpeningParticipantMayAttack
             && StringComparer.Ordinal.Equals(opening.Mulligans, "RepeatUntilRegular")
             && opening.BothMulliganNoBonus
             && opening.OtherSideBonusPerExtraMulligan
             && opening.OtherSideBonusOptional)
            "runtime.opening-rules"
            "Setup must use a seven-card hand, one Active Basic, up to five Benched Basics, six Prizes, vintage mulligans, and permit the first player to attack."
            issues

        check
            (rules.Round.RequiredOpeningDraw
             && rules.Round.AttackEndsRound
             && rules.Round.PartyTricksAreNotAttacks)
            "runtime.turn-rules"
            "A turn must begin with a draw, an Attack must end it, and Pokemon Powers are not Attacks."
            issues

        check
            (rules.Promotion.ExactMechanicalEdgeRequired
             && rules.Promotion.NotOnEitherFirstRound
             && rules.Promotion.NotFirstRoundInPlay
             && rules.Promotion.NotTwiceInRound
             && rules.Promotion.RetainDamageAndAttachedCards
             && rules.Promotion.ClearRoughStatesAndAttackEffects)
            "runtime.evolution-rules"
            "Evolution must follow one printed stage, retain cards and damage, clear effects, and observe the first-turn and same-turn restrictions."
            issues

        check
            (rules.Vim.NormalAttachmentPerRound = 1
             && rules.Vim.CostNotChuckedUnlessSpecified
             && rules.Vim.ColorlessSatisfiedByAnyEnergy)
            "runtime.energy-rules"
            "One Energy card may be attached per turn, Energy stays attached unless discarded, and any Energy pays Colorless requirements."
            issues

        check
            (rules.Trainer.UnlimitedPerTurn && rules.Trainer.ResolveTextThenDiscard)
            "runtime.trainer-rules"
            "Trainer cards use the single vintage Trainer rule: resolve the printed text, then discard the card."
            issues

        check
            (rules.PokemonPower.NotAttacks
             && rules.PokemonPower.UsableFromBench
             && rules.PokemonPower.DisabledBy = [| BlokemonRoughState.NoddedOff
                                                   BlokemonRoughState.Muddled
                                                   BlokemonRoughState.Legless |])
            "runtime.pokemon-power-rules"
            "Pokemon Powers are not Attacks, work from the Bench, and are disabled by Asleep, Confused, or Paralyzed."
            issues

        check
            (rules.Taxi.PerRound = 1
             && rules.Taxi.ChuckVimPerFareSymbol
             && rules.Taxi.RequiresBooth
             && rules.Taxi.MovingToBoothClearsRoughStatesAndAttackEffects
             && rules.Taxi.AttachedCardsAndDamageRemain)
            "runtime.retreat-rules"
            "Retreat must discard enough Energy cards, switch with a Benched Pokemon, retain cards and damage, and clear effects."
            issues

        check
            (not rules.Damage.BoothDamageUsesSoftSpotOrStubbornStreak
             && not rules.Damage.PlacedCountersUseDamageModifiers)
            "runtime.damage-rules"
            "Bench damage and placed damage counters must bypass Weakness and Resistance."
            issues

        check
            (StringComparer.Ordinal.Equals(
                rules.SelectionRules.UpToCount,
                "ChooseOneThroughCountExceptDrawMayChooseZero"
             )
             && StringComparer.Ordinal.Equals(
                 rules.SelectionRules.AnyAmountOrNumber,
                 "MayChooseZero"
             )
             && StringComparer.Ordinal.Equals(rules.SelectionRules.Optional, "MayDecline"))
            "runtime.selection-rules"
            "Effect selections must retain the supported vintage partial-effect choices."
            issues

        check
            (rules.EffectDrawFromShortStack = BlokemonEffectDrawFromShortStack.DrawAvailableCardsWithoutLosing
             && rules.RequiredRoundDrawFromEmptyStack = BlokemonRequiredRoundDrawFromEmptyStack.LoseBout)
            "runtime.draw-rules"
            "Effect draws do as much as possible, but a failed required turn draw loses the game."
            issues

        BlokemonRoughRulesValidator.validate rules issues

        check
            (rules.SendHome.DamageAtLeastStayingPower
             && rules.SendHome.ChuckBlokeAndAttachedCards
             && rules.SendHome.PrizeCardsPerKnockout = 1
             && rules.SendHome.OwnerPromotesFromBooth)
            "runtime.knockout-rules"
            "A Knock Out discards the Pokemon and attached cards, awards one Prize, and requires Active replacement from the Bench."
            issues

        let expectedWinConditions =
            [| "TakeLastPrizeCard"
               "OpponentHasNoPokemonInPlay"
               "OpponentCannotDrawAtStartOfTurn" |]

        check
            (rules.Win.Conditions = expectedWinConditions
             && StringComparer.Ordinal.Equals(
                 rules.Win.SimultaneousWin,
                 "MoreWinConditionsWinsOtherwiseSuddenDeath"
             )
             && rules.Win.SuddenDeathPrizeCards = 1
             && rules.Win.SuddenDeathStartsFreshGame
             && rules.Win.RepeatUntilWinner)
            "runtime.win-rules"
            "The three vintage win conditions and fresh one-Prize Sudden Death must be fixed."
            issues

        BlokemonRuleInventoryValidator.validate manifest issues

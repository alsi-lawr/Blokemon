namespace Blokemon.Game.Tests

open System
open Blokemon.Core.SetDesign

module internal ConformanceCensus =

    type ProgramKind =
        | PartyTrick
        | Attack
        | HouseRule

    type ProgramRow =
        { OwnerId: string
          MechanicalId: string
          Kind: ProgramKind
          Trigger: BlokemonTrigger voption
          Program: BlokemonEffectInstruction array }

    type Totals =
        { ProgramBearingCards: int
          Programs: int
          RecursiveInstructions: int
          DeclaredAndUsedOpcodes: int
          DeclaredAndUsedConditions: int
          NonActivatedTriggers: int
          RecursiveNontrivialPrograms: int
          BigHitters: int }

    let rec instructions (program: BlokemonEffectInstruction array) =
        seq {
            for instruction in program do
                yield instruction
                yield! instructions instruction.Then
                yield! instructions instruction.Otherwise
        }

    let programRows =
        seq {
            let rows
                ownerId
                (partyTricks: BlokemonPartyTrick array)
                (attacks: BlokemonAttack array)
                (houseRules: BlokemonHouseRule array)
                =
                seq {
                    for trick in partyTricks do
                        yield
                            { OwnerId = ownerId
                              MechanicalId = trick.MechanicalId
                              Kind = PartyTrick
                              Trigger = ValueSome trick.Trigger
                              Program = trick.Program }

                    for attack in attacks do
                        yield
                            { OwnerId = ownerId
                              MechanicalId = attack.MechanicalId
                              Kind = Attack
                              Trigger = ValueNone
                              Program = attack.Program }

                    for rule in houseRules do
                        yield
                            { OwnerId = ownerId
                              MechanicalId = rule.MechanicalId
                              Kind = HouseRule
                              Trigger = ValueNone
                              Program = rule.Program }
                }

            for card in MatchScenario.Authority.Collectibles do
                yield! rows card.Id card.PartyTricks card.Attacks card.HouseRules

            for card in MatchScenario.Authority.Kits do
                yield! rows card.Id card.PartyTricks card.Attacks card.HouseRules
        }
        |> Seq.sortBy _.MechanicalId
        |> Seq.toArray

    let private programBearingCards =
        programRows |> Seq.map _.OwnerId |> Set.ofSeq |> Set.count

    let usedOpcodes =
        programRows
        |> Seq.collect (fun row -> instructions row.Program)
        |> Seq.map _.Opcode
        |> Set.ofSeq

    let usedConditions =
        programRows
        |> Seq.collect (fun row -> instructions row.Program)
        |> Seq.collect _.Predicates
        |> Seq.map _.Condition
        |> Set.ofSeq

    let nonActivatedTriggers =
        programRows
        |> Array.filter (fun row ->
            match row.Trigger with
            | ValueSome trigger -> trigger <> BlokemonTrigger.Activated
            | ValueNone -> false)

    let recursiveNontrivialPrograms =
        programRows
        |> Array.choose (fun row ->
            let count = instructions row.Program |> Seq.length
            if count > 1 then Some(row, count) else None)

    let totals =
        { ProgramBearingCards = programBearingCards
          Programs = programRows.Length
          RecursiveInstructions =
            programRows |> Seq.sumBy (fun row -> instructions row.Program |> Seq.length)
          DeclaredAndUsedOpcodes = usedOpcodes.Count
          DeclaredAndUsedConditions = usedConditions.Count
          NonActivatedTriggers = nonActivatedTriggers.Length
          RecursiveNontrivialPrograms = recursiveNontrivialPrograms.Length
          BigHitters = MatchScenario.Authority.BaseRules.BigHitters.BlokeIds.Length }

    let declaredOpcodes = Enum.GetValues<BlokemonOpcode>() |> Set.ofArray
    let declaredConditions = Enum.GetValues<BlokemonCondition>() |> Set.ofArray

    let opcodeEvidence =
        [ BlokemonOpcode.AdjustDamage,
          "AuthorityErrataTests.``hotbox should require the matchmaker for its additional damage``"
          BlokemonOpcode.ApplyRoughState,
          "AuthorityErrataTests.``ronnie pickering should leave itself muddled after attacking``"
          BlokemonOpcode.AttachVim,
          "CardFlowTests.``pintman should attach exactly one searched beer vim and shuffle the remaining stack``"
          BlokemonOpcode.BeerMatToss,
          "InterpreterBranchingTests.``a beer-mat conditional should take its badge and blank branches deterministically``"
          BlokemonOpcode.ChuckCards,
          "ExceptionalEffectTests.``chucking bar kit should use the chosen attached kit before it scales the attack damage``"
          BlokemonOpcode.ChuckSelf,
          "ExceptionalEffectTests.``a voluntary self-chuck should damage the chosen opponent without awarding bar chits``"
          BlokemonOpcode.ChuckVim,
          "ExceptionalEffectTests.``an attached-vim chuck should choose and discard the opposing oche's vim``"
          BlokemonOpcode.ClearRoughState,
          "OpcodeConformanceTests.``every long-tail opcode should have an observable MatchEngine result`` [BLK-079-B01]"
          BlokemonOpcode.Conditional,
          "ConditionConformanceTests.``every authority condition should select its true branch and reject its false branch``"
          BlokemonOpcode.ContinuousPartyTrick,
          "FullSetBehaviorTests.``every continuous party trick should refresh deterministically``"
          BlokemonOpcode.CopyAttack,
          "AuthorityParityTests.``a copied attack should request its own card choice``"
          BlokemonOpcode.DealBoothDamage,
          "ExceptionalEffectTests.``a spread attack should damage every opponent and switch with the chosen own booth``"
          BlokemonOpcode.DealPrintedDamage,
          "AuthorityErrataTests.``lullaby should apply nodded off through the match engine`` and ``do the wave should count only the attacker's boothed blokes``"
          BlokemonOpcode.DealSelfDamage,
          "InterpreterBranchingTests.``a beer-mat conditional should take its badge and blank branches deterministically``"
          BlokemonOpcode.Demote,
          "AuthorityParityTests.``a demotion should leave only the lower stage carrying the battle state``"
          BlokemonOpcode.DrawFromStack,
          "ExceptionalEffectTests.``a once-per-round local should use the active player's mitt and be answered by the policy``"
          BlokemonOpcode.EndRoundEffect,
          "AuthorityParityTests.``delayed counters should follow the original defender to the booth``"
          BlokemonOpcode.ForceBeerMatBlank,
          "AuthorityErrataTests.``day two should force the opponent's beer mat to the blank side``"
          BlokemonOpcode.HealDamage,
          "ActivatedEffectLegalityTests.``a heal should be offered at the Oche while a team-mate is damaged and should heal when it is taken``"
          BlokemonOpcode.IgnoreSoftSpotAndStubbornStreak,
          "OpcodeConformanceTests.``every long-tail opcode should have an observable MatchEngine result`` [BLK-120-B01]"
          BlokemonOpcode.IgnoreStubbornStreak,
          "OpcodeConformanceTests.``every long-tail opcode should have an observable MatchEngine result`` [BLK-076-B02]"
          BlokemonOpcode.ModifyAttackCost,
          "AuthorityErrataTests.``a pending waste licence should raise both the attack cost and the taxi fare``"
          BlokemonOpcode.ModifySoftSpot,
          "DamageAndRoughStateTests.``protective goggles should remove a regular defender's soft spot`` and ``protective goggles should leave a seasoned defender's soft spot intact``"
          BlokemonOpcode.ModifyTaxiFare,
          "ModifierDurationTests.``a continuous global taxi modifier should be recomputed from its declarative source``"
          BlokemonOpcode.MoveCards,
          "CardSemanticsTests.``a stack move should take each bloke together with the cards attached to it``"
          BlokemonOpcode.MoveVim,
          "TriggerTimingTests.``a knockout vim move should wait for its own owner before the knockout completes``"
          BlokemonOpcode.OncePerRound,
          "ExceptionalEffectTests.``a once-per-round local should use the active player's mitt and be answered by the policy``"
          BlokemonOpcode.PlaceDamageCounters,
          "TriggerTimingTests.``a damaged-active trigger should place its counters on the attacker before knockouts``"
          BlokemonOpcode.PlayAsBloke,
          "CardSemanticsTests.``playing a fossil kit should be ungated and should put the kit into the booth``"
          BlokemonOpcode.PreventDamage,
          "AuthorityErrataTests.``a protection attack should block the reply's damage and its effects`` [damage]"
          BlokemonOpcode.PreventEffects,
          "AuthorityErrataTests.``a protection attack should block the reply's damage and its effects`` [rough state]"
          BlokemonOpcode.RecoverFromSendHome,
          "TriggerTimingTests.``a would-be-knocked-out trigger should resolve before the knockout``"
          BlokemonOpcode.ReduceDamage,
          "DamageAndRoughStateTests.``attack damage should apply the soft spot before the defender's own reduction``"
          BlokemonOpcode.ReflectAttackDamage,
          "AuthorityErrataTests.``mirror the mood should reflect attack damage even when it is knocked out``"
          BlokemonOpcode.RepeatUntilBlankSide,
          "OpcodeConformanceTests.``every long-tail opcode should have an observable MatchEngine result`` [BLK-073-B02]"
          BlokemonOpcode.RestrictAttack,
          "AuthorityErrataTests.``a one-year chip should defer both required beer mats until the opponent attacks``"
          BlokemonOpcode.RestrictEmptiesRecovery,
          "AuthorityParityTests.``a discard-recovery lock should block an opposing item from returning a kit to the stack``"
          BlokemonOpcode.RestrictKit,
          "AuthorityParityTests.``an item lock should not block an otherwise legal supporter``"
          BlokemonOpcode.RestrictLocal,
          "OpcodeConformanceTests.``every long-tail opcode should have an observable MatchEngine result`` [KIT-002-T01]"
          BlokemonOpcode.RestrictTaxi,
          "OpcodeConformanceTests.``every long-tail opcode should have an observable MatchEngine result`` [BLK-024-B01]"
          BlokemonOpcode.RevealCards,
          "CardSemanticsTests.``revealing cards should emit one generic reveal and leave the bar chits where they were``"
          BlokemonOpcode.ScaleDamage,
          "ModifierDurationTests.``a next-round damage modifier should apply only to its own source on its owner's next round``"
          BlokemonOpcode.SearchStack,
          "CardFlowTests.``a stack search should offer only regular blokes and move the chosen card to the booth``"
          BlokemonOpcode.SendHome,
          "KnockoutResolutionTests.``a retaliating defender should take the attacker home in the same resolution``"
          BlokemonOpcode.ShuffleStack,
          "CardFlowTests.``get the lads in with booth room should move one regular and shuffle the remaining stack``"
          BlokemonOpcode.SwapOche,
          "ExceptionalEffectTests.``a switch a card effect forces should say which cards traded places``"
          BlokemonOpcode.TakeExtraBarChit,
          "FullSetBehaviorTests.``a knockout bonus should be taken on the damage that lands after the soft spot``"
          BlokemonOpcode.TransformFromStack,
          "ExceptionalEffectTests.``a first-round transform should replace its source and discard what was attached to it``"
          BlokemonOpcode.TriggeredPartyTrick,
          "STRUCTURAL EXCLUSION: AuthorityAudit validates the marker and TriggerConformanceTests proves the owning non-Activated trigger; MatchEngine dispatches from BlokemonPartyTrick.Trigger, so no other opcode state change is credited." ]
        |> Map.ofList

    let conditionEvidence (condition: BlokemonCondition) =
        match condition with
        | BlokemonCondition.OwnBlokeSentHomeByOtherAttackDamage ->
            "TriggerTimingTests.``a knockout vim move should wait for its own owner before the knockout completes`` [attack knockout]; AuthorityParityTests.``a recoil knockout should not trigger the send-home reaction`` [outer trigger non-invocation]"
        | BlokemonCondition.MatePlayedThisRound ->
            "CardSemanticsTests.``a named-mate condition should match only the mate it names`` [KIT-010 true; KIT-009 false]"
        | BlokemonCondition.OwnersFirstRound ->
            "ActivatedEffectLegalityTests.``a trick gated on its owner's first round should not be offered after it`` [round one true; round two false]"
        | BlokemonCondition.SelfIsAtOche ->
            "ActivatedEffectLegalityTests.``a heal should be offered at the Oche while a team-mate is damaged and should heal when it is taken`` [true]; ``a heal that only works from the Oche should not be offered from the bench although a team-mate is damaged`` [false]"
        | BlokemonCondition.TargetIsRegular ->
            "DamageAndRoughStateTests.``protective goggles should remove a regular defender's soft spot`` [true]; ``protective goggles should leave a seasoned defender's soft spot intact`` [false]"
        | other ->
            $"ConditionConformanceTests.``every authority condition should select its true branch and reject its false branch`` [{other}]"

    let triggerEvidence (row: ProgramRow) =
        $"TriggerConformanceTests.``every non-activated authority trigger should fire only in its declared context`` [{row.MechanicalId}]"

    let executionEvidence (row: ProgramRow) =
        $"ProgramCompositionConformanceTests.``every recursive nontrivial program should preserve its MatchEngine semantic composition`` [program={row.MechanicalId}]"

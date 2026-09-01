namespace Blokemon.Game

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign

/// Proving the printed authority only uses shapes this interpreter can actually run.
module internal AuthorityAudit =

    let rec private programContainsOpcode
        (program: BlokemonEffectInstruction array)
        (opcode: BlokemonOpcode)
        =
        program
        |> Array.exists (fun instruction ->
            instruction.Opcode = opcode
            || programContainsOpcode instruction.Then opcode
            || programContainsOpcode instruction.Otherwise opcode)

    let rec private programContainsCondition
        (program: BlokemonEffectInstruction array)
        (condition: BlokemonCondition)
        =
        program
        |> Array.exists (fun instruction ->
            (instruction.Predicates
             |> Array.exists (fun predicate -> predicate.Condition = condition))
            || programContainsCondition instruction.Then condition
            || programContainsCondition instruction.Otherwise condition)

    let private hasSupportedSemanticShape (instruction: BlokemonEffectInstruction) =
        if instruction.TargetCount < 1 || instruction.Amount < -99 then
            false
        elif
            instruction.Selection = BlokemonSelection.OtherSideChosen
            && instruction.Opcode <> BlokemonOpcode.SwapOche
            && instruction.Opcode <> BlokemonOpcode.ChuckCards
        then
            false
        elif
            instruction.Opcode = BlokemonOpcode.MoveCards
            && instruction.Destination = BlokemonEffectDestination.Unspecified
        then
            false
        elif
            (match instruction.CardFilter with
             | null -> false
             | filter ->
                 (filter.Categories |> Array.exists (fun value -> not (Enum.IsDefined value)))
                 || (filter.Ranks |> Array.exists (fun value -> not (Enum.IsDefined value))))
        then
            false
        elif
            instruction.Selection = BlokemonSelection.AnyDistribution
            && instruction.Opcode <> BlokemonOpcode.AttachVim
            && instruction.Opcode <> BlokemonOpcode.PlaceDamageCounters
            && instruction.Opcode <> BlokemonOpcode.TriggeredPartyTrick
        then
            false
        elif
            instruction.Selection = BlokemonSelection.Top
            && instruction.Opcode <> BlokemonOpcode.SearchStack
            && instruction.Opcode <> BlokemonOpcode.ShuffleStack
            && instruction.Opcode <> BlokemonOpcode.RevealCards
            && instruction.Opcode <> BlokemonOpcode.MoveCards
            && instruction.Opcode <> BlokemonOpcode.ChuckCards
        then
            false
        else
            instruction.Opcode <> BlokemonOpcode.Conditional
            || instruction.Predicates.Length > 0

    let rec private auditProgram
        (effect: EffectId)
        (program: BlokemonEffectInstruction array)
        (issues: ResizeArray<InterpreterAuditIssue>)
        =
        let mutable count = 0

        for instruction in program do
            count <- count + 1

            let add code =
                issues.Add
                    { Code = code
                      Effect = ValueSome effect }

            if not (Enum.IsDefined instruction.Opcode) then
                add "unknown-opcode"

            if not (Enum.IsDefined instruction.Selection) then
                add "unknown-selection"

            if not (Enum.IsDefined instruction.ValueSource) then
                add "unknown-value-source"

            if instruction.Targets |> Array.exists (fun target -> not (Enum.IsDefined target)) then
                add "unknown-target"

            if
                instruction.Predicates
                |> Array.exists (fun predicate -> not (Enum.IsDefined predicate.Condition))
            then
                add "unknown-condition"

            if not (hasSupportedSemanticShape instruction) then
                add "unsupported-semantic-shape"

            count <- count + auditProgram effect instruction.Then issues
            count <- count + auditProgram effect instruction.Otherwise issues

        count

    let private auditTrigger
        (trick: BlokemonPartyTrick)
        (issues: ResizeArray<InterpreterAuditIssue>)
        =
        let effect = EffectId trick.MechanicalId

        let continuous =
            programContainsOpcode trick.Program BlokemonOpcode.ContinuousPartyTrick

        let triggered =
            programContainsOpcode trick.Program BlokemonOpcode.TriggeredPartyTrick

        let valid =
            match trick.Trigger with
            | BlokemonTrigger.Activated -> not continuous && not triggered
            | BlokemonTrigger.Continuous -> continuous
            | BlokemonTrigger.OnPromotionFromMitt ->
                triggered
                && programContainsCondition
                    trick.Program
                    BlokemonCondition.PromotedFromMittThisRound
            | BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage ->
                triggered && programContainsCondition trick.Program BlokemonCondition.Optional
            | BlokemonTrigger.BeforeSelfSentHomeByAttackDamage
            | BlokemonTrigger.AfterSelfDamagedByAttack
            | BlokemonTrigger.AfterSelfSentHomeByAttackDamage -> triggered
            | BlokemonTrigger.OnBarChitTaken ->
                triggered && programContainsCondition trick.Program BlokemonCondition.Optional
            | _ -> false

        if not valid then
            issues.Add
                { Code = "unsupported-trigger-shape"
                  Effect = ValueSome effect }

    let private allPrograms (catalog: AuthorityCatalog) =
        let effectsOf
            (partyTricks: BlokemonPartyTrick array)
            (attacks: BlokemonAttack array)
            (houseRules: BlokemonHouseRule array)
            =
            Seq.concat
                [ partyTricks
                  |> Seq.map (fun effect -> EffectId effect.MechanicalId, effect.Program)
                  attacks |> Seq.map (fun effect -> EffectId effect.MechanicalId, effect.Program)
                  houseRules
                  |> Seq.map (fun effect -> EffectId effect.MechanicalId, effect.Program) ]

        Seq.append
            (catalog.Manifest.Collectibles
             |> Seq.collect (fun card -> effectsOf card.PartyTricks card.Attacks card.HouseRules))
            (catalog.Manifest.Kits
             |> Seq.collect (fun card -> effectsOf card.PartyTricks card.Attacks card.HouseRules))

    let auditAuthority (catalog: AuthorityCatalog) =
        let issues = ResizeArray<InterpreterAuditIssue>()
        let mutable effectCount = 0
        let mutable instructionCount = 0

        let declared =
            System.Collections.Generic.HashSet catalog.Manifest.BaseRules.OpcodeInventory

        for opcode in Enum.GetValues<BlokemonOpcode>() do
            if not (declared.Contains opcode) then
                issues.Add
                    { Code = "opcode-not-declared"
                      Effect = ValueNone }

        for declaredOpcode in declared do
            if not (Enum.IsDefined declaredOpcode) then
                issues.Add
                    { Code = "unknown-declared-opcode"
                      Effect = ValueNone }

        for effect, program in allPrograms catalog do
            effectCount <- effectCount + 1
            instructionCount <- instructionCount + auditProgram effect program issues

        for card in catalog.Manifest.Collectibles do
            for trick in card.PartyTricks do
                auditTrigger trick issues

        for trick in catalog.Manifest.Kits |> Seq.collect (fun card -> card.PartyTricks) do
            auditTrigger trick issues

        { EffectCount = effectCount
          InstructionCount = instructionCount
          Issues = ImmutableArray.CreateRange issues }

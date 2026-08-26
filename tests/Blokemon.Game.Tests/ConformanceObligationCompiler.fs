namespace Blokemon.Game.Tests

open System
open Blokemon.Core.SetDesign
open ConformanceCensus

type internal ConformanceProgramDisposition =
    | Executable
    | StructuralNonExecutable

type internal ConformanceObligationKind =
    | InstructionOccurrence
    | PredicateRecord
    | BranchEdge
    | SemanticDimension

type internal ConformanceMultiplicity = | ExactlyOnce

type internal ConformanceRequiredObligation =
    { Key: string
      ProgramKey: string
      Kind: ConformanceObligationKind
      Multiplicity: ConformanceMultiplicity }

type internal ConformanceMutationOperation =
    | ReplaceString of string
    | IncrementInteger
    | DecrementInteger
    | ToggleBoolean
    | RemoveBranch

type internal ConformanceMutationTarget =
    { Pointer: string
      ProgramKey: string
      NamedObligation: string
      Operation: ConformanceMutationOperation }

type internal ConformanceNonMutableOperand =
    { Pointer: string
      ProgramKey: string
      NamedItem: string
      Rationale: string }

type internal ConformanceCompiledProgram =
    { Row: ProgramRow
      ProgramKey: string
      Disposition: ConformanceProgramDisposition
      Required: ConformanceRequiredObligation array
      Mutations: ConformanceMutationTarget array
      NonMutableOperands: ConformanceNonMutableOperand array }

type internal ConformanceCompilation =
    { Programs: ConformanceCompiledProgram array
      Required: ConformanceRequiredObligation array
      Mutations: ConformanceMutationTarget array
      NonMutableOperands: ConformanceNonMutableOperand array }

module internal ConformanceObligationCompiler =

    [<Literal>]
    let StructuralRationale =
        "This declarative house-rule program is filtered from both kit-play execution and continuous refresh, so this occurrence, operand, or branch edge has no public MatchEngine execution route."

    [<Literal>]
    let IgnoredTypedFieldRationale =
        "The public interpreter route for this opcode does not read this typed fixture field, so changing it cannot express a semantic mutation."

    [<Literal>]
    let ContinuousAttachSkippedRationale =
        "The public interpreter explicitly skips AttachVim instructions while refreshing a Continuous party trick, so changing this typed operand cannot affect public behaviour."

    [<Literal>]
    let UnboundedAttachedBarKitAmountRationale =
        "The published six-Blokemon play-area limit and one-Bar-Kit-per-Blokemon limit bound eligible attached Bar Kits below both 99 and 98, so this sentinel amount cannot be distinguished by a public MatchEngine route."

    [<Literal>]
    let AttackSelfOwnOcheSourceRationale =
        "An Attack's source card is necessarily the attacker's Own Oche card at this instruction, so replacing source Self with OwnOche cannot be distinguished through a public Attack route."

    [<Literal>]
    let KnockoutOptionalProtocolRationale =
        "The published OnOwnBlokeSentHomeByOtherAttackDamage trigger is a may effect, and public authority validation requires its Optional predicate as fixed trigger-protocol metadata."

    [<Literal>]
    let BarChitOptionalProtocolRationale =
        "The official Lucky Bonus rule makes placing the face-down Prize onto the Bench optional, and public OnBarChitTaken validation requires its Optional predicate as fixed trigger-protocol metadata."

    let private programKindSegment =
        function
        | ProgramKind.PartyTrick -> "party-trick"
        | ProgramKind.Attack -> "attack"
        | ProgramKind.HouseRule -> "house-rule"

    let private programKey (row: ProgramRow) =
        $"{row.OwnerId}/{programKindSegment row.Kind}/{row.MechanicalId}"

    let private disposition (row: ProgramRow) =
        if declarativeKitStructuralProgramIds.Contains row.MechanicalId then
            StructuralNonExecutable
        else
            Executable

    let private enumReplacement (current: obj) =
        Enum.GetValues(current.GetType())
        |> Seq.cast<obj>
        |> Seq.find (fun candidate -> not (candidate.Equals current))
        |> string

    let private stringReplacement (value: string) = value + "-mutation"

    let private excludedIdReplacement value =
        if value = "BLK-001" then "BLK-003" else "BLK-001"

    let private selectionReplacement selection =
        match selection with
        | BlokemonSelection.Chosen
        | BlokemonSelection.UpTo
        | BlokemonSelection.BeerMat
        | BlokemonSelection.UntilBlankSide -> BlokemonSelection.All
        | _ -> BlokemonSelection.UpTo

    let private targetReplacement (instruction: BlokemonEffectInstruction) target =
        match target with
        | BlokemonTarget.Self when instruction.RelatedIds.Length > 0 -> BlokemonTarget.OwnBlokesAll
        | BlokemonTarget.Self
        | BlokemonTarget.OwnOche -> BlokemonTarget.OtherOche
        | BlokemonTarget.OwnBoothChosen -> BlokemonTarget.OtherBoothChosen
        | BlokemonTarget.OwnBlokeChosen -> BlokemonTarget.OtherBlokeChosen
        | BlokemonTarget.OwnBlokesAll -> BlokemonTarget.OtherBlokesAll
        | BlokemonTarget.OwnMitt -> BlokemonTarget.OtherMitt
        | BlokemonTarget.OwnStack -> BlokemonTarget.OtherStack
        | BlokemonTarget.OwnEmptiesTray -> BlokemonTarget.OtherEmptiesTray
        | BlokemonTarget.OwnOcheAttachedVim -> BlokemonTarget.OtherOcheAttachedVim
        | BlokemonTarget.OtherOche -> BlokemonTarget.Self
        | BlokemonTarget.OtherBoothChosen -> BlokemonTarget.OwnBoothChosen
        | BlokemonTarget.OtherBlokeChosen -> BlokemonTarget.OwnBlokeChosen
        | BlokemonTarget.OtherBlokesAll -> BlokemonTarget.OwnBlokesAll
        | BlokemonTarget.OtherMitt -> BlokemonTarget.OwnMitt
        | BlokemonTarget.OtherStack -> BlokemonTarget.OwnStack
        | BlokemonTarget.OtherEmptiesTray -> BlokemonTarget.OwnEmptiesTray
        | BlokemonTarget.OtherOcheAttachedVim -> BlokemonTarget.OwnOcheAttachedVim
        | BlokemonTarget.AttackingBloke -> BlokemonTarget.Self
        | _ -> BlokemonTarget.OtherOche

    let private conditionReplacement hasOptionalSibling condition =
        match condition with
        | BlokemonCondition.Optional -> BlokemonCondition.BoothHasSpace
        | _ when hasOptionalSibling -> BlokemonCondition.FirstBeerMatIsBlankSide
        | _ -> BlokemonCondition.Optional

    let private mechanicalTypesAreSemantic (instruction: BlokemonEffectInstruction) =
        match instruction.Opcode with
        | BlokemonOpcode.MoveCards
        | BlokemonOpcode.ChuckCards
        | BlokemonOpcode.SearchStack
        | BlokemonOpcode.AttachVim
        | BlokemonOpcode.MoveVim
        | BlokemonOpcode.ChuckVim
        | BlokemonOpcode.ModifyAttackCost
        | BlokemonOpcode.ModifySoftSpot -> true
        | BlokemonOpcode.AdjustDamage ->
            instruction.ValueSource = BlokemonValueSource.OwnAttachedVim
        | _ -> false

    let private valueSourceIsSemantic (instruction: BlokemonEffectInstruction) =
        instruction.Opcode = BlokemonOpcode.AdjustDamage
        || instruction.Opcode = BlokemonOpcode.ScaleDamage
        || (instruction.Opcode = BlokemonOpcode.DrawFromStack
            && instruction.Selection <> BlokemonSelection.UntilBlankSide)

    let private valueSourceReplacement (instruction: BlokemonEffectInstruction) =
        if instruction.Opcode = BlokemonOpcode.DrawFromStack then
            if instruction.ValueSource = BlokemonValueSource.MittCardsNeeded then
                string BlokemonValueSource.Fixed
            else
                string BlokemonValueSource.MittCardsNeeded
        else
            enumReplacement instruction.ValueSource

    let private scaleDamageMarker (instruction: BlokemonEffectInstruction) =
        instruction.Opcode = BlokemonOpcode.ScaleDamage
        && instruction.ValueSource = BlokemonValueSource.Fixed
        && (instruction.Selection = BlokemonSelection.BeerMat
            || instruction.Selection = BlokemonSelection.UntilBlankSide)

    let private amountIsSemantic (instruction: BlokemonEffectInstruction) =
        if scaleDamageMarker instruction then
            false
        else
            match instruction.Opcode with
            | BlokemonOpcode.SearchStack ->
                instruction.Selection = BlokemonSelection.All
                || instruction.Selection = BlokemonSelection.Top
                || instruction.Selection = BlokemonSelection.UpTo
            | BlokemonOpcode.Conditional
            | BlokemonOpcode.RepeatUntilBlankSide
            | BlokemonOpcode.ApplyRoughState
            | BlokemonOpcode.ClearRoughState
            | BlokemonOpcode.RevealCards
            | BlokemonOpcode.ShuffleStack
            | BlokemonOpcode.SwapOche
            | BlokemonOpcode.PreventDamage
            | BlokemonOpcode.PreventEffects
            | BlokemonOpcode.IgnoreStubbornStreak
            | BlokemonOpcode.IgnoreSoftSpotAndStubbornStreak
            | BlokemonOpcode.RestrictAttack
            | BlokemonOpcode.RestrictTaxi
            | BlokemonOpcode.RestrictKit
            | BlokemonOpcode.RestrictLocal
            | BlokemonOpcode.RestrictEmptiesRecovery
            | BlokemonOpcode.ForceBeerMatBlank
            | BlokemonOpcode.ReflectAttackDamage
            | BlokemonOpcode.CopyAttack
            | BlokemonOpcode.Demote
            | BlokemonOpcode.TransformFromStack
            | BlokemonOpcode.SendHome
            | BlokemonOpcode.PlayAsBloke
            | BlokemonOpcode.ChuckSelf
            | BlokemonOpcode.ContinuousPartyTrick
            | BlokemonOpcode.OncePerRound
            | BlokemonOpcode.EndRoundEffect
            | BlokemonOpcode.TriggeredPartyTrick -> false
            | _ -> true

    let private selectionIsSemantic (instruction: BlokemonEffectInstruction) =
        if instruction.Opcode = BlokemonOpcode.RestrictAttack then
            instruction.Selection = BlokemonSelection.BeerMat
        elif
            ((instruction.Selection = BlokemonSelection.BeerMat
              && instruction.Opcode <> BlokemonOpcode.BeerMatToss
              && instruction.Opcode <> BlokemonOpcode.ScaleDamage
              && instruction.Opcode <> BlokemonOpcode.RestrictAttack)
             || (instruction.Selection = BlokemonSelection.UntilBlankSide
                 && instruction.Opcode <> BlokemonOpcode.ScaleDamage
                 && instruction.Opcode <> BlokemonOpcode.DrawFromStack))
        then
            false
        else
            match instruction.Opcode with
            | BlokemonOpcode.Conditional
            | BlokemonOpcode.BeerMatToss
            | BlokemonOpcode.RepeatUntilBlankSide
            | BlokemonOpcode.AdjustDamage
            | BlokemonOpcode.DealSelfDamage
            | BlokemonOpcode.ShuffleStack
            | BlokemonOpcode.PreventDamage
            | BlokemonOpcode.PreventEffects
            | BlokemonOpcode.ReduceDamage
            | BlokemonOpcode.ModifyAttackCost
            | BlokemonOpcode.ModifyTaxiFare
            | BlokemonOpcode.RestrictTaxi
            | BlokemonOpcode.RestrictKit
            | BlokemonOpcode.RestrictLocal
            | BlokemonOpcode.RestrictEmptiesRecovery
            | BlokemonOpcode.ReflectAttackDamage
            | BlokemonOpcode.IgnoreStubbornStreak
            | BlokemonOpcode.IgnoreSoftSpotAndStubbornStreak
            | BlokemonOpcode.ForceBeerMatBlank
            | BlokemonOpcode.RecoverFromSendHome
            | BlokemonOpcode.CopyAttack
            | BlokemonOpcode.TakeExtraBarChit
            | BlokemonOpcode.PlayAsBloke
            | BlokemonOpcode.ChuckSelf
            | BlokemonOpcode.TriggeredPartyTrick
            | BlokemonOpcode.OncePerRound
            | BlokemonOpcode.ContinuousPartyTrick
            | BlokemonOpcode.EndRoundEffect -> false
            | BlokemonOpcode.DrawFromStack ->
                instruction.Selection = BlokemonSelection.UntilBlankSide
            | _ -> true

    let private targetsAreSemantic (instruction: BlokemonEffectInstruction) =
        let hasSources =
            match instruction.Sources with
            | null -> false
            | sources -> sources.Length > 0

        if scaleDamageMarker instruction then
            false
        elif hasSources then
            instruction.Opcode = BlokemonOpcode.AttachVim
        else
            match instruction.Opcode with
            | BlokemonOpcode.AdjustDamage
            | BlokemonOpcode.DealSelfDamage
            | BlokemonOpcode.DrawFromStack
            | BlokemonOpcode.BeerMatToss
            | BlokemonOpcode.RepeatUntilBlankSide
            | BlokemonOpcode.Conditional
            | BlokemonOpcode.IgnoreStubbornStreak
            | BlokemonOpcode.IgnoreSoftSpotAndStubbornStreak
            | BlokemonOpcode.ForceBeerMatBlank
            | BlokemonOpcode.RecoverFromSendHome
            | BlokemonOpcode.CopyAttack
            | BlokemonOpcode.TakeExtraBarChit
            | BlokemonOpcode.PlayAsBloke
            | BlokemonOpcode.ChuckSelf
            | BlokemonOpcode.TriggeredPartyTrick
            | BlokemonOpcode.OncePerRound -> false
            | _ -> true

    let private amountMutation amount =
        if amount > 0 then DecrementInteger else IncrementInteger

    let private isUnboundedAttachedBarKitChuck (instruction: BlokemonEffectInstruction) =
        instruction.Opcode = BlokemonOpcode.ChuckCards
        && instruction.Selection = BlokemonSelection.UpTo
        && instruction.Amount = 99
        && (instruction.Targets |> Array.contains BlokemonTarget.OwnAttachedBarKits)

    let private requiredRow program kind key =
        { Key = key
          ProgramKey = program
          Kind = kind
          Multiplicity = ExactlyOnce }

    let private instructionKey program path = $"{program}/{path}"
    let private dimensionKey occurrence dimension = $"{occurrence}::{dimension}"

    let private addRequired (rows: ResizeArray<ConformanceRequiredObligation>) program kind key =
        rows.Add(requiredRow program kind key)

    let private addOperand
        (requiredRows: ResizeArray<ConformanceRequiredObligation>)
        (mutations: ResizeArray<ConformanceMutationTarget>)
        program
        occurrence
        dimension
        pointer
        operation
        =
        let key = dimensionKey occurrence dimension
        addRequired requiredRows program SemanticDimension key

        mutations.Add
            { Pointer = pointer
              ProgramKey = program
              NamedObligation = key
              Operation = operation }

    let private addDimension
        (requiredRows: ResizeArray<ConformanceRequiredObligation>)
        program
        occurrence
        dimension
        =
        addRequired requiredRows program SemanticDimension (dimensionKey occurrence dimension)

    let private addStructuralOperand
        (rows: ResizeArray<ConformanceNonMutableOperand>)
        program
        namedItem
        pointer
        =
        rows.Add
            { Pointer = pointer
              ProgramKey = program
              NamedItem = namedItem
              Rationale = StructuralRationale }

    let private addMappedOperand
        disposition
        requiredRows
        mutations
        nonMutable
        program
        occurrence
        dimension
        pointer
        operation
        =
        match disposition with
        | Executable ->
            addOperand requiredRows mutations program occurrence dimension pointer operation
        | StructuralNonExecutable ->
            addStructuralOperand nonMutable program (dimensionKey occurrence dimension) pointer

    let private selectionDimensions
        requiredRows
        program
        occurrence
        (instruction: BlokemonEffectInstruction)
        =
        let dimension name =
            addDimension requiredRows program occurrence $"selection/{name}"

        let countLabel count =
            match count with
            | 0 -> "zero"
            | 1 -> "one"
            | 2 -> "two"
            | value -> string value

        match instruction.Selection with
        | BlokemonSelection.Chosen ->
            let count = countLabel instruction.TargetCount
            dimension $"minimum/{count}"

            if instruction.TargetCount > 1 then
                dimension $"maximum/{count}"
        | BlokemonSelection.SeededRandom ->
            dimension "cardinality/one"
            dimension "random/first-candidate"
            dimension "random/last-candidate"
        | BlokemonSelection.OtherSideChosen ->
            dimension $"other-side/{countLabel instruction.TargetCount}"
        | BlokemonSelection.AnyDistribution when instruction.Opcode = BlokemonOpcode.AttachVim ->
            let minimum =
                match instruction.Sources with
                | null -> "zero"
                | sources when sources.Length = 0 -> "zero"
                | _ -> countLabel instruction.Amount

            dimension $"attachment/minimum/{minimum}"
            dimension $"attachment/maximum/{countLabel instruction.Amount}"
            dimension "attachment/eligible-target"
        | BlokemonSelection.AnyDistribution ->
            dimension "distribution/zero-allocation"
            dimension $"distribution/full/{instruction.Amount}"

            if instruction.Amount > 1 then
                dimension "distribution/split"
        | BlokemonSelection.UpTo ->
            dimension "cardinality/zero"

            if isUnboundedAttachedBarKitChuck instruction then
                dimension "cardinality/all-eligible"
            else
                dimension $"cardinality/maximum/{instruction.Amount}"
        | BlokemonSelection.All -> dimension "cardinality/all"
        | BlokemonSelection.Top -> dimension $"cardinality/top/{instruction.Amount}"
        | BlokemonSelection.BeerMat when instruction.Opcode = BlokemonOpcode.BeerMatToss ->
            dimension "random/badge"
            dimension "random/blank"
        | BlokemonSelection.BeerMat -> dimension "gate/badge"
        | BlokemonSelection.UntilBlankSide when
            instruction.Opcode = BlokemonOpcode.RepeatUntilBlankSide
            ->
            dimension "random/first-blank"
            dimension "random/badge-then-blank"
            dimension "repeat/one"
            dimension "repeat/many"
        | BlokemonSelection.UntilBlankSide -> dimension "gate/badge-count-positive"
        | selection -> invalidOp $"Unknown selection value {int selection}."

    let private amountDimensions
        requiredRows
        program
        occurrence
        (instruction: BlokemonEffectInstruction)
        =
        match instruction.ValueSource with
        | BlokemonValueSource.Fixed ->
            addDimension
                requiredRows
                program
                occurrence
                $"amount/boundary/declared/{instruction.Amount}"
        | BlokemonValueSource.PrintedDamage ->
            addDimension
                requiredRows
                program
                occurrence
                $"amount/source/PrintedDamage/declared/{instruction.Amount}"
        | BlokemonValueSource.BadgeSides ->
            addDimension requiredRows program occurrence "amount/source/BadgeSides/positive"
        | source ->
            addDimension requiredRows program occurrence $"amount/source/{source}/zero"
            addDimension requiredRows program occurrence $"amount/source/{source}/positive"

    let private triggerDimensions requiredRows program occurrence (row: ProgramRow) =
        match row.Kind, row.Trigger with
        | ProgramKind.PartyTrick, ValueSome BlokemonTrigger.Activated ->
            addDimension requiredRows program occurrence "activation/available"
            addDimension requiredRows program occurrence "activation/unavailable"
            addDimension requiredRows program occurrence "activation/result"
        | ProgramKind.PartyTrick, ValueSome trigger ->
            addDimension requiredRows program occurrence $"trigger/{trigger}/fire"
            addDimension requiredRows program occurrence $"trigger/{trigger}/nonfire"
        | ProgramKind.Attack, ValueNone
        | ProgramKind.HouseRule, ValueNone -> ()
        | ProgramKind.PartyTrick, ValueNone
        | ProgramKind.Attack, ValueSome _
        | ProgramKind.HouseRule, ValueSome _ ->
            invalidOp $"Program {row.MechanicalId} has an invalid kind/trigger combination."

    let private falsePredicateOutcomeIsApplicable
        (row: ProgramRow)
        (predicate: BlokemonEffectPredicate)
        =
        match predicate.Condition, row.Trigger with
        | BlokemonCondition.PromotedFromMittThisRound, ValueSome BlokemonTrigger.OnPromotionFromMitt
        | BlokemonCondition.OwnBlokeSentHomeByOtherAttackDamage,
          ValueSome BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage -> false
        | BlokemonCondition.OtherOcheHasMechanicalType, _ when not predicate.MechanicalType.HasValue ->
            // These attack programs use the untyped condition as an opposing-Oche
            // existence guard. A payable public Attack necessarily has that Oche.
            false
        | BlokemonCondition.TargetIsLandlord, _ when row.MechanicalId = "KIT-004-R01" ->
            // The public Bar Kit play route only offers its published Landlord target.
            false
        | _ -> true

    let private compileProgram (row: ProgramRow) =
        let program = programKey row
        let disposition = disposition row
        let requiredRows = ResizeArray<ConformanceRequiredObligation>()
        let mutations = ResizeArray<ConformanceMutationTarget>()
        let nonMutable = ResizeArray<ConformanceNonMutableOperand>()

        let mappedOperand =
            addMappedOperand disposition requiredRows mutations nonMutable program

        let nonMutableOperand rationale occurrence dimension pointer =
            addStructuralOperand nonMutable program (dimensionKey occurrence dimension) pointer

            if disposition = Executable then
                let index = nonMutable.Count - 1

                nonMutable[index] <-
                    { nonMutable[index] with
                        Rationale = rationale }

        let ignoredOperand = nonMutableOperand IgnoredTypedFieldRationale

        let rec compileInstructions
            prefix
            pointer
            inheritsMovedSelection
            (instructions: BlokemonEffectInstruction array)
            =
            for index, instruction in Array.indexed instructions do
                let path = $"{prefix}/{index}"
                let instructionPointer = $"{pointer}/{index}"
                let occurrence = instructionKey program path

                let priorCardSelection =
                    index > 0
                    && instructions[.. index - 1]
                       |> Array.exists (fun prior ->
                           prior.Opcode = BlokemonOpcode.SearchStack
                           || prior.Opcode = BlokemonOpcode.MoveCards
                           || prior.Opcode = BlokemonOpcode.RevealCards)

                let deferredEndRoundPayload =
                    index > 0
                    && instructions[.. index - 1]
                       |> Array.exists (fun prior -> prior.Opcode = BlokemonOpcode.EndRoundEffect)
                    && (instruction.Opcode = BlokemonOpcode.PlaceDamageCounters
                        || instruction.Opcode = BlokemonOpcode.HealDamage)

                let continuousAttachSkipped =
                    instruction.Opcode = BlokemonOpcode.AttachVim
                    && row.Kind = ProgramKind.PartyTrick
                    && row.Trigger = ValueSome BlokemonTrigger.Continuous

                let swapUsesPriorMoveSelection =
                    instruction.Opcode = BlokemonOpcode.SwapOche
                    && (inheritsMovedSelection
                        || (index > 0 && instructions[index - 1].Opcode = BlokemonOpcode.MoveCards))

                let usesAttachedToolTarget =
                    row.Kind = ProgramKind.HouseRule
                    && instruction.Opcode = BlokemonOpcode.ModifySoftSpot
                    && instruction.Targets.Length = 0
                    && (match instruction.Sources with
                        | null -> true
                        | sources -> sources.Length = 0)

                let transformUsesPriorSelection =
                    instruction.Opcode = BlokemonOpcode.TransformFromStack && priorCardSelection

                let amountIsSemanticHere =
                    amountIsSemantic instruction
                    && not deferredEndRoundPayload
                    && not (isUnboundedAttachedBarKitChuck instruction)

                let mechanicalTypesAreSemanticHere =
                    mechanicalTypesAreSemantic instruction
                    && not continuousAttachSkipped
                    && not (
                        instruction.Opcode = BlokemonOpcode.AttachVim
                        && priorCardSelection
                        && (match instruction.Sources with
                            | null -> true
                            | sources -> sources.Length = 0)
                    )

                let consumesPriorSelectionWithoutTargets =
                    priorCardSelection
                    && (instruction.Opcode = BlokemonOpcode.RevealCards
                        || (instruction.Opcode = BlokemonOpcode.MoveCards
                            && (match instruction.Sources with
                                | null -> true
                                | sources -> sources.Length = 0)))

                let targetsAreSemanticHere =
                    targetsAreSemantic instruction
                    && not continuousAttachSkipped
                    && not swapUsesPriorMoveSelection
                    && not consumesPriorSelectionWithoutTargets
                    && not deferredEndRoundPayload
                    && not (
                        row.Kind = ProgramKind.Attack
                        && instruction.Opcode = BlokemonOpcode.ScaleDamage
                        && instruction.ValueSource = BlokemonValueSource.Fixed
                        && instruction.Selection = BlokemonSelection.All
                    )

                let selectionIsSemanticHere =
                    selectionIsSemantic instruction
                    && not continuousAttachSkipped
                    && not swapUsesPriorMoveSelection
                    && not usesAttachedToolTarget
                    && not transformUsesPriorSelection
                    && not consumesPriorSelectionWithoutTargets
                    && not deferredEndRoundPayload

                let relatedIdsAreSemanticHere =
                    instruction.Opcode <> BlokemonOpcode.ShuffleStack
                    && instruction.Opcode <> BlokemonOpcode.OncePerRound
                    && not consumesPriorSelectionWithoutTargets
                    && not deferredEndRoundPayload

                addRequired requiredRows program InstructionOccurrence occurrence

                match disposition with
                | Executable ->
                    addDimension requiredRows program occurrence $"opcode/{instruction.Opcode}"
                | StructuralNonExecutable ->
                    addStructuralOperand
                        nonMutable
                        program
                        (dimensionKey occurrence $"opcode/{instruction.Opcode}")
                        $"{instructionPointer}/opcode"

                if amountIsSemanticHere && not continuousAttachSkipped then
                    mappedOperand
                        occurrence
                        $"operand/amount/{instruction.Amount}"
                        $"{instructionPointer}/amount"
                        (amountMutation instruction.Amount)
                else
                    nonMutableOperand
                        (if continuousAttachSkipped then
                             ContinuousAttachSkippedRationale
                         elif isUnboundedAttachedBarKitChuck instruction then
                             UnboundedAttachedBarKitAmountRationale
                         else
                             IgnoredTypedFieldRationale)
                        occurrence
                        $"operand/amount/{instruction.Amount}"
                        $"{instructionPointer}/amount"

                if valueSourceIsSemantic instruction then
                    mappedOperand
                        occurrence
                        $"operand/value-source/{instruction.ValueSource}"
                        $"{instructionPointer}/valueSource"
                        (ReplaceString(valueSourceReplacement instruction))
                else
                    ignoredOperand
                        occurrence
                        $"operand/value-source/{instruction.ValueSource}"
                        $"{instructionPointer}/valueSource"

                for targetIndex, target in Array.indexed instruction.Targets do
                    if targetsAreSemanticHere then
                        mappedOperand
                            occurrence
                            $"target/{targetIndex}/{target}"
                            $"{instructionPointer}/targets/{targetIndex}"
                            (ReplaceString(string (targetReplacement instruction target)))
                    else
                        nonMutableOperand
                            (if continuousAttachSkipped then
                                 ContinuousAttachSkippedRationale
                             else
                                 IgnoredTypedFieldRationale)
                            occurrence
                            $"target/{targetIndex}/{target}"
                            $"{instructionPointer}/targets/{targetIndex}"

                if selectionIsSemanticHere then
                    mappedOperand
                        occurrence
                        $"operand/selection/{instruction.Selection}"
                        $"{instructionPointer}/selection"
                        (ReplaceString(string (selectionReplacement instruction.Selection)))
                else
                    nonMutableOperand
                        (if continuousAttachSkipped then
                             ContinuousAttachSkippedRationale
                         else
                             IgnoredTypedFieldRationale)
                        occurrence
                        $"operand/selection/{instruction.Selection}"
                        $"{instructionPointer}/selection"

                if
                    selectionIsSemanticHere
                    && ((instruction.Selection = BlokemonSelection.All
                         && instruction.TargetCount > 1)
                        || instruction.Selection = BlokemonSelection.Chosen
                        || instruction.Selection = BlokemonSelection.OtherSideChosen
                        || instruction.Selection = BlokemonSelection.SeededRandom)
                then
                    mappedOperand
                        occurrence
                        $"operand/target-count/{instruction.TargetCount}"
                        $"{instructionPointer}/targetCount"
                        (amountMutation instruction.TargetCount)
                else
                    nonMutableOperand
                        (if continuousAttachSkipped then
                             ContinuousAttachSkippedRationale
                         else
                             IgnoredTypedFieldRationale)
                        occurrence
                        $"operand/target-count/{instruction.TargetCount}"
                        $"{instructionPointer}/targetCount"

                for predicateIndex, predicate in Array.indexed instruction.Predicates do
                    let predicatePath = $"{path}/predicate/{predicateIndex}"
                    let predicateOccurrence = instructionKey program predicatePath
                    let predicatePointer = $"{instructionPointer}/predicates/{predicateIndex}"

                    addRequired requiredRows program PredicateRecord predicateOccurrence

                    match disposition with
                    | Executable ->
                        addDimension requiredRows program predicateOccurrence "truth/true"

                        if falsePredicateOutcomeIsApplicable row predicate then
                            addDimension requiredRows program predicateOccurrence "truth/false"
                    | StructuralNonExecutable -> ()

                    if
                        row.Trigger = ValueSome
                            BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage
                        && predicate.Condition = BlokemonCondition.Optional
                    then
                        if disposition = Executable then
                            addDimension
                                requiredRows
                                program
                                predicateOccurrence
                                $"condition/{predicate.Condition}"

                        nonMutableOperand
                            KnockoutOptionalProtocolRationale
                            predicateOccurrence
                            $"condition/{predicate.Condition}"
                            $"{predicatePointer}/condition"
                    elif
                        row.Trigger = ValueSome BlokemonTrigger.OnBarChitTaken
                        && predicate.Condition = BlokemonCondition.Optional
                    then
                        if disposition = Executable then
                            addDimension
                                requiredRows
                                program
                                predicateOccurrence
                                $"condition/{predicate.Condition}"

                        nonMutableOperand
                            BarChitOptionalProtocolRationale
                            predicateOccurrence
                            $"condition/{predicate.Condition}"
                            $"{predicatePointer}/condition"
                    else
                        mappedOperand
                            predicateOccurrence
                            $"condition/{predicate.Condition}"
                            $"{predicatePointer}/condition"
                            (ReplaceString(
                                string (
                                    conditionReplacement
                                        (instruction.Predicates
                                         |> Array.exists (fun other ->
                                             other.Condition = BlokemonCondition.Optional))
                                        predicate.Condition
                                )
                            ))

                    ignoredOperand
                        predicateOccurrence
                        $"value/{predicate.Value}"
                        $"{predicatePointer}/value"

                    if predicate.MechanicalType.HasValue then
                        mappedOperand
                            predicateOccurrence
                            $"mechanical-type/{predicate.MechanicalType.Value}"
                            $"{predicatePointer}/mechanicalType"
                            (ReplaceString(enumReplacement predicate.MechanicalType.Value))

                    if predicate.RoughState.HasValue then
                        mappedOperand
                            predicateOccurrence
                            $"rough-state/{predicate.RoughState.Value}"
                            $"{predicatePointer}/roughState"
                            (ReplaceString(enumReplacement predicate.RoughState.Value))

                    match predicate.RelatedId with
                    | null -> ()
                    | relatedId ->
                        mappedOperand
                            predicateOccurrence
                            $"related-id/{relatedId}"
                            $"{predicatePointer}/relatedId"
                            (ReplaceString(stringReplacement relatedId))

                for mechanicalTypeIndex, mechanicalType in Array.indexed instruction.MechanicalTypes do
                    if mechanicalTypesAreSemanticHere then
                        mappedOperand
                            occurrence
                            $"operand/mechanical-type/{mechanicalTypeIndex}/{mechanicalType}"
                            $"{instructionPointer}/mechanicalTypes/{mechanicalTypeIndex}"
                            (ReplaceString(enumReplacement mechanicalType))
                    else
                        nonMutableOperand
                            (if continuousAttachSkipped then
                                 ContinuousAttachSkippedRationale
                             else
                                 IgnoredTypedFieldRationale)
                            occurrence
                            $"operand/mechanical-type/{mechanicalTypeIndex}/{mechanicalType}"
                            $"{instructionPointer}/mechanicalTypes/{mechanicalTypeIndex}"

                for roughStateIndex, roughState in Array.indexed instruction.RoughStates do
                    if
                        scaleDamageMarker instruction
                        || instruction.Opcode = BlokemonOpcode.TriggeredPartyTrick
                    then
                        ignoredOperand
                            occurrence
                            $"operand/rough-state/{roughStateIndex}/{roughState}"
                            $"{instructionPointer}/roughStates/{roughStateIndex}"
                    else
                        mappedOperand
                            occurrence
                            $"operand/rough-state/{roughStateIndex}/{roughState}"
                            $"{instructionPointer}/roughStates/{roughStateIndex}"
                            (ReplaceString(enumReplacement roughState))

                for relatedIdIndex, relatedId in Array.indexed instruction.RelatedIds do
                    if relatedIdsAreSemanticHere then
                        mappedOperand
                            occurrence
                            $"operand/related-id/{relatedIdIndex}/{relatedId}"
                            $"{instructionPointer}/relatedIds/{relatedIdIndex}"
                            (ReplaceString(stringReplacement relatedId))
                    else
                        ignoredOperand
                            occurrence
                            $"operand/related-id/{relatedIdIndex}/{relatedId}"
                            $"{instructionPointer}/relatedIds/{relatedIdIndex}"

                match instruction.Sources with
                | null -> ()
                | sources ->
                    for sourceIndex, source in Array.indexed sources do
                        if
                            row.Kind = ProgramKind.Attack
                            && instruction.Opcode = BlokemonOpcode.MoveCards
                            && source = BlokemonTarget.Self
                        then
                            nonMutableOperand
                                AttackSelfOwnOcheSourceRationale
                                occurrence
                                $"source/{sourceIndex}/{source}"
                                $"{instructionPointer}/sources/{sourceIndex}"
                        else
                            mappedOperand
                                occurrence
                                $"source/{sourceIndex}/{source}"
                                $"{instructionPointer}/sources/{sourceIndex}"
                                (ReplaceString(enumReplacement source))

                if instruction.Destination <> BlokemonEffectDestination.Unspecified then
                    mappedOperand
                        occurrence
                        $"operand/destination/{instruction.Destination}"
                        $"{instructionPointer}/destination"
                        (ReplaceString(enumReplacement instruction.Destination))

                match instruction.CardFilter with
                | null -> ()
                | filter ->
                    let filterPointer = $"{instructionPointer}/cardFilter"

                    let vimCategoryAlreadyExact =
                        filter.Categories.Length = 1
                        && filter.Categories[0] = BlokemonCardCategory.Vim

                    let differentMechanicalTypesIsSemantic =
                        match instruction.Selection with
                        | BlokemonSelection.Chosen
                        | BlokemonSelection.OtherSideChosen -> instruction.TargetCount > 1
                        | BlokemonSelection.UpTo -> instruction.Amount > 1
                        | _ -> false

                    for categoryIndex, category in Array.indexed filter.Categories do
                        mappedOperand
                            occurrence
                            $"filter/category/{categoryIndex}/{category}"
                            $"{filterPointer}/categories/{categoryIndex}"
                            (ReplaceString(enumReplacement category))

                    for rankIndex, rank in Array.indexed filter.Ranks do
                        mappedOperand
                            occurrence
                            $"filter/rank/{rankIndex}/{rank}"
                            $"{filterPointer}/ranks/{rankIndex}"
                            (ReplaceString(enumReplacement rank))

                    for kitKindIndex, kitKind in Array.indexed filter.KitKinds do
                        mappedOperand
                            occurrence
                            $"filter/kit-kind/{kitKindIndex}/{kitKind}"
                            $"{filterPointer}/kitKinds/{kitKindIndex}"
                            (ReplaceString(enumReplacement kitKind))

                    if vimCategoryAlreadyExact then
                        ignoredOperand
                            occurrence
                            $"filter/basic-vim-only/{filter.BasicVimOnly}"
                            $"{filterPointer}/basicVimOnly"
                    else
                        mappedOperand
                            occurrence
                            $"filter/basic-vim-only/{filter.BasicVimOnly}"
                            $"{filterPointer}/basicVimOnly"
                            ToggleBoolean

                    if differentMechanicalTypesIsSemantic then
                        mappedOperand
                            occurrence
                            $"filter/different-mechanical-types/{filter.DifferentMechanicalTypes}"
                            $"{filterPointer}/differentMechanicalTypes"
                            ToggleBoolean
                    else
                        ignoredOperand
                            occurrence
                            $"filter/different-mechanical-types/{filter.DifferentMechanicalTypes}"
                            $"{filterPointer}/differentMechanicalTypes"

                    for excludedIndex, excluded in Array.indexed filter.ExcludedRelatedIds do
                        mappedOperand
                            occurrence
                            $"filter/excluded-related-id/{excludedIndex}/{excluded}"
                            $"{filterPointer}/excludedRelatedIds/{excludedIndex}"
                            (ReplaceString(excludedIdReplacement excluded))

                if instruction.SourceTopCount <> 0 then
                    mappedOperand
                        occurrence
                        $"operand/source-top-count/{instruction.SourceTopCount}"
                        $"{instructionPointer}/sourceTopCount"
                        (amountMutation instruction.SourceTopCount)

                match disposition with
                | Executable ->
                    if
                        selectionIsSemanticHere
                        || instruction.Opcode = BlokemonOpcode.BeerMatToss
                        || instruction.Opcode = BlokemonOpcode.RepeatUntilBlankSide
                    then
                        if not (scaleDamageMarker instruction) then
                            selectionDimensions requiredRows program occurrence instruction

                    if valueSourceIsSemantic instruction && not (scaleDamageMarker instruction) then
                        amountDimensions requiredRows program occurrence instruction
                    elif amountIsSemanticHere && not continuousAttachSkipped then
                        addDimension
                            requiredRows
                            program
                            occurrence
                            $"amount/boundary/declared/{instruction.Amount}"

                    if path = "root/0" then
                        triggerDimensions requiredRows program occurrence row
                | StructuralNonExecutable -> ()

                if instruction.Then.Length > 0 then
                    let edge = instructionKey program $"{path}/then"
                    addRequired requiredRows program BranchEdge edge

                    match disposition with
                    | Executable ->
                        mutations.Add
                            { Pointer = $"{instructionPointer}/then"
                              ProgramKey = program
                              NamedObligation = edge
                              Operation = RemoveBranch }
                    | StructuralNonExecutable ->
                        addStructuralOperand nonMutable program edge $"{instructionPointer}/then"

                    compileInstructions
                        $"{path}/then"
                        $"{instructionPointer}/then"
                        (instruction.Opcode = BlokemonOpcode.MoveCards)
                        instruction.Then

                if instruction.Otherwise.Length > 0 then
                    let edge = instructionKey program $"{path}/otherwise"
                    addRequired requiredRows program BranchEdge edge

                    match disposition with
                    | Executable ->
                        mutations.Add
                            { Pointer = $"{instructionPointer}/otherwise"
                              ProgramKey = program
                              NamedObligation = edge
                              Operation = RemoveBranch }
                    | StructuralNonExecutable ->
                        addStructuralOperand
                            nonMutable
                            program
                            edge
                            $"{instructionPointer}/otherwise"

                    compileInstructions
                        $"{path}/otherwise"
                        $"{instructionPointer}/otherwise"
                        false
                        instruction.Otherwise

        compileInstructions "root" row.AuthorityPointer false row.Program

        { Row = row
          ProgramKey = program
          Disposition = disposition
          Required = requiredRows.ToArray()
          Mutations = mutations.ToArray()
          NonMutableOperands = nonMutable.ToArray() }

    let compile (fixture: ConformanceFixtureData) =
        let programs = fixture.Authority |> programRowsFrom |> Array.map compileProgram

        { Programs = programs
          Required = programs |> Array.collect _.Required
          Mutations = programs |> Array.collect _.Mutations
          NonMutableOperands = programs |> Array.collect _.NonMutableOperands }

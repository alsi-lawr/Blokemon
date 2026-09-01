namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.ChoiceShapes
open Blokemon.Game.ChoiceInspection
open Blokemon.Game.ChoiceValidation
open Blokemon.Game.EffectSelection
open Blokemon.Game.EffectDamage
open Blokemon.Game.EffectPredicates
open Blokemon.Game.EffectRegistration
open Blokemon.Game.EffectCardMoves
open Blokemon.Game.EffectVimOperations
open Blokemon.Game.EffectCardTransforms

/// The interpreter's recursive core: running a program, running one instruction, and the four
/// instructions that run a branch of their own. F# has no cross-file recursion, so these stay
/// together; every opcode that does not re-enter the runner lives in the modules above.
module internal EffectExecution =

    let rec private copiedAttackProgram (program: BlokemonEffectInstruction array) =
        program
        |> Array.choose (fun instruction ->
            let isUseRequirement =
                instruction.Opcode = BlokemonOpcode.RequireDefenderCondition
                || (instruction.Opcode = BlokemonOpcode.ChuckVim
                    && Array.contains BlokemonTarget.Self instruction.Targets)

            if isUseRequirement then
                None
            else
                Some
                    { instruction with
                        Then = copiedAttackProgram instruction.Then
                        Otherwise = copiedAttackProgram instruction.Otherwise })

    let private requirementsFor
        (runtime: EffectRuntime)
        (branch: BlokemonEffectInstruction array)
        (branchPath: string)
        =
        let requirements = ResizeArray<ChoiceRequirement>()

        inspectProgram
            runtime.Catalog
            runtime.Builder
            runtime.Actor
            runtime.Source
            runtime.Effect
            branch
            branchPath
            ValueNone
            requirements

        ImmutableArray.CreateRange(requirements |> Seq.distinctBy (fun value -> value.Id))

    let private requireBranchChoices
        (runtime: EffectRuntime)
        (branch: BlokemonEffectInstruction array)
        (branchPath: string)
        =
        let distinct = requirementsFor runtime branch branchPath

        let branchChoices =
            ImmutableArray.CreateRange(
                runtime.Choices
                |> Seq.filter (fun choice ->
                    distinct |> Seq.exists (fun requirement -> requirement.Id = choice.Id))
            )

        match validateChoices branchChoices distinct with
        | ValueSome CommandRejectionCode.ChoiceRequired ->
            runtime.Defer distinct
            false
        | ValueSome rejection ->
            runtime.Rejection <- ValueSome rejection
            false
        | ValueNone ->
            runtime.Use distinct
            true

    let rec executeProgram
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (program: BlokemonEffectInstruction array)
        (parentPath: string)
        =
        let mutable index = 0

        while index < program.Length
              && runtime.Rejection.IsNone
              && runtime.DeferredRequirements.Length = 0 do
            let instruction = program[index]

            if
                not (
                    runtime.BeerMatGateParent = ValueSome parentPath
                    && runtime.BadgeSides = 0
                    && instruction.Opcode <> BlokemonOpcode.BeerMatToss
                )
            then
                executeInstruction catalog runtime instruction $"{parentPath}/{index}"

            index <- index + 1

    and private executeBeerMat catalog (runtime: EffectRuntime) instruction path =
        runtime.BadgeSides <- 0
        runtime.TossCount <- instruction.Amount
        runtime.FirstBeerMatIsBlank <- false

        runtime.BeerMatGateParent <-
            if instruction.Then.Length = 0 && instruction.Otherwise.Length = 0 then
                ValueSome(parentPath path)
            else
                ValueNone

        for toss in 0 .. instruction.Amount - 1 do
            let badge = runtime.NextBeerMat()

            if badge then
                runtime.BadgeSides <- runtime.BadgeSides + 1

            if toss = 0 then
                runtime.FirstBeerMatIsBlank <- not badge

            runtime.RecordBeerMatEvent badge

        let branch =
            if runtime.BadgeSides > 0 then
                instruction.Then
            else
                instruction.Otherwise

        let branchPath = path + (if runtime.BadgeSides > 0 then "/then" else "/otherwise")

        if requireBranchChoices runtime branch branchPath then
            executeProgram catalog runtime branch branchPath

    and private executeUntilBlank catalog (runtime: EffectRuntime) instruction path =
        runtime.BadgeSides <- 0
        runtime.TossCount <- 0
        let mutable running = true

        while running do
            let badge = runtime.NextBeerMat()
            runtime.TossCount <- runtime.TossCount + 1
            runtime.RecordBeerMatEvent badge

            if badge then
                runtime.BadgeSides <- runtime.BadgeSides + 1
            else
                runtime.FirstBeerMatIsBlank <- runtime.TossCount = 1
                running <- false

        let branchPath = path + "/then"

        if requireBranchChoices runtime instruction.Then branchPath then
            executeProgram catalog runtime instruction.Then branchPath

    and private executeConditional catalog (runtime: EffectRuntime) instruction path =
        let passed =
            instruction.Predicates
            |> Array.forall (fun predicate -> evaluatePredicate catalog runtime predicate path)

        let branch = if passed then instruction.Then else instruction.Otherwise
        let branchPath = path + (if passed then "/then" else "/otherwise")

        if requireBranchChoices runtime branch branchPath then
            executeProgram catalog runtime branch branchPath

    and private executeCopyAttack (catalog: AuthorityCatalog) (runtime: EffectRuntime) path =
        match runtime.AttackChoice(choiceId runtime.Effect path "attack") with
        | ValueNone -> runtime.Rejection <- ValueSome CommandRejectionCode.EffectUnavailable
        | ValueSome chosen when runtime.CopyStack.Contains chosen ->
            runtime.Rejection <- ValueSome CommandRejectionCode.EffectUnavailable
        | ValueSome chosen ->
            match catalog.Attack chosen with
            | ValueNone -> runtime.Rejection <- ValueSome CommandRejectionCode.EffectNotFound
            | ValueSome attack ->
                runtime.CopyStack.Add chosen |> ignore
                let copyPath = path + "/copy"
                let program = copiedAttackProgram attack.Program

                if requireBranchChoices runtime program copyPath then
                    executeProgram catalog runtime program copyPath

                runtime.CopyStack.Remove chosen |> ignore

    and private executeMoveCards catalog (runtime: EffectRuntime) instruction path =
        let selected =
            if hasDeclaredSources instruction || not runtime.HasCardSelection then
                resolveSelectedTargets catalog runtime instruction path |> Seq.toArray
            else
                runtime.LastSelectedCards |> Seq.map runtime.Builder.Card |> Seq.toArray

        let moved =
            moveCardsToDestination
                catalog
                runtime
                (selected |> Seq.truncate instruction.Amount)
                instruction.Destination

        runtime.LastSelectedCards <-
            ImmutableArray.CreateRange(selected |> Array.map (fun card -> card.Id))

        runtime.HasCardSelection <- true

        if
            moved > 0
            && instruction.Then.Length > 0
            && requireBranchChoices runtime instruction.Then (path + "/then")
        then
            executeProgram catalog runtime instruction.Then (path + "/then")

    and private executeInstruction
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        let builder = runtime.Builder

        let selectedTargets () =
            resolveSelectedTargets catalog runtime instruction path |> Seq.toArray

        let register kind =
            registerEffect catalog runtime instruction kind ValueNone

        let runThen =
            match EffectInstructions.executeSimple catalog runtime instruction path with
            | ValueSome result -> result
            | ValueNone ->

                match instruction.Opcode with
                | BlokemonOpcode.MoveCards ->
                    executeMoveCards catalog runtime instruction path
                    false
                | BlokemonOpcode.BeerMatToss ->
                    executeBeerMat catalog runtime instruction path
                    false
                | BlokemonOpcode.RepeatUntilBlankSide ->
                    executeUntilBlank catalog runtime instruction path
                    false
                | BlokemonOpcode.Conditional ->
                    executeConditional catalog runtime instruction path
                    false
                | BlokemonOpcode.CopyAttack ->
                    executeCopyAttack catalog runtime path
                    true
                | BlokemonOpcode.PlayAsBloke ->
                    playAsBloke runtime
                    true
                | BlokemonOpcode.OncePerRound ->
                    builder.RoundUsage <-
                        { builder.RoundUsage with
                            EffectsUsed =
                                ImmutableArray.CreateRange(
                                    Seq.append builder.RoundUsage.EffectsUsed [ runtime.Effect ]
                                    |> Seq.distinct
                                ) }

                    true
                | _ ->
                    runtime.Rejection <- ValueSome CommandRejectionCode.AuthorityMismatch
                    false

        if runThen then
            executeProgram catalog runtime instruction.Then (path + "/then")

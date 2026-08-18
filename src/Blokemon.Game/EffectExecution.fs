namespace Blokemon.Game

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
            runtime.TriggerContext

        FrozenList<ChoiceRequirement>.Create(requirements |> Seq.distinctBy (fun value -> value.Id))

    let private requireBranchChoices
        (runtime: EffectRuntime)
        (branch: BlokemonEffectInstruction array)
        (branchPath: string)
        =
        let distinct = requirementsFor runtime branch branchPath

        let branchChoices =
            FrozenList<EffectChoice>
                .Create(
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
              && runtime.DeferredRequirements.Count = 0 do
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

                if requireBranchChoices runtime attack.Program copyPath then
                    executeProgram catalog runtime attack.Program copyPath

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
            FrozenList<CardInstanceId>.Create(selected |> Array.map (fun card -> card.Id))

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
                | BlokemonOpcode.SendHome ->
                    for target in selectedTargets () do
                        if not (effectIsPrevented runtime target) then
                            runtime.ForcedSendHome.Add target.Id |> ignore

                    true
                | BlokemonOpcode.RecoverFromSendHome ->
                    let recovered = builder.Card runtime.Source.Id

                    builder.SetCard
                        { recovered with
                            Damage = max 0 (catalog.StayingPower recovered - instruction.Amount) }

                    true
                | BlokemonOpcode.CopyAttack ->
                    executeCopyAttack catalog runtime path
                    true
                | BlokemonOpcode.Demote ->
                    for target in selectedTargets () do
                        if not (effectIsPrevented runtime target) then
                            demote catalog runtime target

                    true
                | BlokemonOpcode.TransformFromStack ->
                    executeTransform catalog runtime instruction path
                    true
                | BlokemonOpcode.TakeExtraBarChit ->
                    if runtime.IsAttack then
                        runtime.DeferredAttackKnockoutBarChits <-
                            runtime.DeferredAttackKnockoutBarChits + instruction.Amount
                    else
                        takeBarChits
                            catalog
                            builder
                            runtime.Actor
                            instruction.Amount
                            runtime.Source.Id

                    true
                | BlokemonOpcode.PlayAsBloke ->
                    playAsBloke runtime
                    true
                | BlokemonOpcode.ChuckSelf ->
                    if not runtime.IsHouseRule then
                        builder.ChuckBloke runtime.Source.Id |> ignore
                        runtime.SourceChucked <- true

                    true
                | BlokemonOpcode.ContinuousPartyTrick ->
                    register TemporaryEffectKind.ContinuousPartyTrick
                    true
                | BlokemonOpcode.OncePerRound ->
                    builder.RoundUsage <-
                        { builder.RoundUsage with
                            EffectsUsed =
                                FrozenList<EffectId>
                                    .Create(
                                        Seq.append builder.RoundUsage.EffectsUsed [ runtime.Effect ]
                                        |> Seq.distinct
                                    ) }

                    true
                | BlokemonOpcode.EndRoundEffect ->
                    register TemporaryEffectKind.EndRoundEffect
                    runtime.DeferringEndRound <- true
                    true
                | _ -> true

        if runThen then
            executeProgram catalog runtime instruction.Then (path + "/then")

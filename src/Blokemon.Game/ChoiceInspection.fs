namespace Blokemon.Game

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.ChoiceShapes

/// Working out what an effect has to ask before it can run, without running any of it.
module internal ChoiceInspection =

    let private deferConditionalBranches (catalog: AuthorityCatalog) (effect: EffectId) =
        match catalog.PartyTrick effect with
        | ValueSome trick -> trick.Trigger <> BlokemonTrigger.OnPromotionFromMitt
        | ValueNone -> true

    let remainingBoothCapacity
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (destination: BlokemonEffectDestination)
        =
        let remaining player =
            max
                0
                (catalog.Manifest.BaseRules.Opening.BoothLimit
                 - (builder.CardsIn(player, CardZone.Booth) |> Seq.length))

        match destination with
        | BlokemonEffectDestination.OwnBooth -> ValueSome(remaining actor)
        | BlokemonEffectDestination.OtherBooth -> ValueSome(remaining (builder.Other actor))
        | _ -> ValueNone

    let private inspectInstructionChoice
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (effect: EffectId)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        (dependency: EffectChoiceId voption)
        (requirements: ResizeArray<ChoiceRequirement>)
        (triggerContext: TriggerContext voption)
        =
        let candidatesOf () =
            resolveCandidates catalog builder actor source instruction triggerContext

        if
            instruction.Opcode = BlokemonOpcode.AttachVim
            && instruction.Selection = BlokemonSelection.AnyDistribution
        then
            let eligibleVim =
                (if hasDeclaredSources instruction then
                     candidatesOf ()
                 else
                     builder.CardsIn(actor, CardZone.Stack)
                     |> Seq.truncate instruction.Amount
                     |> Seq.filter (fun card -> card.Kind = CardKind.Vim))
                |> Seq.map (fun card -> card.Id)
                |> Seq.toArray

            let eligibleTargets =
                (if instruction.Targets.Length > 0 then
                     instruction.Targets
                     |> Seq.collect (fun target ->
                         resolveTarget catalog builder actor source target triggerContext)
                 else
                     inPlay builder actor)
                |> Seq.filter isInPlay
                |> Seq.map (fun card -> card.Id)
                |> Seq.distinct
                |> Seq.sort
                |> Seq.toArray

            let required =
                if hasDeclaredSources instruction then
                    min instruction.Amount eligibleVim.Length
                else
                    eligibleVim.Length

            if required > 0 && eligibleTargets.Length > 0 then
                requirements.Add
                    { ChoiceRequirement.create
                          (choiceId effect path "attachments")
                          ChoiceRequirementKind.Attachments
                          actor
                          (if hasDeclaredSources instruction then required else 0)
                          required
                          (ImmutableArray.CreateRange eligibleVim)
                          ImmutableArray<_>.Empty
                          ImmutableArray<_>.Empty
                          dependency with
                        EligibleTargets = ImmutableArray.CreateRange eligibleTargets }
        else

            if
                instruction.Opcode = BlokemonOpcode.ModifySoftSpot
                && instruction.Selection = BlokemonSelection.Chosen
                && instruction.MechanicalTypes.Length > 1
            then
                requirements.Add(
                    ChoiceRequirement.create
                        (choiceId effect path "type")
                        ChoiceRequirementKind.MechanicalType
                        actor
                        1
                        1
                        ImmutableArray<_>.Empty
                        (ImmutableArray.CreateRange instruction.MechanicalTypes)
                        ImmutableArray<_>.Empty
                        dependency
                )

            if instruction.Opcode = BlokemonOpcode.CopyAttack then
                let opponent = builder.Other actor

                let effects =
                    (match builder.Oche opponent with
                     | ValueSome card -> Seq.singleton card
                     | ValueNone -> Seq.empty)
                    |> Seq.collect catalog.Attacks
                    |> Seq.map (fun attack -> EffectId attack.MechanicalId)
                    |> Seq.toArray

                if effects.Length > 0 then
                    requirements.Add(
                        ChoiceRequirement.create
                            (choiceId effect path "attack")
                            ChoiceRequirementKind.Attack
                            actor
                            1
                            1
                            ImmutableArray<_>.Empty
                            ImmutableArray<_>.Empty
                            (ImmutableArray.CreateRange effects)
                            dependency
                    )
            elif instruction.Selection = BlokemonSelection.AnyDistribution then
                if instruction.Opcode = BlokemonOpcode.PlaceDamageCounters then
                    let eligible =
                        resolveCandidates catalog builder actor source instruction ValueNone
                        |> Seq.map (fun card -> card.Id)
                        |> Seq.toArray

                    if eligible.Length > 0 then
                        requirements.Add(
                            ChoiceRequirement.create
                                (choiceId effect path "distribution")
                                ChoiceRequirementKind.Distribution
                                actor
                                0
                                instruction.Amount
                                (ImmutableArray.CreateRange eligible)
                                ImmutableArray<_>.Empty
                                ImmutableArray<_>.Empty
                                dependency
                        )
            elif instructionOwnsCardChoice instruction then
                let boothCapacity =
                    remainingBoothCapacity catalog builder actor instruction.Destination

                if
                    instruction.Opcode <> BlokemonOpcode.SearchStack || boothCapacity <> ValueSome 0
                then
                    let candidateCards =
                        candidatesOf ()
                        |> Seq.distinct
                        |> Seq.sortBy (fun card -> card.Id)
                        |> Seq.toArray

                    let candidates = candidateCards |> Array.map (fun card -> card.Id)

                    let selectionMaximum =
                        match instruction.Selection with
                        | BlokemonSelection.Chosen
                        | BlokemonSelection.OtherSideChosen ->
                            min instruction.TargetCount candidates.Length
                        | BlokemonSelection.UpTo
                        | BlokemonSelection.All -> min instruction.Amount candidates.Length
                        | _ -> 0

                    let maximum =
                        if instruction.Destination = BlokemonEffectDestination.OwnBooth then
                            min
                                selectionMaximum
                                (catalog.Manifest.BaseRules.Opening.BoothLimit
                                 - (builder.CardsIn(actor, CardZone.Booth) |> Seq.length))
                        elif instruction.Destination = BlokemonEffectDestination.OtherBooth then
                            min
                                selectionMaximum
                                (catalog.Manifest.BaseRules.Opening.BoothLimit
                                 - (builder.CardsIn(builder.Other actor, CardZone.Booth)
                                    |> Seq.length))
                        else
                            selectionMaximum

                    if maximum <> 0 || instruction.Selection = BlokemonSelection.UpTo then
                        let minimum =
                            if instruction.Selection = BlokemonSelection.UpTo then
                                0
                            else
                                maximum

                        let chooser =
                            if instruction.Selection = BlokemonSelection.OtherSideChosen then
                                builder.Other actor
                            else
                                actor

                        requirements.Add
                            { ChoiceRequirement.create
                                  (choiceId effect path "cards")
                                  ChoiceRequirementKind.Cards
                                  chooser
                                  minimum
                                  maximum
                                  (ImmutableArray.CreateRange candidates)
                                  ImmutableArray<_>.Empty
                                  ImmutableArray<_>.Empty
                                  dependency with
                                RequireDifferentMechanicalTypes =
                                    (match instruction.CardFilter with
                                     | null -> false
                                     | filter -> filter.DifferentMechanicalTypes)
                                EligibleCardTypes =
                                    ImmutableArray.CreateRange(
                                        candidateCards
                                        |> Array.map (fun card ->
                                            { Card = card.Id
                                              Types = catalog.MechanicalTypes card })
                                    ) }

    let rec inspectProgram
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (effect: EffectId)
        (program: BlokemonEffectInstruction array)
        (parentPath: string)
        (optionalDependency: EffectChoiceId voption)
        (requirements: ResizeArray<ChoiceRequirement>)
        (triggerContext: TriggerContext voption)
        =
        for index in 0 .. program.Length - 1 do
            let instruction = program[index]
            let path = $"{parentPath}/{index}"

            let dependency =
                if
                    instruction.Opcode = BlokemonOpcode.Conditional
                    && instruction.Predicates
                       |> Array.exists (fun predicate ->
                           predicate.Condition = BlokemonCondition.Optional)
                then
                    let optionalId = choiceId effect path "optional"

                    requirements.Add(
                        ChoiceRequirement.create
                            optionalId
                            ChoiceRequirementKind.Optional
                            actor
                            0
                            1
                            ImmutableArray<_>.Empty
                            ImmutableArray<_>.Empty
                            ImmutableArray<_>.Empty
                            optionalDependency
                    )

                    ValueSome optionalId
                else
                    optionalDependency

            inspectInstructionChoice
                catalog
                builder
                actor
                source
                effect
                instruction
                path
                dependency
                requirements
                triggerContext

            let skipBranches =
                instruction.Opcode = BlokemonOpcode.BeerMatToss
                || instruction.Opcode = BlokemonOpcode.RepeatUntilBlankSide
                || (instruction.Opcode = BlokemonOpcode.MoveCards && instruction.Then.Length > 0)
                || (instruction.Opcode = BlokemonOpcode.Conditional
                    && deferConditionalBranches catalog effect)

            if not skipBranches then
                inspectProgram
                    catalog
                    builder
                    actor
                    source
                    effect
                    instruction.Then
                    (path + "/then")
                    dependency
                    requirements
                    triggerContext

                inspectProgram
                    catalog
                    builder
                    actor
                    source
                    effect
                    instruction.Otherwise
                    (path + "/otherwise")
                    optionalDependency
                    requirements
                    triggerContext

    let inspectChoices
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (effect: EffectId)
        (program: BlokemonEffectInstruction array)
        (triggerContext: TriggerContext voption)
        =
        let requirements = ResizeArray<ChoiceRequirement>()

        inspectProgram
            catalog
            builder
            actor
            source
            effect
            program
            "root"
            ValueNone
            requirements
            triggerContext

        ImmutableArray.CreateRange(requirements |> Seq.distinctBy (fun value -> value.Id))

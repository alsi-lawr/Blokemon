namespace Blokemon.Game

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.ChoiceShapes
open Blokemon.Game.PokemonPowers
open Blokemon.Game.VintageChoices

/// Working out what an effect has to ask before it can run, without running any of it.
module internal ChoiceInspection =

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
        =
        let candidatesOf () =
            resolveCandidates catalog builder actor source instruction

        if
            VintageChoices.inspect
                catalog
                builder
                actor
                source
                effect
                instruction
                path
                dependency
                requirements
        then
            ()
        else

            if
                instruction.Opcode = BlokemonOpcode.ModifySoftSpot
                && instruction.Selection = BlokemonSelection.Chosen
                && instruction.MechanicalTypes.Length > 1
                && candidatesOf () |> Seq.exists (hasEffectiveSoftSpot catalog builder)
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

            if
                instruction.Opcode = BlokemonOpcode.CopyAttack
                || (instruction.Opcode = BlokemonOpcode.RestrictAttack
                    && instruction.Selection = BlokemonSelection.Chosen)
            then
                let opponent = builder.Other actor

                let effects =
                    (match builder.Oche opponent with
                     | ValueSome card -> Seq.singleton card
                     | ValueNone -> Seq.empty)
                    |> Seq.collect (effectiveAttacks catalog builder)
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
        =
        for index in 0 .. program.Length - 1 do
            let instruction = program[index]
            let path = $"{parentPath}/{index}"

            let dependency = optionalDependency

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

            let skipBranches =
                instruction.Opcode = BlokemonOpcode.BeerMatToss
                || instruction.Opcode = BlokemonOpcode.RepeatUntilBlankSide
                || (instruction.Opcode = BlokemonOpcode.MoveCards && instruction.Then.Length > 0)
                || instruction.Opcode = BlokemonOpcode.Conditional

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

    let inspectChoices
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (effect: EffectId)
        (program: BlokemonEffectInstruction array)
        =
        let requirements = ResizeArray<ChoiceRequirement>()

        inspectProgram catalog builder actor source effect program "root" ValueNone requirements

        ImmutableArray.CreateRange(requirements |> Seq.distinctBy (fun value -> value.Id))

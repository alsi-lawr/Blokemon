namespace Blokemon.Game

open System
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting

/// How a resolved candidate set narrows to the cards an instruction actually acts on, and how the
/// authority's value sources read off the live state.
module internal EffectSelection =

    let private choiceCards
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        (candidates: CardState array)
        : CardState seq =
        match runtime.CardsChoice(choiceId runtime.Effect path "cards") with
        | ValueSome values ->
            runtime.LastSelectedCards <- values
            values |> Seq.map runtime.Builder.Card
        | ValueNone ->
            if runtime.LastSelectedCards.Length > 0 then
                runtime.LastSelectedCards
                |> Seq.map runtime.Builder.Card
                |> Seq.filter (fun card -> Array.contains card candidates)
            elif instruction.Selection = BlokemonSelection.UpTo then
                Seq.empty
            else
                candidates |> Seq.truncate instruction.TargetCount

    let resolveSelectedTargets
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        : CardState seq =
        let candidates =
            resolveCandidates
                catalog
                runtime.Builder
                runtime.Actor
                runtime.Source
                instruction
                runtime.TriggerContext
            |> Seq.toArray

        if instruction.Selection = BlokemonSelection.BeerMat && runtime.BadgeSides = 0 then
            Seq.empty
        else
            match instruction.Selection with
            | BlokemonSelection.Chosen
            | BlokemonSelection.OtherSideChosen
            | BlokemonSelection.UpTo -> choiceCards runtime instruction path candidates
            | BlokemonSelection.SeededRandom ->
                candidates
                |> Seq.sortBy (fun _ -> runtime.Builder.Random.NextInt Int32.MaxValue)
                |> Seq.truncate instruction.TargetCount
            | BlokemonSelection.AnyDistribution ->
                match runtime.CardsChoice(choiceId runtime.Effect path "cards") with
                | ValueSome values -> values |> Seq.map runtime.Builder.Card
                | ValueNone -> candidates
            | BlokemonSelection.All when instruction.Opcode = BlokemonOpcode.SearchStack ->
                candidates |> Seq.truncate instruction.Amount
            | BlokemonSelection.All ->
                candidates
                |> Seq.truncate (
                    if instruction.TargetCount > 1 then
                        instruction.TargetCount
                    else
                        candidates.Length
                )
            | BlokemonSelection.Top -> candidates |> Seq.truncate instruction.Amount
            | _ -> candidates

    let resolveValue (runtime: EffectRuntime) (instruction: BlokemonEffectInstruction) =
        let builder = runtime.Builder

        let other () =
            builder.Oche(builder.Other runtime.Actor)

        match instruction.ValueSource with
        | BlokemonValueSource.Fixed -> 1
        | BlokemonValueSource.PrintedDamage -> instruction.Amount
        | BlokemonValueSource.SelfDamageCounters -> (builder.Card runtime.Source.Id).Damage / 10
        | BlokemonValueSource.OtherOcheDamageCounters ->
            match other () with
            | ValueSome card -> card.Damage / 10
            | ValueNone -> 0
        | BlokemonValueSource.OtherBoothCount ->
            builder.CardsIn(builder.Other runtime.Actor, CardZone.Booth) |> Seq.length
        | BlokemonValueSource.OwnAttachedVim ->
            attachedVim builder runtime.Source.Id
            |> Seq.filter (fun vim ->
                instruction.MechanicalTypes.Length = 0
                || Array.contains
                    (runtime.Catalog.Vim vim.MechanicalId).MechanicalType
                    instruction.MechanicalTypes)
            |> Seq.length
        | BlokemonValueSource.OtherAttachedVim ->
            match other () with
            | ValueSome card -> attachedVim builder card.Id |> Seq.length
            | ValueNone -> 0
        | BlokemonValueSource.BadgeSides -> runtime.BadgeSides
        | BlokemonValueSource.CardsChuckedByEffect -> runtime.CardsChucked
        | BlokemonValueSource.KitCardsInOtherMitt ->
            builder.CardsIn(builder.Other runtime.Actor, CardZone.Mitt)
            |> Seq.filter (fun card -> card.Kind = CardKind.Kit)
            |> Seq.length
        | BlokemonValueSource.QualifyingChuckedCards -> runtime.QualifyingChuckedCards
        | _ ->
            max
                0
                (instruction.Amount
                 - (builder.CardsIn(runtime.Actor, CardZone.Mitt) |> Seq.length))

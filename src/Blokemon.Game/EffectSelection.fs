namespace Blokemon.Game

open System
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.PokemonPowers

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
            resolveCandidates catalog runtime.Builder runtime.Actor runtime.Source instruction
            |> Seq.toArray

        if instruction.Selection = BlokemonSelection.BeerMat && runtime.BadgeSides = 0 then
            Seq.empty
        else
            match instruction.Selection with
            | BlokemonSelection.Chosen
            | BlokemonSelection.OtherSideChosen
            | BlokemonSelection.UpTo -> choiceCards runtime instruction path candidates
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
        | BlokemonValueSource.OwnBoothCount ->
            builder.CardsIn(runtime.Actor, CardZone.Booth) |> Seq.length
        | BlokemonValueSource.OtherAttachedVim ->
            match other () with
            | ValueSome card -> attachedVim runtime.Catalog builder card.Id |> Seq.length
            | ValueNone -> 0
        | BlokemonValueSource.BadgeSides -> runtime.BadgeSides
        | BlokemonValueSource.ExtraTypedEnergy ->
            let energyType = instruction.MechanicalTypes |> Array.tryHead

            match energyType, runtime.Catalog.Attack runtime.Effect with
            | Some selectedType, ValueSome attack ->
                let transforming =
                    hasActivePower runtime.Catalog builder runtime.Source BlokemonOpcode.Transform

                let attached =
                    (builder.Card runtime.Source.Id).Attachments
                    |> Seq.map builder.Card
                    |> Seq.collect (effectiveEnergy runtime.Catalog builder runtime.Source)
                    |> Seq.filter (fun value -> transforming || value = selectedType)
                    |> Seq.length

                let required =
                    if transforming then
                        attack.VimCost.Length
                    else
                        attack.VimCost
                        |> Seq.filter (fun value -> value = selectedType)
                        |> Seq.length

                min instruction.TargetCount (max 0 (attached - required))
            | _ -> 0
        | BlokemonValueSource.NamedPokemonInPlay ->
            builder.Cards
            |> Seq.filter (fun card ->
                card.Owner = runtime.Actor
                && isInPlay card
                && instruction.RelatedIds |> Array.contains card.MechanicalId.Value)
            |> Seq.length
        | unsupported -> invalidOp $"Unsupported value source {int unsupported}."

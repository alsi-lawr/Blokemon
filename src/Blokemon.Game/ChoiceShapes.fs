namespace Blokemon.Game

open System
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting

/// Which instructions own the card choice they read, and whether a set of cards is allowed to
/// repeat a mechanical type. Both are asked while inspecting and again while validating.
module internal ChoiceShapes =

    let instructionOwnsCardChoice (instruction: BlokemonEffectInstruction) =
        if
            instruction.Opcode = BlokemonOpcode.MoveCards
            && instruction.Destination <> BlokemonEffectDestination.Unspecified
            && not (hasDeclaredSources instruction)
        then
            false
        elif
            instruction.Selection <> BlokemonSelection.Chosen
            && instruction.Selection <> BlokemonSelection.OtherSideChosen
            && instruction.Selection <> BlokemonSelection.UpTo
        then
            instruction.Opcode = BlokemonOpcode.ChuckCards
            && Array.contains BlokemonTarget.OtherMitt instruction.Targets
        else
            match instruction.Opcode with
            | BlokemonOpcode.DealBoothDamage
            | BlokemonOpcode.PlaceDamageCounters
            | BlokemonOpcode.HealDamage
            | BlokemonOpcode.ApplyRoughState
            | BlokemonOpcode.SearchStack
            | BlokemonOpcode.MoveCards
            | BlokemonOpcode.ChuckCards
            | BlokemonOpcode.AttachVim
            | BlokemonOpcode.MoveVim
            | BlokemonOpcode.ChuckVim
            | BlokemonOpcode.SwapOche
            | BlokemonOpcode.SendHome
            | BlokemonOpcode.TransformFromStack -> true
            | _ -> false

    let haveDifferentMechanicalTypes
        (cards: FrozenList<CardInstanceId>)
        (requirement: ChoiceRequirement)
        =
        let used = Collections.Generic.HashSet<BlokemonMechanicalType>()

        let rec check (remaining: CardInstanceId list) =
            match remaining with
            | [] -> true
            | cardId :: rest ->
                match
                    requirement.EligibleCardTypes |> Seq.tryFind (fun value -> value.Card = cardId)
                with
                | None -> false
                | Some types when types.Types.Count = 0 -> false
                | Some types when types.Types |> Seq.exists used.Contains -> false
                | Some types ->
                    used.UnionWith types.Types
                    check rest

        check (List.ofSeq cards)

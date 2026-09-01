namespace Blokemon.Game

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting

/// Which instructions own the card choice they read, and whether a set of cards is allowed to
/// repeat a mechanical type. Both are asked while inspecting and again while validating.
module internal ChoiceShapes =

    let choiceAcceptsDependents choice =
        match choice with
        | EffectChoice.Amount(_, amount) -> amount <> 0
        | EffectChoice.Cards(_, cards) -> cards.Length <> 0
        | EffectChoice.Attachments(_, placements) -> placements.Length <> 0
        | EffectChoice.MechanicalType _
        | EffectChoice.Attack _ -> true

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
            | BlokemonOpcode.HealDamage
            | BlokemonOpcode.ApplyRoughState
            | BlokemonOpcode.SearchStack
            | BlokemonOpcode.MoveCards
            | BlokemonOpcode.ChuckCards
            | BlokemonOpcode.ChuckVim
            | BlokemonOpcode.SwapOche -> true
            | _ -> false

    let haveDifferentMechanicalTypes
        (cards: ImmutableArray<CardInstanceId>)
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
                | Some types when types.Types.Length = 0 -> false
                | Some types when types.Types |> Seq.exists used.Contains -> false
                | Some types ->
                    used.UnionWith types.Types
                    check rest

        check (List.ofSeq cards)

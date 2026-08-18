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

/// Every opcode that never re-enters the program runner. Keeping them here leaves the recursive
/// core small enough to read in one go; ValueNone means the instruction belongs to that core.
module internal EffectInstructions =

    let executeSimple
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        : bool voption =
        let builder = runtime.Builder

        let selectedTargets () =
            resolveSelectedTargets catalog runtime instruction path |> Seq.toArray

        let register kind =
            registerEffect catalog runtime instruction kind ValueNone

        match instruction.Opcode with
        | BlokemonOpcode.DealPrintedDamage ->
            addPendingDamage catalog runtime instruction path instruction.Amount DamageKind.Attack

            ValueSome true
        | BlokemonOpcode.AdjustDamage ->
            adjustPendingDamage runtime (instruction.Amount * resolveValue runtime instruction)
            ValueSome true
        | BlokemonOpcode.ScaleDamage ->
            executeScaleDamage catalog runtime instruction path
            ValueSome true
        | BlokemonOpcode.DealBoothDamage ->
            addPendingDamage
                catalog
                runtime
                instruction
                path
                instruction.Amount
                DamageKind.BoothAttack

            ValueSome true
        | BlokemonOpcode.PlaceDamageCounters ->
            if not runtime.DeferringEndRound then
                executePlacedCounters catalog runtime instruction path

            ValueSome true
        | BlokemonOpcode.DealSelfDamage ->
            runtime.PendingOtherDamage.Add
                { Target = runtime.Source.Id
                  Amount = instruction.Amount
                  Kind = DamageKind.SelfDamage }

            ValueSome true
        | BlokemonOpcode.HealDamage ->
            if not runtime.DeferringEndRound then
                for target in selectedTargets () do
                    builder.Heal(
                        runtime.Actor,
                        target.Id,
                        instruction.Amount,
                        ValueSome runtime.Source.Id
                    )

            ValueSome true
        | BlokemonOpcode.ApplyRoughState ->
            for target in selectedTargets () do
                if not (effectIsPrevented runtime target) then
                    for state in instruction.RoughStates do
                        builder.ApplyRoughState(
                            runtime.Actor,
                            target.Id,
                            state,
                            ValueSome runtime.Source.Id
                        )

            ValueSome true
        | BlokemonOpcode.ClearRoughState ->
            for target in selectedTargets () do
                builder.ClearRoughStates(runtime.Actor, target.Id)

            ValueSome true
        | BlokemonOpcode.DrawFromStack ->
            executeDraw runtime instruction
            ValueSome true
        | BlokemonOpcode.SearchStack ->
            let selected = selectedTargets ()

            let selected =
                if
                    runtime.BeerMatGateParent = ValueSome(parentPath path)
                    && runtime.TossCount = instruction.Amount
                then
                    selected |> Array.truncate runtime.BadgeSides
                else
                    selected

            runtime.LastSelectedCards <-
                ImmutableArray.CreateRange(selected |> Array.map (fun card -> card.Id))

            runtime.HasCardSelection <- true

            moveCardsToDestination
                catalog
                runtime
                (runtime.LastSelectedCards |> Seq.map builder.Card)
                instruction.Destination
            |> ignore

            ValueSome true
        | BlokemonOpcode.ShuffleStack ->
            let stackOwner =
                if
                    Array.contains BlokemonTarget.OtherStack instruction.Targets
                    || (match instruction.Sources with
                        | null -> false
                        | sources -> Array.contains BlokemonTarget.OtherStack sources)
                then
                    builder.Other runtime.Actor
                else
                    runtime.Actor

            builder.Shuffle(
                stackOwner,
                if runtime.HasCardSelection then
                    runtime.LastSelectedCards
                else
                    ImmutableArray<_>.Empty
            )

            ValueSome true
        | BlokemonOpcode.RevealCards ->
            if not runtime.HasCardSelection then
                runtime.LastSelectedCards <-
                    ImmutableArray.CreateRange(
                        selectedTargets () |> Array.map (fun card -> card.Id)
                    )

                runtime.HasCardSelection <- true

            builder.Events.Add
                { PendingMatchEvent.forCards
                      MatchEventKind.CardsRevealed
                      runtime.Actor
                      runtime.Source.Id
                      runtime.LastSelectedCards with
                    Effect = ValueSome runtime.Effect }

            ValueSome true
        | BlokemonOpcode.ChuckCards ->
            executeChuckCards catalog runtime instruction path
            ValueSome true
        | BlokemonOpcode.AttachVim ->
            if
                (match catalog.PartyTrick runtime.Effect with
                 | ValueSome trick -> trick.Trigger <> BlokemonTrigger.Continuous
                 | ValueNone -> true)
            then
                executeAttachVim catalog runtime instruction path

            ValueSome true
        | BlokemonOpcode.MoveVim ->
            executeMoveVim catalog runtime instruction path
            ValueSome true
        | BlokemonOpcode.ChuckVim ->
            executeChuckVim catalog runtime instruction path
            ValueSome true
        | BlokemonOpcode.SwapOche ->
            executeSwap catalog runtime instruction path
            ValueSome true
        | BlokemonOpcode.PreventDamage ->
            register TemporaryEffectKind.PreventDamage
            ValueSome true
        | BlokemonOpcode.PreventEffects ->
            register TemporaryEffectKind.PreventEffects
            ValueSome true
        | BlokemonOpcode.ReduceDamage ->
            register TemporaryEffectKind.ReduceDamage
            ValueSome true
        | BlokemonOpcode.ModifyAttackCost ->
            register TemporaryEffectKind.ModifyAttackCost
            ValueSome true
        | BlokemonOpcode.ModifyTaxiFare ->
            register TemporaryEffectKind.ModifyTaxiFare
            ValueSome true
        | BlokemonOpcode.ModifyStayingPower ->
            register TemporaryEffectKind.ModifyStayingPower
            ValueSome true
        | BlokemonOpcode.ModifySoftSpot ->
            if not runtime.IsAttack || instruction.MechanicalTypes.Length > 1 then
                registerEffect
                    catalog
                    runtime
                    instruction
                    TemporaryEffectKind.ModifySoftSpot
                    (ValueSome path)

            ValueSome true
        | BlokemonOpcode.IgnoreStubbornStreak ->
            runtime.IgnoreStubbornStreak <- true
            ValueSome true
        | BlokemonOpcode.IgnoreSoftSpotAndStubbornStreak ->
            runtime.IgnoreSoftSpot <- true
            runtime.IgnoreStubbornStreak <- true
            ValueSome true
        | BlokemonOpcode.RestrictAttack ->
            register (
                if instruction.Selection = BlokemonSelection.BeerMat then
                    TemporaryEffectKind.RestrictAttackOnBeerMat
                else
                    TemporaryEffectKind.RestrictAttack
            )

            ValueSome true
        | BlokemonOpcode.RestrictTaxi ->
            register TemporaryEffectKind.RestrictTaxi
            ValueSome true
        | BlokemonOpcode.RestrictKit ->
            register TemporaryEffectKind.RestrictKit
            ValueSome true
        | BlokemonOpcode.RestrictLocal ->
            register TemporaryEffectKind.RestrictLocal
            ValueSome true
        | BlokemonOpcode.RestrictEmptiesRecovery ->
            register TemporaryEffectKind.RestrictEmptiesRecovery
            ValueSome true
        | BlokemonOpcode.ForceBeerMatBlank ->
            registerPlayerEffect runtime TemporaryEffectKind.ForceBeerMatBlank
            ValueSome true
        | BlokemonOpcode.ReflectAttackDamage ->
            register TemporaryEffectKind.ReflectAttackDamage
            ValueSome true
        | _ -> ValueNone

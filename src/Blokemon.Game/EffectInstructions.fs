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
open Blokemon.Game.VintageEffects
open Blokemon.Game.PokemonPowers

/// Every opcode that never re-enters the program runner. Keeping them here leaves the recursive
/// core small enough to read in one go; ValueNone means the instruction belongs to that core.
module internal EffectInstructions =

    let private executeLegacySimple
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
        | BlokemonOpcode.DealSelfDamage ->
            runtime.PendingOtherDamage.Add
                { Target = runtime.Source.Id
                  Amount = instruction.Amount
                  Kind = DamageKind.SelfDamage }

            ValueSome true
        | BlokemonOpcode.HealDamage ->
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
                if
                    not (effectIsPrevented runtime target)
                    && not (
                        hasActivePower catalog runtime.Builder target BlokemonOpcode.ThickSkinned
                    )
                then
                    for state in instruction.RoughStates do
                        builder.ApplyRoughState(
                            runtime.Actor,
                            target.Id,
                            state,
                            ValueSome runtime.Source.Id
                        )

                        if state = BlokemonRoughState.DodgyPint && instruction.Amount > 1 then
                            builder.AddEffect
                                { SourceEffect = runtime.Effect
                                  SourceCard = runtime.Source.Id
                                  Owner = runtime.Actor
                                  TargetCard = ValueSome target.Id
                                  Kind = TemporaryEffectKind.EnhancedPoison
                                  Amount = instruction.Amount * 10
                                  MechanicalTypes = ImmutableArray<_>.Empty
                                  RoughStates = ImmutableArray<_>.Empty
                                  RelatedCards = ImmutableArray<_>.Empty
                                  Conditions = ImmutableArray<_>.Empty
                                  Duration = EffectDuration.WhileTargetInPlay
                                  AppliesFromRound = builder.RoundNumber
                                  ExpiresAfterRound = System.Int32.MaxValue }

            ValueSome true
        | BlokemonOpcode.ClearRoughState ->
            for target in selectedTargets () do
                builder.ClearRoughStates(runtime.Actor, target.Id)

                for effect in
                    builder.Effects
                    |> Seq.filter (fun effect ->
                        effect.TargetCard = ValueSome target.Id
                        && effect.Kind = TemporaryEffectKind.EnhancedPoison)
                    |> Seq.toArray do
                    builder.RemoveEffect effect

            ValueSome true
        | BlokemonOpcode.DrawFromStack ->
            executeDraw runtime instruction
            ValueSome true
        | BlokemonOpcode.SearchStack ->
            let boothCapacity =
                remainingBoothCapacity catalog runtime.Builder runtime.Actor instruction.Destination

            if boothCapacity <> ValueSome 0 then
                let selected = selectedTargets ()

                let selected =
                    if
                        runtime.BeerMatGateParent = ValueSome(parentPath path)
                        && runtime.TossCount = instruction.Amount
                    then
                        selected |> Array.truncate runtime.BadgeSides
                    else
                        selected

                let selected =
                    match boothCapacity with
                    | ValueSome capacity -> selected |> Array.truncate capacity
                    | ValueNone -> selected

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

            builder.Shuffle stackOwner

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
        | BlokemonOpcode.ModifyTaxiFare ->
            register TemporaryEffectKind.ModifyTaxiFare
            ValueSome true
        | BlokemonOpcode.ModifySoftSpot ->
            let targetHasWeakness =
                resolveCandidates catalog runtime.Builder runtime.Actor runtime.Source instruction
                |> Seq.exists (hasEffectiveSoftSpot catalog runtime.Builder)

            if
                targetHasWeakness
                && (not runtime.IsAttack || instruction.MechanicalTypes.Length > 1)
            then
                registerEffect
                    catalog
                    runtime
                    instruction
                    TemporaryEffectKind.ModifySoftSpot
                    (ValueSome path)

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
        | BlokemonOpcode.ReflectAttackDamage -> ValueSome true
        | _ -> ValueNone

    let executeSimple
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        : bool voption =
        match execute catalog runtime instruction path with
        | ValueSome handled -> ValueSome handled
        | ValueNone -> executeLegacySimple catalog runtime instruction path

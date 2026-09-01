namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.EffectSelection

/// Registering the lasting effects an instruction leaves behind, and the guard that decides
/// whether an opponent's protection stops one landing at all.
module internal EffectRegistration =

    let effectIsPrevented (runtime: EffectRuntime) (target: CardState) =
        runtime.Builder.Effects
        |> Seq.exists (fun effect ->
            effect.TargetCard = ValueSome target.Id
            && effect.Owner <> runtime.Actor
            && effect.Kind = TemporaryEffectKind.PreventEffects
            && (if runtime.IsAttack then
                    (runtime.Builder.Card effect.SourceCard).Kind = CardKind.Bloke
                else
                    not runtime.IsHouseRule
                    && (runtime.Builder.Card effect.SourceCard).Kind = CardKind.Kit))

    let private durationFor (runtime: EffectRuntime) (kind: TemporaryEffectKind) =
        if kind = TemporaryEffectKind.ModifySoftSpot && runtime.IsAttack then
            EffectDuration.WhileTargetInPlay
        elif kind = TemporaryEffectKind.ModifySoftSpot then
            EffectDuration.WhileSourceInPlay
        else
            EffectDuration.UntilEndOfOpponentsNextRound

    let registerEffect
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (kind: TemporaryEffectKind)
        (path: string voption)
        =
        if
            instruction.Selection = BlokemonSelection.BeerMat
            && runtime.BadgeSides = 0
            && kind <> TemporaryEffectKind.RestrictAttackOnBeerMat
        then
            ()
        else

            let usesAttachedToolTarget =
                runtime.IsHouseRule
                && kind = TemporaryEffectKind.ModifySoftSpot
                && instruction.Targets.Length = 0
                && not (hasDeclaredSources instruction)
                && runtime.Source.Kind = CardKind.Kit
                && runtime.Source.Zone = CardZone.Attached
                && runtime.Source.AttachedTo.IsSome

            let mutable targets =
                (if usesAttachedToolTarget then
                     Seq.empty
                 else
                     match path with
                     | ValueNone ->
                         resolveCandidates
                             catalog
                             runtime.Builder
                             runtime.Actor
                             runtime.Source
                             instruction
                     | ValueSome path -> resolveSelectedTargets catalog runtime instruction path)
                |> Seq.filter isInPlay
                |> Seq.filter (fun target -> not (effectIsPrevented runtime target))
                |> Seq.toArray

            if targets.Length = 0 then
                match (runtime.Builder.Card runtime.Source.Id).AttachedTo with
                | ValueSome attachedTo -> targets <- [| runtime.Builder.Card attachedTo |]
                | ValueNone -> ()

            if targets.Length = 0 && isInPlay (runtime.Builder.Card runtime.Source.Id) then
                targets <- [| runtime.Builder.Card runtime.Source.Id |]

            let mutable abandoned = false

            if
                kind = TemporaryEffectKind.ModifyTaxiFare
                && instruction.MechanicalTypes.Length > 0
                && (match catalog.PartyTrick runtime.Effect with
                    | ValueSome trick -> trick.Trigger = BlokemonTrigger.Continuous
                    | ValueNone -> false)
            then
                targets <-
                    targets
                    |> Array.filter (fun target ->
                        attachedVim catalog runtime.Builder target.Id
                        |> Seq.exists (fun vim ->
                            Array.contains
                                (catalog.Vim vim.MechanicalId).MechanicalType
                                instruction.MechanicalTypes))

                abandoned <- targets.Length = 0

            if not abandoned then
                let duration =
                    if
                        kind = TemporaryEffectKind.RestrictAttack
                        && instruction.RelatedIds.Length > 0
                        && targets |> Array.exists (fun target -> target.Id = runtime.Source.Id)
                    then
                        EffectDuration.WhileSourceInPlay
                    else
                        durationFor runtime kind

                // DefaultIfEmpty: an effect with no card target still registers, owned by nobody.
                let places =
                    if targets.Length = 0 then
                        [| ValueNone |]
                    else
                        targets |> Array.map (fun target -> ValueSome target.Id)

                for target in places do
                    let mechanicalTypes =
                        match path with
                        | ValueSome path ->
                            match runtime.TypeChoice(choiceId runtime.Effect path "type") with
                            | ValueSome selected -> [| selected |]
                            | ValueNone -> instruction.MechanicalTypes
                        | ValueNone -> instruction.MechanicalTypes

                    runtime.Builder.AddEffect
                        { SourceEffect = runtime.Effect
                          SourceCard = runtime.Source.Id
                          Owner = runtime.Actor
                          TargetCard = target
                          Kind = kind
                          Amount = instruction.Amount
                          MechanicalTypes = ImmutableArray.CreateRange mechanicalTypes
                          RoughStates = ImmutableArray.CreateRange instruction.RoughStates
                          RelatedCards =
                            if
                                kind = TemporaryEffectKind.RestrictAttack
                                && instruction.Selection = BlokemonSelection.Chosen
                                && path.IsSome
                            then
                                match
                                    runtime.AttackChoice(
                                        choiceId runtime.Effect path.Value "attack"
                                    )
                                with
                                | ValueSome selected ->
                                    ImmutableArray.Create(MechanicalCardId selected.Value)
                                | ValueNone -> ImmutableArray<_>.Empty
                            else
                                ImmutableArray.CreateRange(
                                    instruction.RelatedIds |> Array.map MechanicalCardId
                                )
                          Conditions =
                            ImmutableArray.CreateRange(
                                instruction.Predicates
                                |> Array.map (fun predicate -> predicate.Condition)
                            )
                          Duration = duration
                          AppliesFromRound = runtime.Builder.RoundNumber
                          ExpiresAfterRound = runtime.Builder.RoundNumber + 2 }

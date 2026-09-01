namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.EffectSelection
open Blokemon.Game.EffectDamage
open Blokemon.Game.EffectRegistration

/// Swapping Active Pokemon, playing a Doll or Fossil, and resolving scaled damage.
module internal EffectCardTransforms =

    let executeSwap
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        let incoming =
            runtime.LastSelectedCards
            |> Seq.map runtime.Builder.Card
            |> Seq.tryFind (fun card -> card.Zone = CardZone.Booth)
            |> Option.orElseWith (fun () ->
                resolveSelectedTargets catalog runtime instruction path |> Seq.tryHead)

        match incoming with
        | Some incoming when catalog.CountsAsPokemon incoming && incoming.Zone = CardZone.Booth ->
            match runtime.Builder.Oche incoming.Owner with
            | ValueSome outgoing when
                catalog.CountsAsPokemon outgoing && not (effectIsPrevented runtime outgoing)
                ->
                resolvePendingDamageFor catalog runtime outgoing.Id
                let outgoing = runtime.Builder.Card outgoing.Id

                runtime.Builder.ExchangeCards(
                    outgoing.Id,
                    CardZone.Booth,
                    incoming.Id,
                    CardZone.Oche,
                    (fun () ->
                        runtime.Builder.ClearRoughStates(runtime.Actor, outgoing.Id)
                        runtime.Builder.RemoveEffectsFor(outgoing.Id, true))
                )

                // A card moving is not a public event - every draw and every discard is one, and a
                // log of them is a log of the engine rather than of the game. So a swap says so
                // itself, or the Blokemon standing opposite is replaced with nothing anywhere
                // accounting for it. The pair is ordered arriving first and leaving second, which
                // is the order the sentence downstream reads them in.
                runtime.Builder.Events.Add
                    { PendingMatchEvent.forCards
                          MatchEventKind.OcheSwapped
                          incoming.Owner
                          runtime.Source.Id
                          (ImmutableArray.Create(incoming.Id, outgoing.Id)) with
                        Effect = ValueSome runtime.Effect }
            | _ -> ()
        | _ -> ()

    let playAsBloke (runtime: EffectRuntime) =
        let source = runtime.Builder.Card runtime.Source.Id

        if source.Zone = CardZone.Mitt then
            let zone =
                if (runtime.Builder.Oche runtime.Actor).IsNone then
                    CardZone.Oche
                else
                    CardZone.Booth

            if
                zone = CardZone.Booth
                && (runtime.Builder.CardsIn(runtime.Actor, CardZone.Booth) |> Seq.length)
                   >= runtime.Catalog.Manifest.BaseRules.Opening.BoothLimit
            then
                runtime.Rejection <- ValueSome CommandRejectionCode.RuleLimitReached
            else
                runtime.Builder.MoveCard(source.Id, zone)

    let executeScaleDamage
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        if
            instruction.ValueSource = BlokemonValueSource.Fixed
            && (instruction.Selection = BlokemonSelection.BeerMat
                || instruction.Selection = BlokemonSelection.UntilBlankSide)
        then
            ()
        elif
            instruction.ValueSource = BlokemonValueSource.Fixed
            && instruction.Selection = BlokemonSelection.All
        then
            if runtime.IsAttack then
                runtime.Builder.AddEffect
                    { SourceEffect = runtime.Effect
                      SourceCard = runtime.Source.Id
                      Owner = runtime.Actor
                      TargetCard = ValueSome runtime.Source.Id
                      Kind = TemporaryEffectKind.ScaleNextAttackDamage
                      Amount = instruction.Amount
                      MechanicalTypes = ImmutableArray<_>.Empty
                      RoughStates = ImmutableArray<_>.Empty
                      RelatedCards = ImmutableArray<_>.Empty
                      Conditions = ImmutableArray<_>.Empty
                      Duration = EffectDuration.UntilEndOfOpponentsNextRound
                      AppliesFromRound = runtime.Builder.RoundNumber + 2
                      ExpiresAfterRound = runtime.Builder.RoundNumber + 2 }
            else
                registerEffect
                    catalog
                    runtime
                    instruction
                    TemporaryEffectKind.ScaleNextAttackDamage
                    ValueNone
        else
            let damage = instruction.Amount * resolveValue runtime instruction
            addPendingDamage catalog runtime instruction path damage DamageKind.Attack

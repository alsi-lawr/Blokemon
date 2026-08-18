namespace Blokemon.Game

open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.EffectSelection
open Blokemon.Game.EffectDamage
open Blokemon.Game.EffectRegistration

/// Turning a card into something else, or changing what is already on it: swap, demote, transform,
/// the placed counters and the scaled damage.
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
        | Some incoming when incoming.Zone = CardZone.Booth ->
            match runtime.Builder.Oche incoming.Owner with
            | ValueSome outgoing when not (effectIsPrevented runtime outgoing) ->
                resolvePendingDamageFor catalog runtime outgoing.Id
                let outgoing = runtime.Builder.Card outgoing.Id
                runtime.Builder.MoveCard(outgoing.Id, CardZone.Booth)
                runtime.Builder.ClearRoughStates(runtime.Actor, outgoing.Id)
                runtime.Builder.RemoveEffectsFor(outgoing.Id, true)
                runtime.Builder.MoveCard(incoming.Id, CardZone.Oche)
            | _ -> ()
        | _ -> ()

    let demote (catalog: AuthorityCatalog) (runtime: EffectRuntime) (target: CardState) =
        if target.UnderlyingCards.Count > 0 then
            resolvePendingDamageFor catalog runtime target.Id
            let target = runtime.Builder.Card target.Id
            let underlyingId = target.UnderlyingCards[target.UnderlyingCards.Count - 1]
            let underlying = runtime.Builder.Card underlyingId
            runtime.Builder.RemoveEffectsFor target.Id
            runtime.Builder.MoveCard(target.Id, CardZone.Mitt)

            runtime.Builder.SetCard
                { runtime.Builder.Card target.Id with
                    AttachedTo = ValueNone
                    Attachments = FrozenList.empty
                    UnderlyingCards = FrozenList.empty
                    Damage = 0
                    RoughStates = FrozenList.empty }

            runtime.Builder.SetCard
                { underlying with
                    Zone = target.Zone
                    Damage = target.Damage
                    Attachments = target.Attachments
                    UnderlyingCards =
                        FrozenList<CardInstanceId>
                            .Create(
                                target.UnderlyingCards
                                |> Seq.truncate (target.UnderlyingCards.Count - 1)
                            )
                    AttachedTo = ValueNone }

            for attachmentId in target.Attachments do
                let attachment = runtime.Builder.Card attachmentId

                runtime.Builder.SetCard
                    { attachment with
                        AttachedTo = ValueSome underlyingId }

    let executeTransform
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        let replacement =
            runtime.LastSelectedCards
            |> Seq.map runtime.Builder.Card
            |> Seq.tryFind (fun card -> card.Zone = CardZone.Stack)
            |> Option.orElseWith (fun () ->
                resolveSelectedTargets catalog runtime instruction path |> Seq.tryHead)

        match replacement with
        | Some replacement when replacement.Zone = CardZone.Stack ->
            let source = runtime.Builder.Card runtime.Source.Id
            runtime.Builder.MoveCard(source.Id, CardZone.EmptiesTray)

            runtime.Builder.SetCard
                { replacement with
                    Zone = source.Zone
                    Damage = source.Damage
                    EnteredAtOwnerRound = (runtime.Builder.Player runtime.Actor).RoundsStarted }
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

    let executePlacedCounters
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        if instruction.Selection = BlokemonSelection.AnyDistribution then
            if
                resolveCandidates
                    catalog
                    runtime.Builder
                    runtime.Actor
                    runtime.Source
                    instruction
                    ValueNone
                |> Seq.isEmpty
                |> not
            then
                match runtime.DistributionChoice(choiceId runtime.Effect path "distribution") with
                | ValueNone -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired
                | ValueSome allocations ->
                    for allocation in allocations do
                        if
                            not (effectIsPrevented runtime (runtime.Builder.Card allocation.Card))
                        then
                            runtime.PendingOtherDamage.Add
                                { Target = allocation.Card
                                  Amount = allocation.Counters * 10
                                  Kind = DamageKind.PlacedCounter }
        else
            for target in resolveSelectedTargets catalog runtime instruction path |> Seq.toArray do
                if not (effectIsPrevented runtime target) then
                    runtime.PendingOtherDamage.Add
                        { Target = target.Id
                          Amount = instruction.Amount * 10
                          Kind = DamageKind.PlacedCounter }

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
                      MechanicalTypes = FrozenList.empty
                      RoughStates = FrozenList.empty
                      RelatedCards = FrozenList.empty
                      Conditions = FrozenList.empty
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

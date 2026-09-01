namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectSelection
open Blokemon.Game.PokemonPowers

/// Staging, ordering and placing the damage an effect deals: soft spots, stubborn streaks,
/// prevention, reduction and reflection, applied in the printed order.
module internal EffectDamage =

    /// Whether an effect that names a rank applies to this attacker and this target.
    let effectMatchesAttack
        (_: AuthorityCatalog)
        (_: TemporaryEffect)
        (_: CardState)
        (_: CardState)
        =
        true

    /// Whether an effect that names a rank applies to this card, without an attacker in play.
    let effectMatchesCardRank (_: AuthorityCatalog) (_: TemporaryEffect) (_: CardState) = true

    let private applyOutgoingAttackDamage
        (runtime: EffectRuntime)
        (target: CardState)
        (kind: DamageKind)
        (damage: int)
        =
        let plusPower =
            if kind <> DamageKind.Attack || target.Zone <> CardZone.Oche then
                0
            else
                runtime.Builder.Effects
                |> Seq.filter (fun effect ->
                    effect.Owner = runtime.Actor
                    && effect.TargetCard = ValueSome runtime.Source.Id
                    && effect.Kind = TemporaryEffectKind.AttachedTrainer)
                |> Seq.sumBy (fun effect -> max 0 effect.Amount)

        damage
        + plusPower
        + (runtime.Builder.Effects
           |> Seq.filter (fun effect ->
               effect.Owner = runtime.Actor
               && effect.TargetCard = ValueSome runtime.Source.Id
               && effect.Kind = TemporaryEffectKind.ScaleNextAttackDamage
               && effect.AppliesFromRound <= runtime.Builder.RoundNumber
               && (effect.RelatedCards.Length = 0
                   || effect.RelatedCards
                      |> Seq.exists (fun related -> related.Value = runtime.Effect.Value)))
           |> Seq.sumBy (fun effect -> effect.Amount))

    let private applyAttackProtection
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (target: CardState)
        (damage: int)
        =
        let targetEffects =
            runtime.Builder.Effects
            |> Seq.filter (fun effect ->
                effect.TargetCard = ValueSome target.Id
                && effectMatchesAttack catalog effect runtime.Source target
                && (effect.Kind <> TemporaryEffectKind.ReduceDamageFromAttacker
                    || effect.SourceCard = runtime.Source.Id))
            |> Seq.toArray

        if
            targetEffects
            |> Array.exists (fun effect ->
                effect.Kind = TemporaryEffectKind.PreventDamage
                || effect.Kind = TemporaryEffectKind.PreventEffects)
        then
            0
        else
            damage
            - (targetEffects
               |> Array.filter (fun effect -> effect.Kind = TemporaryEffectKind.ReduceDamage)
               |> Array.append (
                   targetEffects
                   |> Array.filter (fun effect ->
                       effect.Kind = TemporaryEffectKind.ReduceDamageFromAttacker)
               )
               |> Array.sumBy (fun effect -> effect.Amount))

    let private applyTrainerEffects
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (target: CardState)
        (damage: int)
        =
        applyAttackProtection catalog runtime target damage

    let private applyPokemonPowerProtection
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (target: CardState)
        (damage: int)
        =
        if
            hasActivePower catalog runtime.Builder target BlokemonOpcode.InvisibleWall
            && damage >= 30
        then
            0
        elif hasActivePower catalog runtime.Builder target BlokemonOpcode.KabutoArmor then
            damage / 20 * 10
        else
            damage

    let private applyAttackEffectProtection
        (runtime: EffectRuntime)
        (target: CardState)
        (damage: int)
        =
        if
            runtime.Builder.Effects
            |> Seq.exists (fun effect ->
                effect.TargetCard = ValueSome target.Id
                && effect.Kind = TemporaryEffectKind.PreventDamageUpTo
                && damage <= effect.Amount)
        then
            0
        else
            damage

    let private applySoftSpot
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (target: CardState)
        (damage: int)
        =
        if target.Kind <> CardKind.Bloke then
            damage
        else
            let attackerTypes =
                effectiveMechanicalTypes
                    catalog
                    runtime.Builder
                    (runtime.Builder.Card runtime.Source.Id)

            let softSpotEffects =
                runtime.Builder.Effects
                |> Seq.filter (fun effect ->
                    effect.TargetCard = ValueSome target.Id
                    && effect.Kind = TemporaryEffectKind.ModifySoftSpot
                    && effectMatchesAttack catalog effect runtime.Source target)
                |> Seq.toArray

            let chosenSoftSpot =
                softSpotEffects
                |> Array.tryFindBack (fun effect -> effect.MechanicalTypes.Length > 0)

            let effectiveSoftSpots =
                if
                    softSpotEffects
                    |> Array.exists (fun effect ->
                        effect.Amount = 1 && effect.MechanicalTypes.Length = 0)
                then
                    ImmutableArray<_>.Empty
                else
                    match chosenSoftSpot with
                    | Some effect -> effect.MechanicalTypes
                    | None ->
                        ImmutableArray.CreateRange(
                            (effectiveBloke catalog runtime.Builder target).SoftSpots
                            |> Array.map (fun softSpot -> softSpot.MechanicalType)
                        )

            if effectiveSoftSpots |> Seq.exists (fun value -> Seq.contains value attackerTypes) then
                damage
                * (if softSpotEffects |> Array.exists (fun effect -> effect.Amount = 4) then
                       4
                   else
                       2)
            else
                damage

    let private applyStubbornStreak
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (target: CardState)
        (damage: int)
        =
        if target.Kind <> CardKind.Bloke then
            damage
        else
            let attackerTypes =
                effectiveMechanicalTypes
                    catalog
                    runtime.Builder
                    (runtime.Builder.Card runtime.Source.Id)

            let changedResistance =
                runtime.Builder.Effects
                |> Seq.tryFindBack (fun effect ->
                    effect.TargetCard = ValueSome target.Id
                    && effect.Kind = TemporaryEffectKind.ChangeResistance)

            let stubborn =
                match changedResistance with
                | Some effect ->
                    effect.MechanicalTypes
                    |> Seq.map (fun value ->
                        { MechanicalType = value
                          Modifier = "-30" })
                    |> Seq.toArray
                | None -> (effectiveBloke catalog runtime.Builder target).StubbornStreaks

            if
                stubborn
                |> Array.exists (fun streak -> Seq.contains streak.MechanicalType attackerTypes)
            then
                damage - 30
            else
                damage

    let applyAttackDamageOrder
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (target: CardState)
        (kind: DamageKind)
        (damage: int)
        (placeResolved: (int -> unit) voption)
        =
        let mutable resolved = damage
        let mutable stoppedAtZero = false

        for step in catalog.Manifest.BaseRules.DamageOrder do
            runtime.ResolutionTrace(DamageStep step)

            resolved <-
                match step with
                | BlokemonDamageResolutionStep.PrintedOrProgramBaseDamage -> resolved
                | BlokemonDamageResolutionStep.EffectsOnAttackingBloke ->
                    applyOutgoingAttackDamage runtime target kind resolved
                | BlokemonDamageResolutionStep.StopWhenDamageIsZero ->
                    stoppedAtZero <- resolved = 0
                    resolved
                | BlokemonDamageResolutionStep.Weakness when
                    kind = DamageKind.Attack && not stoppedAtZero
                    ->
                    applySoftSpot catalog runtime target resolved
                | BlokemonDamageResolutionStep.Weakness -> resolved
                | BlokemonDamageResolutionStep.Resistance when
                    kind = DamageKind.Attack && not stoppedAtZero
                    ->
                    applyStubbornStreak catalog runtime target resolved
                    |> applyAttackEffectProtection runtime target
                | BlokemonDamageResolutionStep.Resistance -> resolved
                | BlokemonDamageResolutionStep.TrainerEffects when not stoppedAtZero ->
                    applyTrainerEffects catalog runtime target resolved
                | BlokemonDamageResolutionStep.TrainerEffects -> resolved
                | BlokemonDamageResolutionStep.PokemonPowers when not stoppedAtZero ->
                    applyPokemonPowerProtection catalog runtime target resolved
                | BlokemonDamageResolutionStep.PokemonPowers -> resolved
                | BlokemonDamageResolutionStep.PlaceDamageCounters ->
                    let clamped = max 0 resolved

                    match placeResolved with
                    | ValueSome place -> place clamped
                    | ValueNone -> ()

                    clamped
                | BlokemonDamageResolutionStep.EffectsAfterDamage -> resolved
                | unsupported ->
                    invalidOp $"Unsupported validated damage-resolution step {unsupported}."

        resolved

    let private addReflectedDamage (runtime: EffectRuntime) (target: CardState) (damage: int) =
        let rec reflectedAmount (program: BlokemonEffectInstruction array) =
            program
            |> Seq.tryPick (fun instruction ->
                if instruction.Opcode = BlokemonOpcode.ReflectAttackDamage then
                    Some instruction.Amount
                else
                    reflectedAmount instruction.Then
                    |> Option.orElseWith (fun () -> reflectedAmount instruction.Otherwise))

        let amount =
            if
                hasActivePower
                    runtime.Catalog
                    runtime.Builder
                    target
                    BlokemonOpcode.ReflectAttackDamage
            then
                effectivePartyTricks runtime.Catalog runtime.Builder target
                |> Seq.tryPick (fun trick -> reflectedAmount trick.Program)
            else
                None

        match amount with
        | Some reflected ->
            runtime.PendingOtherDamage.Add
                { Target = runtime.Source.Id
                  Amount = if reflected > 0 then reflected else damage
                  Kind = DamageKind.PlacedCounter }
        | None -> ()

    let pendingSendsHome (catalog: AuthorityCatalog) (runtime: EffectRuntime) =
        runtime.PendingAttackDamage
        |> Seq.exists (fun pending ->
            let target = runtime.Builder.Card pending.Target

            let damage =
                if pending.Kind = DamageKind.Attack then
                    applyAttackDamageOrder
                        catalog
                        runtime
                        target
                        pending.Kind
                        pending.Amount
                        ValueNone
                elif pending.Kind = DamageKind.BoothAttack then
                    applyAttackDamageOrder
                        catalog
                        runtime
                        target
                        pending.Kind
                        pending.Amount
                        ValueNone
                else
                    pending.Amount

            damage + target.Damage >= effectiveStayingPower catalog runtime.Builder target)

    let addPendingDamage
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        (amount: int)
        (kind: DamageKind)
        =
        for target in resolveSelectedTargets catalog runtime instruction path |> Seq.toArray do
            let resolvedKind =
                if kind = DamageKind.BoothAttack && target.Zone = CardZone.Oche then
                    DamageKind.Attack
                else
                    kind

            let existing =
                runtime.PendingAttackDamage.FindIndex(fun damage -> damage.Target = target.Id)

            if existing >= 0 then
                let pending = runtime.PendingAttackDamage[existing]

                runtime.PendingAttackDamage[existing] <-
                    { pending with
                        Amount = pending.Amount + amount }
            else
                runtime.PendingAttackDamage.Add
                    { Target = target.Id
                      Amount = amount
                      Kind = resolvedKind }

    let adjustPendingDamage (runtime: EffectRuntime) (amount: int) =
        if runtime.PendingAttackDamage.Count = 0 then
            match runtime.Builder.Oche(runtime.Builder.Other runtime.Actor) with
            | ValueSome other ->
                runtime.PendingAttackDamage.Add
                    { Target = other.Id
                      Amount = amount
                      Kind = DamageKind.Attack }
            | ValueNone -> ()
        else
            for index in 0 .. runtime.PendingAttackDamage.Count - 1 do
                let pending = runtime.PendingAttackDamage[index]

                runtime.PendingAttackDamage[index] <-
                    { pending with
                        Amount = pending.Amount + amount }

    let private place
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (pending: PendingDamage)
        (damage: int)
        =
        let target = runtime.Builder.Card pending.Target

        runtime.Builder.PlaceDamage(
            runtime.Actor,
            target.Id,
            damage,
            pending.Kind,
            ValueSome runtime.Source.Id
        )

        if damage > 0 then
            runtime.AttackDamageTargets.Add target.Id |> ignore
            addReflectedDamage runtime target damage

        runtime.ResolvedAttackDamage[target.Id] <- damage

    let resolveDamage (catalog: AuthorityCatalog) (runtime: EffectRuntime) =
        for pending in runtime.PendingAttackDamage |> Seq.toArray do
            let target = runtime.Builder.Card pending.Target

            match pending.Kind with
            | DamageKind.Attack
            | DamageKind.BoothAttack ->
                applyAttackDamageOrder
                    catalog
                    runtime
                    target
                    pending.Kind
                    pending.Amount
                    (ValueSome(place catalog runtime pending))
                |> ignore
            | _ -> place catalog runtime pending pending.Amount

        for pending in runtime.PendingOtherDamage |> Seq.toArray do
            let target = runtime.Builder.Card pending.Target

            let amount =
                if pending.Kind = DamageKind.SelfDamage then
                    applyAttackProtection catalog runtime target pending.Amount |> max 0
                else
                    pending.Amount

            runtime.Builder.PlaceDamage(
                runtime.Actor,
                pending.Target,
                amount,
                pending.Kind,
                ValueSome runtime.Source.Id
            )

    let resolvePendingDamageFor
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (targetId: CardInstanceId)
        =
        for pending in
            runtime.PendingAttackDamage
            |> Seq.filter (fun damage -> damage.Target = targetId)
            |> Seq.toArray do
            let target = runtime.Builder.Card pending.Target

            match pending.Kind with
            | DamageKind.Attack
            | DamageKind.BoothAttack ->
                applyAttackDamageOrder
                    catalog
                    runtime
                    target
                    pending.Kind
                    pending.Amount
                    (ValueSome(place catalog runtime pending))
                |> ignore
            | _ -> place catalog runtime pending pending.Amount

            runtime.PendingAttackDamage.Remove pending |> ignore

        for pending in
            runtime.PendingOtherDamage
            |> Seq.filter (fun damage -> damage.Target = targetId)
            |> Seq.toArray do
            runtime.Builder.PlaceDamage(
                runtime.Actor,
                pending.Target,
                pending.Amount,
                pending.Kind,
                ValueSome runtime.Source.Id
            )

            runtime.PendingOtherDamage.Remove pending |> ignore

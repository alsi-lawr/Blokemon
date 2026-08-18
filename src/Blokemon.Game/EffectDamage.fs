namespace Blokemon.Game

open Blokemon.Core.SetDesign
open Blokemon.Game.EffectSelection

/// Staging, ordering and placing the damage an effect deals: soft spots, stubborn streaks,
/// prevention, reduction and reflection, applied in the printed order.
module internal EffectDamage =

    let private rankConditions =
        [ BlokemonCondition.TargetIsRegular
          BlokemonCondition.TargetIsSeasoned
          BlokemonCondition.TargetIsLandlord ]

    let private matchesTargetRank
        (catalog: AuthorityCatalog)
        (effect: TemporaryEffect)
        (target: CardState)
        =
        if target.Kind <> CardKind.Bloke then
            not (
                effect.Conditions
                |> Seq.exists (fun condition -> List.contains condition rankConditions)
            )
        else
            let rank = (catalog.Bloke target.MechanicalId).Rank

            (not (Seq.contains BlokemonCondition.TargetIsRegular effect.Conditions)
             || rank = BlokemonRank.Regular)
            && (not (Seq.contains BlokemonCondition.TargetIsSeasoned effect.Conditions)
                || rank = BlokemonRank.Seasoned)
            && (not (Seq.contains BlokemonCondition.TargetIsLandlord effect.Conditions)
                || rank = BlokemonRank.Landlord)

    /// Whether an effect that names a rank applies to this attacker and this target.
    let effectMatchesAttack
        (catalog: AuthorityCatalog)
        (effect: TemporaryEffect)
        (attacker: CardState)
        (target: CardState)
        =
        if
            Seq.contains BlokemonCondition.SourceIsRegular effect.Conditions
            && (attacker.Kind <> CardKind.Bloke
                || (catalog.Bloke attacker.MechanicalId).Rank <> BlokemonRank.Regular)
        then
            false
        else
            matchesTargetRank catalog effect target

    /// Whether an effect that names a rank applies to this card, without an attacker in play.
    let effectMatchesCardRank
        (catalog: AuthorityCatalog)
        (effect: TemporaryEffect)
        (card: CardState)
        =
        matchesTargetRank catalog effect card

    let private applyOutgoingAttackDamage (runtime: EffectRuntime) (damage: int) =
        damage
        + (runtime.Builder.Effects
           |> Seq.filter (fun effect ->
               effect.Owner = runtime.Actor
               && effect.TargetCard = ValueSome runtime.Source.Id
               && effect.Kind = TemporaryEffectKind.ScaleNextAttackDamage
               && effect.AppliesFromRound <= runtime.Builder.RoundNumber)
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
                && effectMatchesAttack catalog effect runtime.Source target)
            |> Seq.toArray

        if
            targetEffects
            |> Array.exists (fun effect -> effect.Kind = TemporaryEffectKind.PreventDamage)
        then
            0
        else
            max
                0
                (damage
                 - (targetEffects
                    |> Array.filter (fun effect -> effect.Kind = TemporaryEffectKind.ReduceDamage)
                    |> Array.sumBy (fun effect -> effect.Amount)))

    let applyAttackDamageOrder
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (target: CardState)
        (damage: int)
        =
        let mutable damage = applyOutgoingAttackDamage runtime damage

        if not runtime.IgnoreSoftSpot && target.Kind = CardKind.Bloke then
            let attackerTypes = catalog.MechanicalTypes(runtime.Builder.Card runtime.Source.Id)

            let softSpotEffects =
                runtime.Builder.Effects
                |> Seq.filter (fun effect ->
                    effect.TargetCard = ValueSome target.Id
                    && effect.Kind = TemporaryEffectKind.ModifySoftSpot
                    && effectMatchesAttack catalog effect runtime.Source target)
                |> Seq.toArray

            let chosenSoftSpot =
                softSpotEffects
                |> Array.tryFindBack (fun effect -> effect.MechanicalTypes.Count > 0)

            let effectiveSoftSpots =
                match chosenSoftSpot with
                | Some effect -> effect.MechanicalTypes
                | None ->
                    FrozenList<BlokemonMechanicalType>
                        .Create(
                            (catalog.Bloke target.MechanicalId).SoftSpots
                            |> Array.map (fun softSpot -> softSpot.MechanicalType)
                        )

            if effectiveSoftSpots |> Seq.exists (fun value -> Seq.contains value attackerTypes) then
                damage <-
                    damage
                    * (if softSpotEffects |> Array.exists (fun effect -> effect.Amount = 4) then
                           4
                       else
                           2)

        if not runtime.IgnoreStubbornStreak && target.Kind = CardKind.Bloke then
            let attackerTypes = catalog.MechanicalTypes(runtime.Builder.Card runtime.Source.Id)
            let stubborn = (catalog.Bloke target.MechanicalId).StubbornStreaks

            if
                stubborn
                |> Array.exists (fun streak -> Seq.contains streak.MechanicalType attackerTypes)
            then
                damage <- damage - 30

        applyAttackProtection catalog runtime target damage

    let private addReflectedDamage (runtime: EffectRuntime) (target: CardState) (damage: int) =
        if
            runtime.Builder.Effects
            |> Seq.exists (fun effect ->
                effect.TargetCard = ValueSome target.Id
                && effect.Owner <> runtime.Actor
                && effect.Kind = TemporaryEffectKind.ReflectAttackDamage
                && effect.AppliesFromRound <= runtime.Builder.RoundNumber)
        then
            runtime.PendingOtherDamage.Add
                { Target = runtime.Source.Id
                  Amount = damage
                  Kind = DamageKind.PlacedCounter }

    let pendingSendsHome (catalog: AuthorityCatalog) (runtime: EffectRuntime) =
        runtime.PendingAttackDamage
        |> Seq.exists (fun pending ->
            let target = runtime.Builder.Card pending.Target

            let damage =
                if pending.Kind = DamageKind.Attack then
                    applyAttackDamageOrder catalog runtime target pending.Amount
                else
                    pending.Amount

            let stayingPower =
                catalog.StayingPower target
                + (runtime.Builder.Effects
                   |> Seq.filter (fun effect ->
                       effect.TargetCard = ValueSome target.Id
                       && effect.Kind = TemporaryEffectKind.ModifyStayingPower
                       && effectMatchesAttack catalog effect runtime.Source target)
                   |> Seq.sumBy (fun effect -> effect.Amount))

            damage + target.Damage >= stayingPower)

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
        let damage = max 0 damage

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

    let resolveDamage (catalog: AuthorityCatalog) (runtime: EffectRuntime) =
        for pending in runtime.PendingAttackDamage |> Seq.toArray do
            let target = runtime.Builder.Card pending.Target

            let damage =
                match pending.Kind with
                | DamageKind.Attack -> applyAttackDamageOrder catalog runtime target pending.Amount
                | DamageKind.BoothAttack ->
                    applyAttackProtection
                        catalog
                        runtime
                        target
                        (applyOutgoingAttackDamage runtime pending.Amount)
                | _ -> pending.Amount

            place catalog runtime pending damage

        for pending in runtime.PendingOtherDamage |> Seq.toArray do
            runtime.Builder.PlaceDamage(
                runtime.Actor,
                pending.Target,
                pending.Amount,
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

            let damage =
                match pending.Kind with
                | DamageKind.Attack -> applyAttackDamageOrder catalog runtime target pending.Amount
                | DamageKind.BoothAttack ->
                    applyAttackProtection catalog runtime target pending.Amount
                | _ -> pending.Amount

            place catalog runtime pending damage
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

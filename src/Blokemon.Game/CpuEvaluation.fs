namespace Blokemon.Game

open System
open System.Linq

module internal CpuEvaluation =

    let private sameEnergyBurnEffect (left: TemporaryEffect) (right: TemporaryEffect) =
        left.Kind = TemporaryEffectKind.EnergyBurn
        && right.Kind = TemporaryEffectKind.EnergyBurn
        && left.SourceEffect = right.SourceEffect
        && left.SourceCard = right.SourceCard
        && left.Owner = right.Owner
        && left.TargetCard = right.TargetCard
        && left.Amount = right.Amount
        && Enumerable.SequenceEqual(left.MechanicalTypes, right.MechanicalTypes)
        && Enumerable.SequenceEqual(left.RoughStates, right.RoughStates)
        && Enumerable.SequenceEqual(left.RelatedCards, right.RelatedCards)
        && Enumerable.SequenceEqual(left.Conditions, right.Conditions)
        && left.Duration = right.Duration
        && left.AppliesFromRound = right.AppliesFromRound
        && left.ExpiresAfterRound = right.ExpiresAfterRound

    let private energyBurnGroups (effects: TemporaryEffect seq) =
        let groups = ResizeArray<TemporaryEffect * int>()

        for effect in effects do
            if effect.Kind = TemporaryEffectKind.EnergyBurn then
                match
                    groups
                    |> Seq.tryFindIndex (fun (identity, _) -> sameEnergyBurnEffect identity effect)
                with
                | Some index ->
                    let identity, count = groups[index]
                    groups[index] <- identity, count + 1
                | None -> groups.Add(effect, 1)

        groups

    let private increasedDuplicateEnergyBurn (before: MatchState) (after: MatchState) =
        let beforeGroups = energyBurnGroups before.Effects

        energyBurnGroups after.Effects
        |> Seq.exists (fun (identity, afterCount) ->
            beforeGroups
            |> Seq.tryFind (fun (candidate, _) -> sameEnergyBurnEffect candidate identity)
            |> Option.exists (fun (_, beforeCount) -> beforeCount > 0 && afterCount > beforeCount))

    let private isInPlay (card: CardState) =
        card.Zone = CardZone.Oche || card.Zone = CardZone.Booth

    let private isActorCard actor (card: CardState) = card.Owner = actor

    let private attackPotential
        (catalog: AuthorityCatalog)
        (readyAttacks: Map<CardInstanceId, int>)
        (card: CardState)
        =
        if not (catalog.CountsAsPokemon card) then
            0
        else
            let attacks = catalog.Attacks card |> Seq.toArray
            let readyDamage = readyAttacks |> Map.tryFind card.Id |> Option.defaultValue 0

            let futureDamage =
                attacks |> Seq.map _.PrintedDamage |> Seq.append (Seq.singleton 0) |> Seq.max

            readyDamage * 3 + futureDamage

    let private handValue (catalog: AuthorityCatalog) (card: CardState) =
        match card.Kind with
        | CardKind.Bloke ->
            let bloke = catalog.Bloke card.MechanicalId
            16 + bloke.StayingPower / 5 + (bloke.Attacks |> Seq.sumBy _.PrintedDamage) / 5
        | CardKind.Vim -> 22
        | CardKind.Kit -> 18
        | other -> failwithf "Unhandled card kind %A." other

    let private roughPenalty (catalog: AuthorityCatalog) (card: CardState) =
        card.RoughStates
        |> Seq.sumBy (fun rough ->
            let rule = catalog.RoughState rough.State

            18
            + rule.CheckupDamageCounters * 10
            + (if rule.PreventsAttack then 28 else 0)
            + (if rule.PreventsTaxi then 8 else 0))

    let private inPlayValue
        (catalog: AuthorityCatalog)
        (readyAttacks: Map<CardInstanceId, int>)
        (card: CardState)
        =
        let stayingPower = catalog.StayingPower card
        let remaining = max 0 (stayingPower - card.Damage)

        let position, stayingPowerWeight, attackWeight =
            if card.Zone = CardZone.Oche then 35, 3, 2 else 20, 2, 1

        position
        + remaining * stayingPowerWeight
        + card.Attachments.Length * 24
        + attackPotential catalog readyAttacks card * attackWeight
        - roughPenalty catalog card

    let private effectValue actor (effect: TemporaryEffect) =
        let magnitude = 8 + abs effect.Amount

        if effect.Owner = actor then magnitude else -magnitude

    let scoreState
        (catalog: AuthorityCatalog)
        (readyAttacks: Map<CardInstanceId, int>)
        (actor: PlayerId)
        (state: MatchState)
        =
        match state.Winner with
        | ValueSome winner when winner = actor -> 1_000_000
        | ValueSome _ -> -1_000_000
        | ValueNone ->
            let cards = state.Cards |> Seq.toArray
            let other = state.Other actor
            let actorState = state.Player actor
            let otherState = state.Player other

            let cardScore =
                cards
                |> Seq.sumBy (fun card ->
                    let value =
                        if isInPlay card && catalog.CountsAsPokemon card then
                            inPlayValue catalog readyAttacks card
                        elif card.Zone = CardZone.Mitt then
                            handValue catalog card
                        elif card.Zone = CardZone.Stack then
                            2
                        else
                            0

                    if isActorCard actor card then value else -value)

            let prizeScore = (otherState.BarChitsRemaining - actorState.BarChitsRemaining) * 320

            let stackSafety owner sign =
                if state.CardsIn(owner, CardZone.Stack) |> Seq.isEmpty then
                    -sign * 80
                else
                    0

            cardScore
            + prizeScore
            + stackSafety actor 1
            + stackSafety other -1
            + (state.Effects |> Seq.sumBy (effectValue actor))

    let transitionScore
        (catalog: AuthorityCatalog)
        (readyBefore: Map<CardInstanceId, int>)
        (readyAfter: Map<CardInstanceId, int>)
        (actor: PlayerId)
        (kind: LegalActionKind)
        (before: MatchState)
        (after: MatchState)
        =
        let progress =
            match kind with
            | LegalActionKind.ChooseMulliganBonus
            | LegalActionKind.ChooseOpening
            | LegalActionKind.ChooseBonusPlacement
            | LegalActionKind.ChooseReplacement
            | LegalActionKind.ResolveEffectChoice
            | LegalActionKind.ResolveKnockoutTrigger
            | LegalActionKind.ResolveBarChitTrigger -> 30
            | LegalActionKind.AttachVim
            | LegalActionKind.PlayBloke
            | LegalActionKind.Promote
            | LegalActionKind.PlayKit
            | LegalActionKind.UsePartyTrick
            | LegalActionKind.Taxi -> 12
            | LegalActionKind.Attack -> 8
            | LegalActionKind.ChuckFossil -> 0
            | LegalActionKind.EndRound -> 0
            | LegalActionKind.Resign -> -1_000_000
            | other -> failwithf "Unhandled legal action kind %A." other

        if
            kind = LegalActionKind.UsePartyTrick
            && increasedDuplicateEnergyBurn before after
        then
            // Energy Burn is printed as repeatable, so the engine must keep offering it. Once the
            // identical conversion is already active, however, another copy cannot change the
            // effective Energy and is not progress for the policy to prefer over ending the round.
            -1
        elif kind = LegalActionKind.EndRound then
            match after.Winner with
            | ValueSome winner when winner = actor -> 1_000_000
            | ValueSome _ -> -1_000_000
            | ValueNone -> 0
        else
            let afterReadiness =
                if kind = LegalActionKind.Attack then
                    readyBefore
                else
                    readyAfter

            scoreState catalog afterReadiness actor after
            - scoreState catalog readyBefore actor before
            + progress

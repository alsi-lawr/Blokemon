namespace Blokemon.Cpu

open Blokemon.Game

open System
open System.Linq

[<RequireQualifiedAccess>]
type internal CpuEvaluationKnowledge =
    | Fair of knownAtRoot: Set<CardInstanceId>
    | Authoritative

module internal CpuEvaluation =

    open CpuPolicyLimits

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

    let private increasedDuplicateEnergyBurn beforeEffects afterEffects =
        let beforeGroups = energyBurnGroups beforeEffects

        energyBurnGroups afterEffects
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

    let private scoreCards
        (catalog: AuthorityCatalog)
        (readyAttacks: Map<CardInstanceId, int>)
        (actor: PlayerId)
        (cards: CardState seq)
        =
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

    let private scoreState
        (catalog: AuthorityCatalog)
        (readyAttacks: Map<CardInstanceId, int>)
        (actor: PlayerId)
        (state: MatchState)
        =
        match state.Winner with
        | ValueSome winner when winner = actor -> 1_000_000
        | ValueSome _ -> -1_000_000
        | ValueNone ->
            let other = state.Other actor
            let actorState = state.Player actor
            let otherState = state.Player other
            let prizeScore = (otherState.BarChitsRemaining - actorState.BarChitsRemaining) * 320

            let stackSafety owner sign =
                if state.CardsIn(owner, CardZone.Stack) |> Seq.isEmpty then
                    -sign * 80
                else
                    0

            scoreCards catalog readyAttacks actor state.Cards
            + prizeScore
            + stackSafety actor 1
            + stackSafety other -1
            + (state.Effects |> Seq.sumBy (effectValue actor))

    let private scorePublicState
        (catalog: AuthorityCatalog)
        (readyAttacks: Map<CardInstanceId, int>)
        (actor: PlayerId)
        (knownAtRoot: Set<CardInstanceId>)
        (state: CpuPublicMatchState)
        =
        match state.Winner with
        | ValueSome winner when winner = actor -> 1_000_000
        | ValueSome _ -> -1_000_000
        | ValueNone ->
            let other = state.Players |> Seq.find (fun player -> player.Id <> actor) |> _.Id

            let actorState = state.Players |> Seq.find (fun player -> player.Id = actor)
            let otherState = state.Players |> Seq.find (fun player -> player.Id = other)
            let prizeScore = (otherState.BarChitsRemaining - actorState.BarChitsRemaining) * 320

            let stackCount owner =
                let known =
                    state.Cards
                    |> Seq.filter (fun card ->
                        knownAtRoot.Contains card.Id
                        && card.Owner = owner
                        && card.Zone = CardZone.Stack)
                    |> Seq.length

                let hidden =
                    state.HiddenZones
                    |> Seq.filter (fun zone -> zone.Owner = owner && zone.Zone = CardZone.Stack)
                    |> Seq.sumBy _.Count

                known + hidden

            let stackSafety owner sign =
                if stackCount owner = 0 then -sign * 80 else 0

            let visibleCards =
                state.Cards |> Seq.filter (fun card -> knownAtRoot.Contains card.Id)

            let visibleEffects =
                state.Effects
                |> Seq.filter (fun effect ->
                    knownAtRoot.Contains effect.SourceCard
                    && (effect.TargetCard.IsNone || knownAtRoot.Contains effect.TargetCard.Value))

            scoreCards catalog readyAttacks actor visibleCards
            + prizeScore
            + stackSafety actor 1
            + stackSafety other -1
            + (visibleEffects |> Seq.sumBy (effectValue actor))

    let private scoreFor
        catalog
        readyAttacks
        actor
        knowledge
        (state: MatchState)
        (observation: CpuObservation)
        =
        match knowledge with
        | CpuEvaluationKnowledge.Fair knownAtRoot ->
            scorePublicState catalog readyAttacks actor knownAtRoot observation.State
        | CpuEvaluationKnowledge.Authoritative -> scoreState catalog readyAttacks actor state

    let transitionScore
        (catalog: AuthorityCatalog)
        (readyBefore: Map<CardInstanceId, int>)
        (readyAfter: Map<CardInstanceId, int>)
        (actor: PlayerId)
        (knowledge: CpuEvaluationKnowledge)
        (kind: LegalActionKind)
        (before: MatchState)
        (beforeObservation: CpuObservation)
        (after: MatchState)
        (afterObservation: CpuObservation)
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
            && increasedDuplicateEnergyBurn
                beforeObservation.State.Effects
                afterObservation.State.Effects
        then
            // Energy Burn is printed as repeatable, so the engine must keep offering it. Once the
            // identical conversion is already active, however, another copy cannot change the
            // effective Energy and is not progress for the policy to prefer over ending the round.
            -1
        elif kind = LegalActionKind.EndRound then
            match afterObservation.State.Winner with
            | ValueSome winner when winner = actor -> 1_000_000
            | ValueSome _ -> -1_000_000
            | ValueNone -> 0
        else
            let afterReadiness =
                if kind = LegalActionKind.Attack then
                    readyBefore
                else
                    readyAfter

            scoreFor catalog afterReadiness actor knowledge after afterObservation
            - scoreFor catalog readyBefore actor knowledge before beforeObservation
            + progress

    let scoreTransition
        (engine: MatchEngine)
        (actor: PlayerId)
        (knowledge: CpuEvaluationKnowledge)
        (kind: LegalActionKind)
        (before: MatchState)
        (beforeObservation: CpuObservation)
        (after: MatchState)
        (afterObservation: CpuObservation)
        =
        let catalog = engine.CpuCatalog

        let readyAttacks (observation: CpuObservation) =
            observation.Candidates
            |> Seq.truncate rootCandidateLimit
            |> Seq.choose (fun action ->
                match action.Action with
                | MatchAction.Attack(attacker, attack) ->
                    catalog.Attack attack
                    |> ValueOption.map (fun details -> attacker, details.PrintedDamage)
                    |> ValueOption.toOption
                | _ -> None)
            |> Seq.groupBy fst
            |> Seq.map (fun (attacker, attacks) -> attacker, attacks |> Seq.map snd |> Seq.max)
            |> Map.ofSeq

        transitionScore
            catalog
            (readyAttacks beforeObservation)
            (readyAttacks afterObservation)
            actor
            knowledge
            kind
            before
            beforeObservation
            after
            afterObservation

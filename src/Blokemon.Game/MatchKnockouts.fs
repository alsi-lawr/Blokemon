namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchWins
open Blokemon.Game.MatchSendHome

/// Working through everything the damage just knocked out, in a fixed order, pausing whenever a
/// trigger needs an answer.
module internal MatchKnockouts =

    let findSendHomeCandidates
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (forcedSendHome: ImmutableArray<CardInstanceId>)
        =
        ImmutableArray.CreateRange(
            builder.Cards
            |> Seq.filter (fun card -> catalog.CountsAsPokemon card && isInPlay card)
            |> Seq.filter (fun card ->
                Seq.contains card.Id forcedSendHome
                || card.Damage >= effectiveStayingPowerAt catalog builder card)
            |> Seq.sortBy (fun card -> card.Owner, card.Id)
            |> Seq.map (fun card -> card.Id)
        )

    let resolveIdentifiedSendHome
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (identified: ImmutableArray<CardInstanceId>)
        (attackingCard: CardInstanceId voption)
        (finishRoundAfterResolution: bool)
        (attackDamageTargets: ImmutableArray<CardInstanceId>)
        =
        // Retaliation can put the attacker into the same list, so the candidates grow while they are
        // being worked through.
        let candidates =
            ResizeArray<CardState>(
                identified
                |> Seq.choose (fun cardId ->
                    match builder.FindCard cardId with
                    | ValueSome card when catalog.CountsAsPokemon card && isInPlay card ->
                        Some card
                    | _ -> None)
            )

        let mutable index = 0

        while index < candidates.Count do
            match builder.FindCard candidates[index].Id with
            | ValueSome current when isInPlay current ->
                let knockedOutByAttackDamage =
                    attackingCard.IsSome
                    && Seq.contains current.Id attackDamageTargets
                    && current.Damage >= effectiveStayingPowerAt catalog builder current

                let damageAttacker =
                    if knockedOutByAttackDamage then
                        attackingCard
                    else
                        ValueNone

                let reflected =
                    sendHomeOne
                        catalog
                        interpreter
                        builder
                        current
                        damageAttacker
                        finishRoundAfterResolution

                match (if reflected then damageAttacker else ValueNone) with
                | ValueSome attackerId ->
                    match builder.FindCard attackerId with
                    | ValueSome attackerState when isInPlay attackerState ->
                        candidates.Add attackerState
                    | _ -> ()
                | ValueNone -> ()

            | _ -> ()

            index <- index + 1

        resolveWins catalog builder ValueNone
        true

    let resolveSendHome
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (forcedSendHome: ImmutableArray<CardInstanceId>)
        (attackingCard: CardInstanceId voption)
        (finishRoundAfterResolution: bool)
        (attackDamageTargets: ImmutableArray<CardInstanceId>)
        =
        resolveIdentifiedSendHome
            catalog
            interpreter
            builder
            (findSendHomeCandidates catalog builder forcedSendHome)
            attackingCard
            finishRoundAfterResolution
            attackDamageTargets

    let private rememberMirrorMove
        (builder: MatchBuilder)
        (attacker: CardState)
        (before: CardState)
        (after: CardState)
        =
        for effect in
            builder.Effects
            |> Seq.filter (fun effect ->
                effect.TargetCard = ValueSome after.Id
                && effect.Kind = TemporaryEffectKind.MirrorMoveMemory)
            |> Seq.toArray do
            builder.RemoveEffect effect

        let newStates =
            after.RoughStates
            |> Seq.filter (fun entry ->
                before.RoughStates
                |> Seq.exists (fun previous -> previous.State = entry.State)
                |> not)
            |> Seq.map (fun entry -> entry.State)

        builder.AddEffect
            { SourceEffect = EffectId "rules:mirror-move-memory"
              SourceCard = after.Id
              Owner = after.Owner
              TargetCard = ValueSome after.Id
              Kind = TemporaryEffectKind.MirrorMoveMemory
              Amount = max 0 (after.Damage - before.Damage)
              MechanicalTypes = ImmutableArray<_>.Empty
              RoughStates = ImmutableArray.CreateRange newStates
              RelatedCards = ImmutableArray<_>.Empty
              Conditions = ImmutableArray<_>.Empty
              Duration = EffectDuration.UntilEndOfOpponentsNextRound
              AppliesFromRound = builder.RoundNumber
              ExpiresAfterRound = builder.RoundNumber + 1 }

    let resolveReactiveAttackTriggers
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (attacker: CardState)
        (defenderBefore: CardState voption)
        (damageBefore: int)
        (attackDamageTargets: ImmutableArray<CardInstanceId>)
        =
        match defenderBefore with
        | ValueNone -> ()
        | ValueSome defenderBefore ->
            match builder.FindCard defenderBefore.Id with
            | ValueSome defender -> rememberMirrorMove builder attacker defenderBefore defender

            | _ -> ()



    let resolveVoluntarySourceChuck
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (sourceBeforeExecution: CardState)
        (execution: InterpreterExecution)
        =
        if execution.SourceLeftPlay then
            if sourceBeforeExecution.Zone = CardZone.Oche then
                assignReplacement catalog builder sourceBeforeExecution.Owner

            resolveWins catalog builder ValueNone

namespace Blokemon.Game

open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchRounds
open Blokemon.Game.MatchPending

/// Declaring and resolving an attack: the beer mats the declaration has to survive, and the damage,
/// reactions and knockouts that follow it.
module internal MatchAttackHandlers =

    /// Everything the declaration itself has to survive before the attack program runs: the gate
    /// beer mats, and the Muddled toss that can cancel the attack outright.
    let private declareAttack
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (attacker: CardState)
        (attackId: EffectId)
        =
        builder.Events.Add
            { PendingMatchEvent.forCard MatchEventKind.AttackDeclared actor attacker.Id with
                Effect = ValueSome attackId }

        let attackGate =
            builder.Effects
            |> Seq.filter (fun effect ->
                effect.TargetCard = ValueSome attacker.Id
                && effect.Kind = TemporaryEffectKind.RestrictAttackOnBeerMat
                && effect.AppliesFromRound <= builder.RoundNumber)
            |> Seq.tryLast

        let gateAllowed =
            match attackGate with
            | None -> true
            | Some gate ->
                let mutable attackAllowed = true

                for _ in 1 .. gate.Amount do
                    let badge = builder.TossBeerMat actor

                    builder.Events.Add
                        { PendingMatchEvent.forCard MatchEventKind.BeerMatTossed actor attacker.Id with
                            Effect = ValueSome gate.SourceEffect
                            BadgeSide = ValueSome badge }

                    attackAllowed <- attackAllowed && badge

                attackAllowed

        if not gateAllowed then
            builder.Events.Add
                { PendingMatchEvent.forCard MatchEventKind.AttackCancelled actor attacker.Id with
                    Effect = ValueSome attackId }

            finishOrPendRound catalog interpreter builder
            false
        elif
            attacker.RoughStates
            |> Seq.exists (fun entry -> entry.State = BlokemonRoughState.Muddled)
        then
            let badge = builder.TossBeerMat actor

            builder.Events.Add
                { PendingMatchEvent.forCard MatchEventKind.BeerMatTossed actor attacker.Id with
                    Effect = ValueSome attackId
                    BadgeSide = ValueSome badge }

            if badge then
                true
            else
                builder.PlaceDamage(
                    actor,
                    attacker.Id,
                    30,
                    DamageKind.PlacedCounter,
                    ValueSome attacker.Id
                )

                builder.Events.Add
                    { PendingMatchEvent.forCard MatchEventKind.AttackCancelled actor attacker.Id with
                        Effect = ValueSome attackId }

                resolveSendHome
                    catalog
                    interpreter
                    builder
                    FrozenList.empty
                    ValueNone
                    false
                    FrozenList.empty
                    0
                |> ignore

                finishOrPendRound catalog interpreter builder
                false
        else
            true

    let attack
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (command: MatchCommand)
        (attackerId: CardInstanceId)
        (attackId: EffectId)
        (isResuming: bool)
        (attackStarted: bool)
        (beerMatResults: FrozenList<bool>)
        =
        match validatePlayingTurn builder command.Actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            match builder.FindCard attackerId, catalog.Attack attackId with
            | ValueNone, _
            | _, ValueNone -> HandlerResult.reject CommandRejectionCode.EffectNotFound
            | ValueSome attacker, ValueSome attack ->

                if
                    attacker.Owner <> command.Actor
                    || not (
                        catalog.Attacks attacker
                        |> Seq.exists (fun candidate -> candidate.MechanicalId = attackId.Value)
                    )
                    || (attacker.Zone <> CardZone.Oche
                        && not (attacker.Zone = CardZone.Booth && attack.CanBeUsedFromBench))
                    || (command.Actor = builder.OpeningPlayer
                        && (builder.Player command.Actor).RoundsStarted = 1)
                    || attacker.RoughStates
                       |> Seq.exists (fun entry ->
                           entry.State = BlokemonRoughState.NoddedOff
                           || entry.State = BlokemonRoughState.Legless)
                    || builder.Effects
                       |> Seq.exists (fun effect ->
                           effect.TargetCard = ValueSome attacker.Id
                           && effect.Kind = TemporaryEffectKind.RestrictAttack)
                then
                    HandlerResult.reject CommandRejectionCode.EffectUnavailable
                elif not (canPayAttack catalog builder attacker attack) then
                    HandlerResult.reject CommandRejectionCode.InsufficientVim
                else

                    let requirements =
                        interpreter.InspectChoices(
                            builder,
                            command.Actor,
                            attacker,
                            attackId,
                            attack.Program
                        )

                    // A requirement the opponent has to answer parks the attack before it is declared, so the
                    // declaration events only ever fire once the answers are in.
                    let deferral =
                        if isResuming then
                            ValueNone
                        else
                            match
                                interpreter.ValidateChoiceSubmission(
                                    command.Choices,
                                    requirements,
                                    command.Actor
                                )
                            with
                            | ValueSome rejection ->
                                ValueSome(HandlerResult.rejectWith rejection requirements)
                            | ValueNone ->
                                let deferred =
                                    requirements
                                    |> Seq.filter (fun requirement ->
                                        requirement.Chooser <> command.Actor)
                                    |> Seq.toArray

                                if deferred.Length = 0 then
                                    ValueNone
                                else
                                    let chooser = deferred[0].Chooser

                                    if
                                        deferred
                                        |> Array.exists (fun requirement ->
                                            requirement.Chooser <> chooser)
                                    then
                                        ValueSome(
                                            HandlerResult.rejectWith
                                                CommandRejectionCode.InvalidChoice
                                                requirements
                                        )
                                    else
                                        builder.PendingEffect <-
                                            ValueSome
                                                { Command = command
                                                  Source = attacker.Id
                                                  Effect = attackId
                                                  Chooser = chooser
                                                  Requirements =
                                                    FrozenList<ChoiceRequirement>.Create deferred
                                                  BeerMatResults = beerMatResults
                                                  AttackStarted = false }

                                        builder.Phase <- MatchPhase.AwaitingEffectChoice

                                        builder.Events.Add
                                            { PendingMatchEvent.forCard
                                                  MatchEventKind.EffectChoiceRequested
                                                  chooser
                                                  attacker.Id with
                                                Effect = ValueSome attackId }

                                        ValueSome HandlerResult.accepted

                    match deferral with
                    | ValueSome result -> result
                    | ValueNone ->

                        let defendingCard = builder.Oche(builder.Other command.Actor)

                        let defendingDamageBefore =
                            match defendingCard with
                            | ValueSome card -> card.Damage
                            | ValueNone -> 0

                        if
                            not attackStarted
                            && not (
                                declareAttack
                                    catalog
                                    interpreter
                                    builder
                                    command.Actor
                                    attacker
                                    attackId
                            )
                        then
                            HandlerResult.accepted
                        else

                            let plan =
                                interpreter.Plan(
                                    builder,
                                    command.Actor,
                                    attacker,
                                    attackId,
                                    attack.Program,
                                    command.Choices,
                                    true,
                                    false,
                                    beerMatResults
                                )

                            if not plan.IsApplied then
                                if
                                    plan.Rejection <> ValueSome CommandRejectionCode.ChoiceRequired
                                then
                                    HandlerResult.rejectWith
                                        (plan.Rejection
                                         |> ValueOption.defaultValue
                                             CommandRejectionCode.InvalidChoice)
                                        plan.Requirements
                                else
                                    pendEffect
                                        builder
                                        command
                                        attacker.Id
                                        attackId
                                        plan.Requirements
                                        beerMatResults
                                        plan.BeerMatResults
                                        true
                            else
                                let execution =
                                    interpreter.Execute(
                                        builder,
                                        command.Actor,
                                        attacker,
                                        attackId,
                                        attack.Program,
                                        command.Choices,
                                        true,
                                        false,
                                        ValueNone,
                                        beerMatResults,
                                        ValueNone
                                    )

                                if not execution.IsApplied then
                                    HandlerResult.rejectWith
                                        (execution.Rejection
                                         |> ValueOption.defaultValue
                                             CommandRejectionCode.InvalidChoice)
                                        execution.Requirements
                                else
                                    resolveReactiveAttackTriggers
                                        catalog
                                        interpreter
                                        builder
                                        attacker
                                        defendingCard
                                        defendingDamageBefore
                                        execution.AttackDamageTargets

                                    if
                                        resolveSendHome
                                            catalog
                                            interpreter
                                            builder
                                            execution.ForcedSendHome
                                            (ValueSome attacker.Id)
                                            true
                                            execution.AttackDamageTargets
                                            execution.DeferredAttackKnockoutBarChits
                                    then
                                        finishOrPendRound catalog interpreter builder

                                    HandlerResult.accepted

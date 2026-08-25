namespace Blokemon.Game

open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchRounds
open Blokemon.Game.MatchPending

/// Declaring and resolving an attack: the beer mats the declaration has to survive, and the damage,
/// reactions and knockouts that follow it.
module internal MatchAttackHandlers =

    let private cancelAttack
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (attacker: CardState)
        (attackId: EffectId)
        =
        builder.Events.Add
            { PendingMatchEvent.forCard MatchEventKind.AttackCancelled actor attacker.Id with
                Effect = ValueSome attackId }

        finishOrPendRound catalog interpreter builder

    let private attackGateAllows (builder: MatchBuilder) (actor: PlayerId) (attacker: CardState) =
        let attackGate =
            builder.Effects
            |> Seq.filter (fun effect ->
                effect.TargetCard = ValueSome attacker.Id
                && effect.Kind = TemporaryEffectKind.RestrictAttackOnBeerMat
                && effect.AppliesFromRound <= builder.RoundNumber)
            |> Seq.tryLast

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

    let private muddledCheckAllows
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (attacker: CardState)
        (attackId: EffectId)
        =
        let beforeAttack =
            attacker.RoughStates
            |> Seq.map (fun entry -> catalog.RoughState entry.State)
            |> Seq.tryFind (fun rule ->
                rule.BeforeAttackBeerMat.HasValue && rule.BeforeAttackBeerMat.Value)

        match beforeAttack with
        | Some rule ->
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
                    rule.BlankSideCancelsAndSelfDamageCounters.GetValueOrDefault() * 10,
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
                    ImmutableArray<_>.Empty
                    ValueNone
                    false
                    ImmutableArray<_>.Empty
                    0
                |> ignore

                finishOrPendRound catalog interpreter builder
                false
        | None -> true

    let private deferOpponentChoices
        (builder: MatchBuilder)
        (command: MatchCommand)
        (attacker: CardState)
        (attackId: EffectId)
        (requirements: ImmutableArray<ChoiceRequirement>)
        (beerMatResults: ImmutableArray<bool>)
        =
        let deferred =
            requirements
            |> Seq.filter (fun requirement -> requirement.Chooser <> command.Actor)
            |> Seq.toArray

        if deferred.Length = 0 then
            ValueNone
        else
            let chooser = deferred[0].Chooser

            if deferred |> Array.exists (fun requirement -> requirement.Chooser <> chooser) then
                ValueSome(HandlerResult.rejectWith CommandRejectionCode.InvalidChoice requirements)
            else
                builder.PendingEffect <-
                    ValueSome
                        { Command = command
                          Source = attacker.Id
                          Effect = attackId
                          Chooser = chooser
                          Requirements = ImmutableArray.CreateRange deferred
                          BeerMatResults = beerMatResults
                          AttackStarted = true }

                builder.Phase <- MatchPhase.AwaitingEffectChoice

                builder.Events.Add
                    { PendingMatchEvent.forCard
                          MatchEventKind.EffectChoiceRequested
                          chooser
                          attacker.Id with
                        Effect = ValueSome attackId }

                ValueSome HandlerResult.accepted

    let finishAttackResolution
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        =
        for step in catalog.Manifest.BaseRules.AttackOrder do
            if step = BlokemonAttackResolutionStep.EndRound then
                interpreter.ResolutionTrace(AttackStep step)
                finishOrPendRound catalog interpreter builder

    let attack
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (command: MatchCommand)
        (attackerId: CardInstanceId)
        (attackId: EffectId)
        (isResuming: bool)
        (attackStarted: bool)
        (beerMatResults: ImmutableArray<bool>)
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
                    || (not catalog.Manifest.BaseRules.Opening.OpeningParticipantMayAttack
                        && command.Actor = builder.OpeningPlayer
                        && (builder.Player command.Actor).RoundsStarted = 1)
                    || attacker.RoughStates
                       |> Seq.exists (fun entry -> (catalog.RoughState entry.State).PreventsAttack)
                    || builder.Effects
                       |> Seq.exists (fun effect ->
                           effect.TargetCard = ValueSome attacker.Id
                           && effect.Kind = TemporaryEffectKind.RestrictAttack)
                then
                    HandlerResult.reject CommandRejectionCode.EffectUnavailable
                elif not (canPayAttack catalog builder attacker attack) then
                    HandlerResult.reject CommandRejectionCode.InsufficientVim
                else
                    let defendingCard = builder.Oche(builder.Other command.Actor)

                    let defendingDamageBefore =
                        match defendingCard with
                        | ValueSome card -> card.Damage
                        | ValueNone -> 0

                    let firstStep =
                        if isResuming && attackStarted then
                            BlokemonAttackResolutionStep.PayOrPerformUseRequirements
                        else
                            BlokemonAttackResolutionStep.ValidateDeclaredAttackAndVim

                    let mutable reachedFirstStep = false
                    let mutable continueResolution = true
                    let mutable result = HandlerResult.accepted
                    let mutable execution = ValueNone
                    let mutable attackDamageTargets = ImmutableArray<_>.Empty
                    let mutable sendHomeCandidates = ImmutableArray<_>.Empty
                    let mutable sendHomeResolved = true

                    for step in catalog.Manifest.BaseRules.AttackOrder do
                        if step = firstStep then
                            reachedFirstStep <- true

                        if reachedFirstStep && continueResolution then
                            interpreter.ResolutionTrace(AttackStep step)

                            match step with
                            | BlokemonAttackResolutionStep.ValidateDeclaredAttackAndVim ->
                                builder.Events.Add
                                    { PendingMatchEvent.forCard
                                          MatchEventKind.AttackDeclared
                                          command.Actor
                                          attacker.Id with
                                        Effect = ValueSome attackId }
                            | BlokemonAttackResolutionStep.ApplyEffectsThatAlterOrCancelAttack ->
                                if not (attackGateAllows builder command.Actor attacker) then
                                    cancelAttack
                                        catalog
                                        interpreter
                                        builder
                                        command.Actor
                                        attacker
                                        attackId

                                    continueResolution <- false
                            | BlokemonAttackResolutionStep.ResolveMuddledCheck ->
                                if
                                    not (
                                        muddledCheckAllows
                                            catalog
                                            interpreter
                                            builder
                                            command.Actor
                                            attacker
                                            attackId
                                    )
                                then
                                    continueResolution <- false
                            | BlokemonAttackResolutionStep.MakeRequiredChoices ->
                                let requirements =
                                    interpreter.InspectChoices(
                                        builder,
                                        command.Actor,
                                        attacker,
                                        attackId,
                                        attack.Program
                                    )

                                match
                                    interpreter.ValidateChoiceSubmission(
                                        command.Choices,
                                        requirements,
                                        command.Actor
                                    )
                                with
                                | ValueSome rejection ->
                                    result <- HandlerResult.rejectWith rejection requirements
                                    continueResolution <- false
                                | ValueNone ->
                                    match
                                        deferOpponentChoices
                                            builder
                                            command
                                            attacker
                                            attackId
                                            requirements
                                            beerMatResults
                                    with
                                    | ValueSome deferred ->
                                        result <- deferred
                                        continueResolution <- false
                                    | ValueNone -> ()
                            | BlokemonAttackResolutionStep.PayOrPerformUseRequirements ->
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
                                        plan.Rejection
                                        <> ValueSome CommandRejectionCode.ChoiceRequired
                                    then
                                        result <-
                                            HandlerResult.rejectWith
                                                (plan.Rejection
                                                 |> ValueOption.defaultValue
                                                     CommandRejectionCode.InvalidChoice)
                                                plan.Requirements
                                    else
                                        result <-
                                            pendEffect
                                                builder
                                                command
                                                attacker.Id
                                                attackId
                                                plan.Requirements
                                                beerMatResults
                                                plan.BeerMatResults
                                                true

                                    continueResolution <- false
                            | BlokemonAttackResolutionStep.ApplyBeforeDamageEffects ->
                                let prepared =
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
                                        ValueNone,
                                        deferAttackDamage = true
                                    )

                                if prepared.IsApplied then
                                    execution <- ValueSome prepared
                                else
                                    result <-
                                        HandlerResult.rejectWith
                                            (prepared.Rejection
                                             |> ValueOption.defaultValue
                                                 CommandRejectionCode.InvalidChoice)
                                            prepared.Requirements

                                    continueResolution <- false
                            | BlokemonAttackResolutionStep.CalculateAndPlaceDamage ->
                                match execution with
                                | ValueSome prepared ->
                                    attackDamageTargets <- prepared.CompleteAttackDamage.Value()
                                | ValueNone ->
                                    result <-
                                        HandlerResult.reject CommandRejectionCode.AuthorityMismatch

                                    continueResolution <- false
                            | BlokemonAttackResolutionStep.ResolveOtherEffects ->
                                resolveReactiveAttackTriggers
                                    catalog
                                    interpreter
                                    builder
                                    attacker
                                    defendingCard
                                    defendingDamageBefore
                                    attackDamageTargets
                            | BlokemonAttackResolutionStep.CheckAllSentHome ->
                                match execution with
                                | ValueSome prepared ->
                                    sendHomeCandidates <-
                                        findSendHomeCandidates
                                            catalog
                                            builder
                                            prepared.ForcedSendHome
                                | ValueNone ->
                                    result <-
                                        HandlerResult.reject CommandRejectionCode.AuthorityMismatch

                                    continueResolution <- false
                            | BlokemonAttackResolutionStep.TakeBarChitsAndPromote ->
                                match execution with
                                | ValueSome prepared ->
                                    sendHomeResolved <-
                                        resolveIdentifiedSendHome
                                            catalog
                                            interpreter
                                            builder
                                            sendHomeCandidates
                                            (ValueSome attacker.Id)
                                            true
                                            attackDamageTargets
                                            prepared.DeferredAttackKnockoutBarChits

                                    if not sendHomeResolved then
                                        continueResolution <- false
                                | ValueNone ->
                                    result <-
                                        HandlerResult.reject CommandRejectionCode.AuthorityMismatch

                                    continueResolution <- false
                            | BlokemonAttackResolutionStep.EndRound ->
                                if
                                    sendHomeResolved
                                    && catalog.Manifest.BaseRules.Round.AttackEndsRound
                                then
                                    finishOrPendRound catalog interpreter builder
                            | unsupported ->
                                invalidOp
                                    $"Unsupported validated attack-resolution step {unsupported}."

                    result

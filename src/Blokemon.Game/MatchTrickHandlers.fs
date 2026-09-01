namespace Blokemon.Game

open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchRounds
open Blokemon.Game.MatchPending
open Blokemon.Game.PokemonPowers
open Blokemon.Game.MatchSendHome
open Blokemon.Game.MatchWins

/// Using an activated party trick or a once-per-round local house rule: one printed program run
/// straight off a card that is already on the table.
module internal MatchTrickHandlers =

    let usePartyTrick
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (command: MatchCommand)
        (sourceId: CardInstanceId)
        (effect: EffectId)
        (isResuming: bool)
        (beerMatResults: ImmutableArray<bool>)
        =
        match validatePlayingTurn builder command.Actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            let source = builder.FindCard sourceId
            let trick = catalog.PartyTrick effect
            let houseRule = catalog.HouseRule effect

            match source with
            | ValueNone -> HandlerResult.reject CommandRejectionCode.EffectNotFound
            | ValueSome _ when trick.IsNone && houseRule.IsNone ->
                HandlerResult.reject CommandRejectionCode.EffectNotFound
            | ValueSome source ->

                let isActivatedTrick =
                    trick.IsSome
                    && source.Owner = command.Actor
                    && PokemonPowers.pokemonPowerIsEnabled catalog builder source
                    && effectivePartyTricks catalog builder source
                       |> Seq.exists (fun candidate ->
                           candidate.MechanicalId = effect.Value
                           && candidate.Trigger = BlokemonTrigger.Activated)

                let isActivatedLocalRule =
                    houseRule.IsSome
                    && source.Kind = CardKind.Kit
                    && source.Zone = CardZone.Local
                    && catalog.HouseRules source
                       |> Seq.exists (fun candidate ->
                           candidate.MechanicalId = effect.Value
                           && containsOpcode candidate.Program BlokemonOpcode.OncePerRound)

                if
                    (not isActivatedTrick && not isActivatedLocalRule)
                    || Seq.contains effect builder.RoundUsage.EffectsUsed
                then
                    HandlerResult.reject CommandRejectionCode.EffectUnavailable
                else

                    let program =
                        match trick with
                        | ValueSome trick -> trick.Program
                        | ValueNone -> houseRule.Value.Program

                    let submissionRejection =
                        if isResuming then
                            ValueNone
                        else
                            let requirements =
                                interpreter.InspectChoices(
                                    builder,
                                    command.Actor,
                                    source,
                                    effect,
                                    program
                                )

                            match
                                interpreter.ValidateChoiceSubmission(
                                    command.Choices,
                                    requirements,
                                    command.Actor
                                )
                            with
                            | ValueSome rejection ->
                                ValueSome(HandlerResult.rejectWith rejection requirements)
                            | ValueNone -> ValueNone

                    match submissionRejection with
                    | ValueSome result -> result
                    | ValueNone ->

                        let plan =
                            interpreter.Plan(
                                builder,
                                command.Actor,
                                source,
                                effect,
                                program,
                                command.Choices,
                                false,
                                isActivatedLocalRule,
                                beerMatResults
                            )

                        if not plan.IsApplied then
                            if plan.Rejection <> ValueSome CommandRejectionCode.ChoiceRequired then
                                HandlerResult.rejectWith
                                    (plan.Rejection
                                     |> ValueOption.defaultValue CommandRejectionCode.InvalidChoice)
                                    plan.Requirements
                            else
                                pendEffect
                                    builder
                                    command
                                    source.Id
                                    effect
                                    plan.Requirements
                                    beerMatResults
                                    plan.BeerMatResults
                                    false
                        else
                            let execution =
                                interpreter.Execute(
                                    builder,
                                    command.Actor,
                                    source,
                                    effect,
                                    program,
                                    command.Choices,
                                    false,
                                    isActivatedLocalRule,
                                    ValueNone,
                                    beerMatResults
                                )

                            if not execution.IsApplied then
                                HandlerResult.rejectWith
                                    (execution.Rejection
                                     |> ValueOption.defaultValue CommandRejectionCode.InvalidChoice)
                                    execution.Requirements
                            else
                                resolveSendHome
                                    catalog
                                    interpreter
                                    builder
                                    ImmutableArray<_>.Empty
                                    ValueNone
                                    false
                                    ImmutableArray<_>.Empty
                                |> ignore

                                if containsOpcode program BlokemonOpcode.Buzzap then
                                    builder.Events.Add(
                                        PendingMatchEvent.forCards
                                            MatchEventKind.BlokeSentHome
                                            (builder.Other source.Owner)
                                            source.Id
                                            (ImmutableArray.Create source.Id)
                                    )

                                    let takingPlayer = builder.Other source.Owner

                                    builder.TakeBarChits(
                                        takingPlayer,
                                        catalog.BarChits source,
                                        source.Id
                                    )
                                    |> ignore

                                    if source.Zone = CardZone.Oche then
                                        assignReplacement catalog builder source.Owner

                                    resolveWins catalog builder ValueNone

                                resolveVoluntarySourceChuck catalog builder source execution
                                HandlerResult.accepted

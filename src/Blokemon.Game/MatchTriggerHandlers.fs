namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchWins
open Blokemon.Game.MatchSendHome
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchRounds
open Blokemon.Game.MatchPending
open Blokemon.Game.MatchKitHandlers
open Blokemon.Game.MatchTrickHandlers
open Blokemon.Game.MatchAttackHandlers

/// Answering something the table is already waiting on: a parked effect, a knockout trigger, or the
/// bar chits drawn off the top of a knockout.
module internal MatchTriggerHandlers =

    let resolveEffectChoice
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (command: MatchCommand)
        =
        match builder.PendingEffect with
        | ValueNone -> HandlerResult.reject CommandRejectionCode.WrongPhase
        | ValueSome _ when builder.Phase <> MatchPhase.AwaitingEffectChoice ->
            HandlerResult.reject CommandRejectionCode.WrongPhase
        | ValueSome pending when pending.Chooser <> command.Actor ->
            HandlerResult.rejectWith CommandRejectionCode.WrongChooser pending.Requirements
        | ValueSome pending ->
            match
                interpreter.ValidateChoiceSubmission(
                    command.Choices,
                    pending.Requirements,
                    command.Actor
                )
            with
            | ValueSome rejection -> HandlerResult.rejectWith rejection pending.Requirements
            | ValueNone ->
                builder.PendingEffect <- ValueNone
                builder.Phase <- MatchPhase.Playing

                let resumed =
                    withChoices
                        pending.Command
                        (ImmutableArray.CreateRange(
                            Seq.append pending.Command.Choices command.Choices
                        ))

                match resumed.Action with
                | MatchAction.Attack(attacker, attackId) ->
                    attack
                        catalog
                        interpreter
                        builder
                        resumed
                        attacker
                        attackId
                        true
                        pending.AttackStarted
                        pending.BeerMatResults
                | MatchAction.PlayKit(kit, target) ->
                    playKit
                        catalog
                        interpreter
                        builder
                        resumed
                        kit
                        target
                        true
                        pending.BeerMatResults
                | MatchAction.UsePartyTrick(source, effect) ->
                    usePartyTrick
                        catalog
                        interpreter
                        builder
                        resumed
                        source
                        effect
                        true
                        pending.BeerMatResults
                | _ -> HandlerResult.reject CommandRejectionCode.InvalidChoice

    let resolveKnockoutTrigger
        (_: AuthorityCatalog)
        (_: BlokemonInterpreter)
        (_: MatchBuilder)
        (_: PlayerId)
        (_: CardInstanceId voption)
        =
        HandlerResult.reject CommandRejectionCode.EffectUnavailable

    let resolveBarChitTrigger
        (_: AuthorityCatalog)
        (_: BlokemonInterpreter)
        (_: MatchBuilder)
        (_: PlayerId)
        (_: bool)
        =
        HandlerResult.reject CommandRejectionCode.EffectUnavailable

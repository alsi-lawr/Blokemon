namespace Blokemon.Game

open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchPending
open Blokemon.Game.MatchKitRules

/// Playing a kit: every executable house rule is planned against a throwaway copy first, so a rule
/// that turns out to need an answer parks the command before anything has moved.
module internal MatchKitHandlers =

    let playKit
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (command: MatchCommand)
        (kitId: CardInstanceId)
        (targetId: CardInstanceId voption)
        (isResuming: bool)
        (beerMatResults: ImmutableArray<bool>)
        =
        match validatePlayingTurn builder command.Actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            match builder.FindCard kitId with
            | ValueNone -> HandlerResult.reject CommandRejectionCode.CardNotFound
            | ValueSome kitCard when kitCard.Owner <> command.Actor ->
                HandlerResult.reject CommandRejectionCode.CardNotOwned
            | ValueSome kitCard when kitCard.Kind <> CardKind.Kit || kitCard.Zone <> CardZone.Mitt ->
                HandlerResult.reject CommandRejectionCode.WrongZone
            | ValueSome kitCard ->

                let kit = catalog.Kit kitCard.MechanicalId

                match validateKitCategory catalog builder command.Actor kit targetId with
                | ValueSome rejection -> HandlerResult.reject rejection
                | ValueNone ->

                    let executableRules =
                        kit.HouseRules
                        |> Array.filter (fun rule ->
                            not (containsOpcode rule.Program BlokemonOpcode.OncePerRound)
                            && not (isDeclarativeHouseRule rule))

                    let submissionRejection =
                        if isResuming then
                            ValueNone
                        else
                            let requirements =
                                ImmutableArray.CreateRange(
                                    executableRules
                                    |> Seq.collect (fun rule ->
                                        interpreter.InspectChoices(
                                            builder,
                                            command.Actor,
                                            kitCard,
                                            EffectId rule.MechanicalId,
                                            rule.Program
                                        ))
                                    |> fun requirements ->
                                        requirements.DistinctBy(fun requirement -> requirement.Id)
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

                        // Every rule is planned against a throwaway copy first, so a rule that turns out to need an
                        // answer parks the whole command before anything has moved on the real table.
                        let planned =
                            executableRules
                            |> Array.tryPick (fun houseRule ->
                                let effect = EffectId houseRule.MechanicalId

                                let plan =
                                    interpreter.Plan(
                                        builder,
                                        command.Actor,
                                        kitCard,
                                        effect,
                                        houseRule.Program,
                                        command.Choices,
                                        false,
                                        true,
                                        beerMatResults
                                    )

                                if plan.IsApplied then
                                    None
                                elif
                                    plan.Rejection <> ValueSome CommandRejectionCode.ChoiceRequired
                                then
                                    Some(
                                        HandlerResult.rejectWith
                                            (plan.Rejection
                                             |> ValueOption.defaultValue
                                                 CommandRejectionCode.InvalidChoice)
                                            plan.Requirements
                                    )
                                else
                                    Some(
                                        pendEffect
                                            builder
                                            command
                                            kitCard.Id
                                            effect
                                            plan.Requirements
                                            beerMatResults
                                            plan.BeerMatResults
                                            false
                                    ))

                        match planned with
                        | Some result -> result
                        | None ->

                            match kit.Kind, targetId with
                            | BlokemonKitKind.BarKit, ValueSome target ->
                                builder.Attach(kitCard.Id, target)
                            | BlokemonKitKind.Local, _ ->
                                let current =
                                    builder.Cards
                                    |> Seq.filter (fun card -> card.Zone = CardZone.Local)
                                    |> Seq.toArray
                                    |> Array.tryExactlyOne

                                match current with
                                | Some card -> builder.MoveCard(card.Id, CardZone.EmptiesTray)
                                | None -> ()

                                builder.MoveCard(kitCard.Id, CardZone.Local)
                            | _ -> ()

                            let executionRejection =
                                executableRules
                                |> Array.tryPick (fun houseRule ->
                                    let execution =
                                        interpreter.Execute(
                                            builder,
                                            command.Actor,
                                            builder.Card kitCard.Id,
                                            EffectId houseRule.MechanicalId,
                                            houseRule.Program,
                                            command.Choices,
                                            false,
                                            true,
                                            ValueNone,
                                            beerMatResults,
                                            ValueNone
                                        )

                                    if execution.IsApplied then
                                        None
                                    else
                                        Some(
                                            HandlerResult.rejectWith
                                                (execution.Rejection
                                                 |> ValueOption.defaultValue
                                                     CommandRejectionCode.InvalidChoice)
                                                execution.Requirements
                                        ))

                            match executionRejection with
                            | Some result -> result
                            | None ->

                                if (builder.Card kitCard.Id).Zone = CardZone.Mitt then
                                    builder.MoveCard(kitCard.Id, CardZone.EmptiesTray)

                                builder.RoundUsage <-
                                    { builder.RoundUsage with
                                        KitsPlayed =
                                            ImmutableArray.CreateRange(
                                                Seq.append
                                                    builder.RoundUsage.KitsPlayed
                                                    [ kitCard.MechanicalId ]
                                            ) }

                                builder.RoundUsage <-
                                    match kit.Kind with
                                    | BlokemonKitKind.BarBit -> builder.RoundUsage
                                    | BlokemonKitKind.Mate ->
                                        { builder.RoundUsage with
                                            MatesPlayed = builder.RoundUsage.MatesPlayed + 1 }
                                    | BlokemonKitKind.Local ->
                                        { builder.RoundUsage with
                                            LocalsPlayed = builder.RoundUsage.LocalsPlayed + 1 }
                                    | BlokemonKitKind.BarKit -> builder.RoundUsage
                                    | other -> failwithf "Unhandled kit kind %A." other

                                HandlerResult.accepted

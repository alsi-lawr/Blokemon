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
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (vim: CardInstanceId voption)
        =
        match builder.PendingKnockout with
        | ValueNone -> HandlerResult.reject CommandRejectionCode.WrongPhase
        | ValueSome _ when builder.Phase <> MatchPhase.AwaitingTriggerChoice ->
            HandlerResult.reject CommandRejectionCode.WrongPhase
        | ValueSome pending when pending.Chooser <> actor ->
            HandlerResult.reject CommandRejectionCode.WrongChooser
        | ValueSome pending when vim.IsSome && not (Seq.contains vim.Value pending.EligibleVim) ->
            HandlerResult.reject CommandRejectionCode.InvalidChoice
        | ValueSome pending ->

            let source = builder.Card pending.TriggerSource

            let trick =
                catalog.PartyTricks source
                |> Seq.find (fun value -> EffectId value.MechanicalId = pending.TriggerEffect)

            let context =
                { KnockedOutBloke = ValueSome pending.KnockedOutCard
                  AttackingBloke = ValueSome pending.AttackingCard }

            let inspected =
                interpreter.InspectChoices(
                    builder,
                    actor,
                    source,
                    pending.TriggerEffect,
                    trick.Program,
                    ValueSome context
                )

            let optional =
                inspected
                |> Seq.find (fun requirement -> requirement.Kind = ChoiceRequirementKind.Optional)

            let acceptedOptional = EffectChoice.Optional(optional.Id, vim.IsSome)

            // Taking the vim opens a second question, so the requirement set has to be re-derived from a
            // run that already knows the answer to the first one.
            let requirements =
                if vim.IsNone then
                    inspected
                else
                    interpreter
                        .Plan(
                            builder,
                            actor,
                            source,
                            pending.TriggerEffect,
                            trick.Program,
                            ImmutableArray.Create acceptedOptional,
                            false,
                            false,
                            ImmutableArray<_>.Empty,
                            ValueSome context
                        )
                        .Requirements

            let choices = ResizeArray<EffectChoice> [ acceptedOptional ]

            match vim with
            | ValueSome selected ->
                let cards =
                    requirements
                    |> Seq.find (fun requirement -> requirement.Kind = ChoiceRequirementKind.Cards)

                choices.Add(EffectChoice.Cards(cards.Id, ImmutableArray.Create selected))
            | ValueNone -> ()

            let execution =
                interpreter.ExecuteTriggered(
                    builder,
                    actor,
                    source,
                    pending.TriggerEffect,
                    trick.Program,
                    ImmutableArray.CreateRange choices,
                    ValueSome context
                )

            if not execution.IsApplied then
                HandlerResult.rejectWith
                    (execution.Rejection
                     |> ValueOption.defaultValue CommandRejectionCode.InvalidChoice)
                    execution.Requirements
            else

                builder.Events.Add
                    { PendingMatchEvent.forCards
                          MatchEventKind.TriggerResolved
                          actor
                          pending.TriggerSource
                          (match vim with
                           | ValueSome moved -> ImmutableArray.Create moved
                           | ValueNone -> ImmutableArray<_>.Empty) with
                        Effect = ValueSome pending.TriggerEffect }

                let remainingVim =
                    pending.EligibleVim
                    |> Seq.filter (fun vim ->
                        (builder.Card vim).AttachedTo = ValueSome pending.KnockedOutCard)
                    |> Seq.toArray

                if pending.TriggerSources.Length > 0 && remainingVim.Length > 0 then
                    let nextSource = pending.TriggerSources[0]

                    let nextTrick =
                        catalog.PartyTricks(builder.Card nextSource)
                        |> Seq.find (fun value ->
                            value.Trigger = BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage)

                    builder.PendingKnockout <-
                        ValueSome
                            { pending with
                                TriggerSources =
                                    ImmutableArray.CreateRange(Seq.skip 1 pending.TriggerSources)
                                TriggerSource = nextSource
                                TriggerEffect = EffectId nextTrick.MechanicalId
                                EligibleVim = ImmutableArray.CreateRange remainingVim }

                    HandlerResult.accepted
                else
                    builder.PendingKnockout <- ValueNone
                    builder.Phase <- MatchPhase.Playing

                    sendHomeOne
                        catalog
                        interpreter
                        builder
                        (builder.Card pending.KnockedOutCard)
                        (ValueSome pending.AttackingCard)
                        pending.FinishRoundAfterResolution
                    |> ignore

                    takeExtraBarChits
                        catalog
                        builder
                        pending.AttackingCard
                        pending.ExtraBarChits
                        pending.FinishRoundAfterResolution

                    let completed =
                        resolveSendHome
                            catalog
                            interpreter
                            builder
                            pending.RemainingKnockouts
                            (ValueSome pending.AttackingCard)
                            pending.FinishRoundAfterResolution
                            pending.AttackDamageTargets
                            0

                    if completed && pending.FinishRoundAfterResolution then
                        finishAttackResolution catalog interpreter builder

                    HandlerResult.accepted

    let resolveBarChitTrigger
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (putOntoBooth: bool)
        =
        match builder.PendingBarChits |> Seq.tryHead with
        | None -> HandlerResult.reject CommandRejectionCode.WrongPhase
        | Some _ when builder.Phase <> MatchPhase.AwaitingTriggerChoice ->
            HandlerResult.reject CommandRejectionCode.WrongPhase
        | Some pending when pending.Player <> actor ->
            HandlerResult.reject CommandRejectionCode.WrongChooser
        | Some pending ->

            let card = builder.Card pending.Card

            if
                putOntoBooth
                && (card.Zone <> CardZone.Mitt
                    || (builder.CardsIn(actor, CardZone.Booth) |> Seq.length)
                       >= catalog.Manifest.BaseRules.Opening.BoothLimit)
            then
                HandlerResult.reject CommandRejectionCode.InvalidChoice
            else

                builder.RemoveBarChit pending

                let trick =
                    catalog.PartyTricks card
                    |> Seq.find (fun value -> EffectId value.MechanicalId = pending.Effect)

                let requirements =
                    interpreter.InspectChoices(builder, actor, card, pending.Effect, trick.Program)

                let optional =
                    requirements
                    |> Seq.find (fun requirement ->
                        requirement.Kind = ChoiceRequirementKind.Optional)

                let execution =
                    interpreter.Execute(
                        builder,
                        actor,
                        card,
                        pending.Effect,
                        trick.Program,
                        ImmutableArray.Create(EffectChoice.Optional(optional.Id, putOntoBooth)),
                        false
                    )

                if not execution.IsApplied then
                    HandlerResult.rejectWith
                        (execution.Rejection
                         |> ValueOption.defaultValue CommandRejectionCode.InvalidChoice)
                        execution.Requirements
                else
                    builder.Events.Add
                        { PendingMatchEvent.forCard MatchEventKind.TriggerResolved actor card.Id with
                            Effect = ValueSome pending.Effect
                            Amount = (if putOntoBooth then 1 else 0) }

                    resolveWins catalog builder ValueNone

                    if builder.Phase = MatchPhase.Complete then
                        HandlerResult.accepted
                    elif not (Seq.isEmpty builder.PendingBarChits) then
                        builder.Phase <- MatchPhase.AwaitingTriggerChoice
                        HandlerResult.accepted
                    else
                        builder.Phase <- MatchPhase.Playing

                        if pending.FinishRoundAfterResolution then
                            finishAttackResolution catalog interpreter builder

                        HandlerResult.accepted

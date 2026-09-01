namespace Blokemon.Game

open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchPending

/// Promoting a bloke and taxiing one in from the booth: the two commands that reshape who is at the
/// oche without running a kit.
module internal MatchPlayHandlers =

    let promote
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (command: MatchCommand)
        (promotionId: CardInstanceId)
        (blokeId: CardInstanceId)
        =
        match validatePlayingTurn builder command.Actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            match builder.FindCard promotionId, builder.FindCard blokeId with
            | ValueNone, _
            | _, ValueNone -> HandlerResult.reject CommandRejectionCode.CardNotFound
            | ValueSome promotion, ValueSome target ->

                let player = builder.Player command.Actor
                let rules = catalog.Manifest.BaseRules.Promotion

                if
                    promotion.Owner <> command.Actor
                    || target.Owner <> command.Actor
                    || promotion.Kind <> CardKind.Bloke
                    || promotion.Zone <> CardZone.Mitt
                    || not (isInPlay target)
                    || (rules.NotOnEitherFirstRound && player.RoundsStarted <= 1)
                    || (rules.NotFirstRoundInPlay
                        && target.EnteredAtOwnerRound = player.RoundsStarted)
                    || (rules.NotTwiceInRound && target.LastPromotedRound = builder.RoundNumber)
                    || (rules.ExactMechanicalEdgeRequired
                        && (catalog.Bloke promotion.MechanicalId).PromotesFromId
                           <> target.MechanicalId.Value)
                then
                    HandlerResult.reject CommandRejectionCode.IneligiblePromotion
                else
                    let zone = target.Zone

                    builder.SetCard
                        { target with
                            Zone = CardZone.Attached
                            AttachedTo = ValueSome promotion.Id
                            Attachments = ImmutableArray<_>.Empty
                            RoughStates = ImmutableArray<_>.Empty }

                    // A promotion takes the place of what it promoted from rather than arriving
                    // anywhere: it is the same pile of cards, taller. Standing it at the end of the
                    // Booth would move a Bloke the player never picked up.
                    builder.SetCard
                        { promotion with
                            Zone = zone
                            StackPosition = target.StackPosition
                            Damage =
                                if rules.RetainDamageAndAttachedCards then
                                    target.Damage
                                else
                                    0
                            Attachments =
                                if rules.RetainDamageAndAttachedCards then
                                    target.Attachments
                                else
                                    ImmutableArray<_>.Empty
                            UnderlyingCards =
                                ImmutableArray.CreateRange(
                                    Seq.append target.UnderlyingCards [ target.Id ]
                                )
                            RoughStates =
                                if rules.ClearRoughStatesAndAttackEffects then
                                    ImmutableArray<_>.Empty
                                else
                                    target.RoughStates
                            EnteredAtOwnerRound = target.EnteredAtOwnerRound
                            LastPromotedRound = builder.RoundNumber }

                    for attachmentId in target.Attachments do
                        if rules.RetainDamageAndAttachedCards then
                            builder.SetCard
                                { builder.Card attachmentId with
                                    AttachedTo = ValueSome promotion.Id }
                        else
                            builder.DetachTo(attachmentId, CardZone.EmptiesTray)

                    if rules.ClearRoughStatesAndAttackEffects then
                        builder.RemoveEffectsFor target.Id
                    else
                        builder.RetargetEffects(target.Id, promotion.Id)

                    let mutable rejected = ValueNone

                    for trick in
                        catalog.PartyTricks(builder.Card promotion.Id)
                        |> Seq.filter (fun trick ->
                            trick.Trigger = BlokemonTrigger.OnPromotionFromMitt)
                        |> Seq.toArray do
                        if rejected.IsNone then
                            let execution =
                                interpreter.Execute(
                                    builder,
                                    command.Actor,
                                    builder.Card promotion.Id,
                                    EffectId trick.MechanicalId,
                                    trick.Program,
                                    command.Choices,
                                    false
                                )

                            if not execution.IsApplied then
                                rejected <-
                                    ValueSome(
                                        HandlerResult.rejectWith
                                            (execution.Rejection
                                             |> ValueOption.defaultValue
                                                 CommandRejectionCode.InvalidChoice)
                                            execution.Requirements
                                    )
                            else
                                resolveSendHome
                                    catalog
                                    interpreter
                                    builder
                                    execution.ForcedSendHome
                                    ValueNone
                                    false
                                    ImmutableArray<_>.Empty
                                    0
                                |> ignore

                    match rejected with
                    | ValueSome result -> result
                    | ValueNone -> HandlerResult.accepted

    let taxi
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (boothBloke: CardInstanceId)
        (vimToChuck: ImmutableArray<CardInstanceId>)
        =
        match validatePlayingTurn builder actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            match builder.Oche actor, builder.FindCard boothBloke with
            | ValueNone, _
            | _, ValueNone -> HandlerResult.reject CommandRejectionCode.CardNotFound
            | ValueSome outgoing, ValueSome incoming ->

                if
                    outgoing.Kind <> CardKind.Bloke
                    || incoming.Kind <> CardKind.Bloke
                    || incoming.Owner <> actor
                    || incoming.Zone <> CardZone.Booth
                then
                    HandlerResult.reject CommandRejectionCode.WrongZone
                elif
                    builder.RoundUsage.TaxisUsed >= catalog.Manifest.BaseRules.Taxi.PerRound
                    || outgoing.RoughStates
                       |> Seq.exists (fun entry -> (catalog.RoughState entry.State).PreventsTaxi)
                    || builder.Effects
                       |> Seq.exists (fun effect ->
                           effect.TargetCard = ValueSome outgoing.Id
                           && effect.Kind = TemporaryEffectKind.RestrictTaxi)
                then
                    HandlerResult.reject CommandRejectionCode.EffectUnavailable
                else
                    let fare = effectiveTaxiFare catalog builder outgoing

                    let attachedVim =
                        outgoing.Attachments
                        |> Seq.map builder.Card
                        |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
                        |> Seq.toArray

                    if not (retreatPaymentIsValid catalog builder outgoing fare vimToChuck) then
                        HandlerResult.reject CommandRejectionCode.InvalidTaxiFare
                    else
                        builder.RoundUsage <-
                            { builder.RoundUsage with
                                TaxisUsed = builder.RoundUsage.TaxisUsed + 1 }

                        let confused =
                            outgoing.RoughStates
                            |> Seq.map (fun entry -> catalog.RoughState entry.State)
                            |> Seq.tryFind (fun rule -> rule.BeforeTaxiBeerMat.GetValueOrDefault())

                        let canRetreat =
                            match confused with
                            | None -> true
                            | Some _ ->
                                let badge = builder.TossBeerMat actor

                                builder.Events.Add
                                    { PendingMatchEvent.forCard
                                          MatchEventKind.BeerMatTossed
                                          actor
                                          outgoing.Id with
                                        BadgeSide = ValueSome badge }

                                badge

                        if canRetreat then
                            if catalog.Manifest.BaseRules.Taxi.ChuckVimPerFareSymbol then
                                for vim in vimToChuck do
                                    builder.DetachTo(vim, CardZone.EmptiesTray)

                            builder.MoveCard(outgoing.Id, CardZone.Booth)

                            if
                                catalog.Manifest.BaseRules.Taxi.MovingToBoothClearsRoughStatesAndAttackEffects
                            then
                                builder.ClearRoughStates(actor, outgoing.Id)
                                builder.RemoveEffectsFor(outgoing.Id, true)

                            if not catalog.Manifest.BaseRules.Taxi.AttachedCardsAndDamageRemain then
                                let moved = builder.Card outgoing.Id

                                for attachment in moved.Attachments do
                                    builder.DetachTo(attachment, CardZone.EmptiesTray)

                                builder.SetCard
                                    { builder.Card outgoing.Id with
                                        Damage = 0 }

                            builder.MoveCard(incoming.Id, CardZone.Oche)

                        HandlerResult.accepted

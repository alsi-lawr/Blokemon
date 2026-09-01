namespace Blokemon.Game

open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchPending
open Blokemon.Game.MatchWins
open Blokemon.Game.PokemonPowers

/// Promoting a bloke and taxiing one in from the booth: the two commands that reshape who is at the
/// oche without running a kit.
module internal MatchPlayHandlers =

    let promote
        (catalog: AuthorityCatalog)
        (_: BlokemonInterpreter)
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
                    || builder.Cards
                       |> Seq.exists (fun card ->
                           hasActivePower catalog builder card BlokemonOpcode.PrehistoricPower)
                    || not (isInPlay target && catalog.CountsAsPokemon target)
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
                        builder.ClearAndRetargetPokemonEffects(target.Id, promotion.Id)
                    else
                        builder.RetargetEffects(target.Id, promotion.Id)

                    HandlerResult.accepted

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
                    not (catalog.CountsAsPokemon outgoing)
                    || not (catalog.CountsAsPokemon incoming)
                    || incoming.Owner <> actor
                    || incoming.Zone <> CardZone.Booth
                then
                    HandlerResult.reject CommandRejectionCode.WrongZone
                elif
                    outgoing.Kind = CardKind.Kit
                    || builder.RoundUsage.TaxisUsed >= catalog.Manifest.BaseRules.Taxi.PerRound
                    || outgoing.RoughStates
                       |> Seq.exists (fun entry -> (catalog.RoughState entry.State).PreventsTaxi)
                    || builder.Effects
                       |> Seq.exists (fun effect ->
                           effect.TargetCard = ValueSome outgoing.Id
                           && effect.Kind = TemporaryEffectKind.RestrictTaxi)
                then
                    HandlerResult.reject CommandRejectionCode.EffectUnavailable
                else
                    let fare = MatchRules.effectiveTaxiFare catalog builder outgoing

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
                                builder.ClearPokemonEffects outgoing.Id

                            if not catalog.Manifest.BaseRules.Taxi.AttachedCardsAndDamageRemain then
                                let moved = builder.Card outgoing.Id

                                for attachment in moved.Attachments do
                                    builder.DetachTo(attachment, CardZone.EmptiesTray)

                                builder.SetCard
                                    { builder.Card outgoing.Id with
                                        Damage = 0 }

                            builder.MoveCard(incoming.Id, CardZone.Oche)

                        HandlerResult.accepted

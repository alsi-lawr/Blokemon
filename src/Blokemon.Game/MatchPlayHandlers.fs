namespace Blokemon.Game

open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchPending

/// Promoting a bloke and taxiing one in from the booth: the two commands that reshape who is at the
/// oche without running a kit.
module internal MatchPlayHandlers =

    let private allowsFirstRoundPromotion
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (target: CardState)
        =
        catalog.PartyTricks target
        |> Seq.filter (fun trick -> trick.Trigger = BlokemonTrigger.Continuous)
        |> Seq.filter (fun trick ->
            containsCondition trick.Program BlokemonCondition.OpenedSecond
            && containsCondition trick.Program BlokemonCondition.OwnersFirstRound)
        |> Seq.exists (fun trick ->
            builder.Effects
            |> Seq.exists (fun effect ->
                effect.SourceEffect = EffectId trick.MechanicalId
                && effect.SourceCard = target.Id
                && effect.Kind = TemporaryEffectKind.ContinuousPartyTrick))

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
                let firstRoundPromotionAllowed = allowsFirstRoundPromotion catalog builder target

                if
                    promotion.Owner <> command.Actor
                    || target.Owner <> command.Actor
                    || promotion.Kind <> CardKind.Bloke
                    || promotion.Zone <> CardZone.Mitt
                    || not (isInPlay target)
                    || (not firstRoundPromotionAllowed
                        && (player.RoundsStarted <= 1
                            || target.EnteredAtOwnerRound = player.RoundsStarted))
                    || target.LastPromotedRound = builder.RoundNumber
                    || (catalog.Bloke promotion.MechanicalId).PromotesFromId
                       <> target.MechanicalId.Value
                then
                    HandlerResult.reject CommandRejectionCode.IneligiblePromotion
                else
                    let zone = target.Zone

                    builder.SetCard
                        { target with
                            Zone = CardZone.Attached
                            AttachedTo = ValueSome promotion.Id
                            Attachments = FrozenList.empty
                            RoughStates = FrozenList.empty }

                    builder.SetCard
                        { promotion with
                            Zone = zone
                            StackPosition = -1
                            Damage = target.Damage
                            Attachments = target.Attachments
                            UnderlyingCards =
                                FrozenList<CardInstanceId>
                                    .Create(Seq.append target.UnderlyingCards [ target.Id ])
                            RoughStates = FrozenList.empty
                            EnteredAtOwnerRound = target.EnteredAtOwnerRound
                            LastPromotedRound = builder.RoundNumber }

                    for attachmentId in target.Attachments do
                        builder.SetCard
                            { builder.Card attachmentId with
                                AttachedTo = ValueSome promotion.Id }

                    builder.RemoveEffectsFor target.Id

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
                                    FrozenList.empty
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
        (vimToChuck: FrozenList<CardInstanceId>)
        =
        match validatePlayingTurn builder actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            match builder.Oche actor, builder.FindCard boothBloke with
            | ValueNone, _
            | _, ValueNone -> HandlerResult.reject CommandRejectionCode.CardNotFound
            | ValueSome outgoing, ValueSome incoming ->

                if incoming.Owner <> actor || incoming.Zone <> CardZone.Booth then
                    HandlerResult.reject CommandRejectionCode.WrongZone
                elif
                    builder.RoundUsage.TaxisUsed >= catalog.Manifest.BaseRules.Taxi.PerRound
                    || outgoing.RoughStates
                       |> Seq.exists (fun entry ->
                           entry.State = BlokemonRoughState.NoddedOff
                           || entry.State = BlokemonRoughState.Legless)
                    || (outgoing.Kind = CardKind.Kit && catalog.IsFossil outgoing.MechanicalId)
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

                    if
                        vimToChuck.Count <> fare
                        || (vimToChuck |> Seq.distinct |> Seq.length) <> vimToChuck.Count
                        || vimToChuck
                           |> Seq.exists (fun id ->
                               not (attachedVim |> Array.exists (fun card -> card.Id = id)))
                    then
                        HandlerResult.reject CommandRejectionCode.InvalidTaxiFare
                    else
                        for vim in vimToChuck do
                            builder.DetachTo(vim, CardZone.EmptiesTray)

                        builder.MoveCard(outgoing.Id, CardZone.Booth)
                        builder.ClearRoughStates(actor, outgoing.Id)
                        builder.RemoveEffectsFor(outgoing.Id, true)
                        builder.MoveCard(incoming.Id, CardZone.Oche)

                        builder.RoundUsage <-
                            { builder.RoundUsage with
                                TaxisUsed = builder.RoundUsage.TaxisUsed + 1 }

                        HandlerResult.accepted

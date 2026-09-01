namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.EffectSelection
open Blokemon.Game.EffectDamage
open Blokemon.Game.EffectRegistration

/// Sending cards somewhere else: the destination rules, the chuck, and the draw.
module internal EffectCardMoves =

    let private moveBlokeAndAttachedCardsToStack (builder: MatchBuilder) (bloke: CardState) =
        builder.RemoveEffectsFor bloke.Id

        for cardId in Seq.append bloke.Attachments bloke.UnderlyingCards |> Seq.distinct do
            builder.RemoveEffectsFor cardId
            builder.MoveCard(cardId, CardZone.Stack)

            builder.SetCard
                { builder.Card cardId with
                    Attachments = ImmutableArray<_>.Empty
                    UnderlyingCards = ImmutableArray<_>.Empty
                    Damage = 0
                    RoughStates = ImmutableArray<_>.Empty }

        builder.MoveCard(bloke.Id, CardZone.Stack)

        builder.SetCard
            { builder.Card bloke.Id with
                Attachments = ImmutableArray<_>.Empty
                UnderlyingCards = ImmutableArray<_>.Empty
                Damage = 0
                RoughStates = ImmutableArray<_>.Empty }

    let private zoneFor (destination: BlokemonEffectDestination) (card: CardState) =
        match destination with
        | BlokemonEffectDestination.OwnMitt
        | BlokemonEffectDestination.OtherMitt -> CardZone.Mitt
        | BlokemonEffectDestination.OwnBooth
        | BlokemonEffectDestination.OtherBooth -> CardZone.Booth
        | BlokemonEffectDestination.OwnStack
        | BlokemonEffectDestination.OtherStack
        | BlokemonEffectDestination.TopOfOwnStack -> CardZone.Stack
        | BlokemonEffectDestination.OwnEmptiesTray -> CardZone.EmptiesTray
        | _ -> card.Zone

    let private canMove
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (destination: BlokemonEffectDestination)
        (card: CardState)
        =
        not (isInPlay card && effectIsPrevented runtime card)

    let private boothCapacityWouldBeExceeded
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (selected: CardState array)
        (destination: BlokemonEffectDestination)
        =
        let destinationOwner =
            match destination with
            | BlokemonEffectDestination.OwnBooth -> ValueSome runtime.Actor
            | BlokemonEffectDestination.OtherBooth -> ValueSome(runtime.Builder.Other runtime.Actor)
            | _ -> ValueNone

        match destinationOwner with
        | ValueNone -> false
        | ValueSome owner ->
            let entering =
                selected
                |> Seq.distinctBy (fun card -> card.Id)
                |> Seq.filter (canMove catalog runtime destination)
                |> Seq.filter (fun card ->
                    card.Owner = owner && card.Kind = CardKind.Bloke && card.Zone <> CardZone.Booth)
                |> Seq.length

            (runtime.Builder.CardsIn(owner, CardZone.Booth) |> Seq.length) + entering > catalog.Manifest.BaseRules.Opening.BoothLimit

    let moveCardsToDestination
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (selected: CardState seq)
        (destination: BlokemonEffectDestination)
        =
        let selected = selected |> Seq.toArray
        let mutable moved = 0

        let capacityExceeded =
            boothCapacityWouldBeExceeded catalog runtime selected destination

        if capacityExceeded then
            runtime.Rejection <- ValueSome CommandRejectionCode.RuleLimitReached

        for card in selected do
            if not capacityExceeded && canMove catalog runtime destination card then
                let zone = zoneFor destination card

                if zone <> card.Zone || destination <> BlokemonEffectDestination.Unspecified then
                    if destination = BlokemonEffectDestination.TopOfOwnStack then
                        runtime.Builder.MoveToTopOfStack card.Id
                        moved <- moved + 1
                    elif zone = CardZone.Stack && catalog.CountsAsPokemon card && isInPlay card then
                        moveBlokeAndAttachedCardsToStack runtime.Builder card
                        moved <- moved + 1
                    else
                        if card.Zone = CardZone.Attached && zone <> CardZone.Attached then
                            runtime.Builder.DetachTo(card.Id, zone)
                        else
                            runtime.Builder.MoveCard(card.Id, zone)

                        moved <- moved + 1

                        if zone = CardZone.Booth && card.Kind = CardKind.Bloke then
                            runtime.Builder.SetCard
                                { runtime.Builder.Card card.Id with
                                    EnteredAtOwnerRound =
                                        (runtime.Builder.Player card.Owner).RoundsStarted }

        moved

    let executeChuckCards
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        let selected =
            resolveSelectedTargets catalog runtime instruction path |> Seq.toArray

        for card in selected |> Seq.truncate instruction.Amount do
            if card.Zone = CardZone.Attached then
                runtime.Builder.DetachTo(card.Id, CardZone.EmptiesTray)
            else
                runtime.Builder.MoveCard(card.Id, CardZone.EmptiesTray)

    let executeDraw (runtime: EffectRuntime) (instruction: BlokemonEffectInstruction) =
        let count =
            if instruction.Selection = BlokemonSelection.UntilBlankSide then
                runtime.BadgeSides * instruction.Amount
            else
                instruction.Amount

        let player =
            if Array.contains BlokemonTarget.OtherStack instruction.Targets then
                runtime.Builder.Other runtime.Actor
            else
                runtime.Actor

        runtime.Builder.Draw(player, count, DrawReason.Effect) |> ignore

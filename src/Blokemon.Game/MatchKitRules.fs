namespace Blokemon.Game

open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchKnockouts
open Blokemon.Game.MatchPending

/// Whether a kit card may be played at all: the per-round limits, the local already on the table,
/// and the bar kit that needs a legal host.
module internal MatchKitRules =

    let private validateLocal
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (kit: BlokemonKit)
        =
        if builder.RoundUsage.LocalsPlayed >= catalog.Manifest.BaseRules.Kit.LocalsPerRound then
            ValueSome CommandRejectionCode.RuleLimitReached
        else
            let current =
                builder.Cards
                |> Seq.filter (fun card -> card.Zone = CardZone.Local)
                |> Seq.toArray
                |> Array.tryExactlyOne

            match current with
            | Some card when card.MechanicalId.Value = kit.Id ->
                ValueSome CommandRejectionCode.RuleLimitReached
            | _ -> ValueNone

    let private validateBarKit
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (targetId: CardInstanceId voption)
        =
        match targetId with
        | ValueNone -> ValueSome CommandRejectionCode.CardNotFound
        | ValueSome id ->
            match builder.FindCard id with
            | ValueNone -> ValueSome CommandRejectionCode.CardNotOwned
            | ValueSome target when target.Owner <> actor || not (isInPlay target) ->
                ValueSome CommandRejectionCode.CardNotOwned
            | ValueSome target ->
                if
                    target.Attachments
                    |> Seq.map builder.Card
                    |> Seq.exists (fun card ->
                        card.Kind = CardKind.Kit
                        && (catalog.Kit card.MechanicalId).Kind = BlokemonKitKind.BarKit)
                then
                    ValueSome CommandRejectionCode.RuleLimitReached
                else
                    ValueNone

    let validateKitCategory
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (kit: BlokemonKit)
        (targetId: CardInstanceId voption)
        =
        let restricted =
            builder.Effects
            |> Seq.exists (fun effect ->
                effect.Owner <> actor
                && ((effect.Kind = TemporaryEffectKind.RestrictKit
                     && kit.Kind = BlokemonKitKind.BarBit)
                    || (kit.Kind = BlokemonKitKind.Local
                        && effect.Kind = TemporaryEffectKind.RestrictLocal)))

        if restricted then
            ValueSome CommandRejectionCode.EffectUnavailable
        else
            match kit.Kind with
            | BlokemonKitKind.BarBit -> ValueNone
            | BlokemonKitKind.Mate ->
                if
                    builder.RoundUsage.MatesPlayed >= catalog.Manifest.BaseRules.Kit.MatesPerRound
                    || (actor = builder.OpeningPlayer && (builder.Player actor).RoundsStarted = 1)
                then
                    ValueSome CommandRejectionCode.RuleLimitReached
                else
                    ValueNone
            | BlokemonKitKind.Local -> validateLocal catalog builder kit
            | BlokemonKitKind.BarKit -> validateBarKit catalog builder actor targetId
            | other -> failwithf "Unhandled kit kind %A." other

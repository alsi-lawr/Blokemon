namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchWins
open Blokemon.Game.MatchKnockouts

/// Ending a round: the checkup, the effects that come due, and handing the table over.
module internal MatchRounds =

    let private tossCheckup (builder: MatchBuilder) (player: PlayerId) (card: CardInstanceId) =
        let badge = builder.TossBeerMat(player, false)

        builder.Events.Add
            { PendingMatchEvent.forCard MatchEventKind.BeerMatTossed player card with
                BadgeSide = ValueSome badge }

        badge

    let private runCheckup
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (completedPlayer: PlayerId)
        =
        for player in builder.Players |> Seq.map (fun player -> player.Id) |> Seq.toArray do
            match builder.Oche player with
            | ValueNone -> ()
            | ValueSome oche ->
                for roughState in catalog.Manifest.BaseRules.Checkup.RoughStateOrder do
                    let current = builder.Card oche.Id

                    if
                        current.RoughStates |> Seq.exists (fun entry -> entry.State = roughState)
                    then
                        match roughState with
                        | BlokemonRoughState.DodgyPint ->
                            builder.PlaceDamage(player, current.Id, 10, DamageKind.RoughState)
                        | BlokemonRoughState.Singed ->
                            builder.PlaceDamage(player, current.Id, 20, DamageKind.RoughState)

                            if tossCheckup builder player current.Id then
                                builder.ClearRoughStates(player, current.Id, ValueSome roughState)
                        | BlokemonRoughState.NoddedOff ->
                            if tossCheckup builder player current.Id then
                                builder.ClearRoughStates(player, current.Id, ValueSome roughState)
                        | BlokemonRoughState.Legless ->
                            let entry =
                                current.RoughStates
                                |> Seq.find (fun value -> value.State = roughState)

                            if
                                (builder.Player player).RoundsStarted > entry.AppliedAtOwnerRound
                            then
                                builder.ClearRoughStates(player, current.Id, ValueSome roughState)
                        | _ -> ()

        for effect in
            builder.Effects
            |> Seq.filter (fun effect -> effect.Kind = TemporaryEffectKind.EndRoundEffect)
            |> Seq.toArray do
            let kit = builder.FindCard effect.SourceCard

            let attachedTo =
                match kit with
                | ValueSome card -> card.AttachedTo
                | ValueNone -> ValueNone

            match attachedTo with
            | ValueSome targetId when
                effect.Owner = completedPlayer && (builder.Card targetId).Zone = CardZone.Oche
                ->
                builder.Heal(effect.Owner, targetId, effect.Amount, ValueSome effect.SourceCard)
            | _ ->
                match effect.TargetCard with
                | ValueSome deferredTarget when effect.ExpiresAfterRound <= builder.RoundNumber ->
                    builder.PlaceDamage(
                        effect.Owner,
                        deferredTarget,
                        effect.Amount * 10,
                        DamageKind.PlacedCounter,
                        ValueSome effect.SourceCard
                    )

                    builder.RemoveEffect effect
                | _ -> ()

    let rec completeRound
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        =
        let completedPlayer = builder.ActivePlayer
        builder.Events.Add(PendingMatchEvent.forActor MatchEventKind.RoundEnded completedPlayer)
        runCheckup catalog builder completedPlayer

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

        if builder.Phase = MatchPhase.Complete then
            builder.PendingRoundEnd <- false
        elif builder.ReplacementPlayer.IsSome then
            builder.Phase <- MatchPhase.AwaitingReplacement
        else
            builder.PendingRoundEnd <- false
            builder.ExpireEffects builder.RoundNumber
            startRound catalog builder (builder.Other completedPlayer)

    let finishOrPendRound
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        =
        if builder.Phase <> MatchPhase.Complete then
            builder.PendingRoundEnd <- true

            if builder.ReplacementPlayer.IsSome then
                builder.Phase <- MatchPhase.AwaitingReplacement
            else
                completeRound catalog interpreter builder

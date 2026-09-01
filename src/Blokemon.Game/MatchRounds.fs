namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchWins
open Blokemon.Game.MatchKnockouts

/// Ending a round: the checkup, the effects that come due, and handing the table over.
module internal MatchRounds =

    let private discardExpiredAttachedTrainers (builder: MatchBuilder) =
        let sources =
            builder.Effects
            |> Seq.filter (fun effect ->
                effect.Kind = TemporaryEffectKind.AttachedTrainer
                && effect.ExpiresAfterRound <= builder.RoundNumber)
            |> Seq.map (fun effect -> effect.SourceCard, effect.SourceEffect)
            |> Seq.distinct
            |> Seq.toArray

        for source, effect in sources do
            match builder.FindCard source with
            | ValueSome card when card.Zone = CardZone.Attached ->
                builder.DetachTo(source, CardZone.EmptiesTray)
            | _ -> ()

            builder.RemoveEffects(effect, source)

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
                        let rule = catalog.RoughState roughState

                        if rule.CheckupDamageCounters > 0 then
                            let damage =
                                builder.Effects
                                |> Seq.filter (fun effect ->
                                    roughState = BlokemonRoughState.DodgyPint
                                    && effect.TargetCard = ValueSome current.Id
                                    && effect.Kind = TemporaryEffectKind.EnhancedPoison)
                                |> Seq.tryLast
                                |> Option.map (fun effect -> effect.Amount)
                                |> Option.defaultValue (rule.CheckupDamageCounters * 10)

                            builder.PlaceDamage(player, current.Id, damage, DamageKind.RoughState)

                        if rule.CheckupBeerMat then
                            if rule.BadgeSideRecovers && tossCheckup builder player current.Id then
                                builder.ClearRoughStates(player, current.Id, ValueSome roughState)
                        elif
                            rule.RecoversAfterOwnersNextRound.HasValue
                            && rule.RecoversAfterOwnersNextRound.Value
                        then
                            let entry =
                                current.RoughStates
                                |> Seq.find (fun value -> value.State = roughState)

                            if
                                (builder.Player player).RoundsStarted > entry.AppliedAtOwnerRound
                            then
                                builder.ClearRoughStates(player, current.Id, ValueSome roughState)

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
        |> ignore

        if builder.Phase = MatchPhase.Complete then
            builder.PendingRoundEnd <- false
        elif builder.ReplacementPlayer.IsSome then
            builder.Phase <- MatchPhase.AwaitingReplacement
        elif builder.Phase <> MatchPhase.Playing then
            builder.PendingRoundEnd <- false
        else
            builder.PendingRoundEnd <- false
            discardExpiredAttachedTrainers builder
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

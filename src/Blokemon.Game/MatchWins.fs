namespace Blokemon.Game

open System.Collections.Generic
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchSetup

/// Who has won, who still owes a replacement, and how a round begins.
module internal MatchWins =

    let assignReplacement (builder: MatchBuilder) (player: PlayerId) =
        if
            (builder.Oche player).IsNone
            && (builder.CardsIn(player, CardZone.Booth)
                |> Seq.exists (fun card -> card.Kind = CardKind.Bloke))
        then
            if builder.ReplacementPlayer.IsNone then
                builder.ReplacementPlayer <- ValueSome player

            builder.Phase <- MatchPhase.AwaitingReplacement

    let nextReplacement (builder: MatchBuilder) =
        builder.Players
        |> Seq.map (fun player -> player.Id)
        |> Seq.tryFind (fun player ->
            (builder.Oche player).IsNone
            && (builder.CardsIn(player, CardZone.Booth)
                |> Seq.exists (fun card -> card.Kind = CardKind.Bloke)))
        |> ValueOption.ofOption

    let private resolveLiveWins
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (failedRequiredDraw: PlayerId voption)
        =
        let methods = Dictionary<PlayerId, int>()

        for player in builder.Players do
            methods[player.Id] <- 0

        for player in builder.Players |> Seq.toArray do
            if (builder.Player player.Id).BarChitsRemaining = 0 then
                methods[player.Id] <- methods[player.Id] + 1

            let other = builder.Other player.Id

            if
                not (
                    builder.Cards
                    |> Seq.exists (fun card ->
                        card.Owner = other && card.Kind = CardKind.Bloke && isInPlay card)
                )
            then
                methods[player.Id] <- methods[player.Id] + 1

            if failedRequiredDraw = ValueSome other then
                methods[player.Id] <- methods[player.Id] + 1

        let winners = methods |> Seq.filter (fun pair -> pair.Value > 0) |> Seq.toArray

        if winners.Length > 0 then
            if winners.Length = 1 || winners[0].Value <> winners[1].Value then
                let winner =
                    (winners |> Seq.sortByDescending (fun pair -> pair.Value) |> Seq.head).Key

                builder.Winner <- ValueSome winner
                builder.Phase <- MatchPhase.Complete
                builder.ReplacementPlayer <- ValueNone
                builder.Events.Add(PendingMatchEvent.forActor MatchEventKind.MatchWon winner)
            else
                builder.SuddenDeathCount <- builder.SuddenDeathCount + 1
                builder.Events.Add(PendingMatchEvent.ofKind MatchEventKind.SuddenDeathStarted)
                startFreshGame catalog builder

    let resolveWins
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (failedRequiredDraw: PlayerId voption)
        =
        if builder.Phase <> MatchPhase.Complete then
            resolveLiveWins catalog builder failedRequiredDraw

    let startRound (catalog: AuthorityCatalog) (builder: MatchBuilder) (player: PlayerId) =
        builder.ActivePlayer <- player
        builder.RoundNumber <- builder.RoundNumber + 1
        let playerState = builder.Player player

        builder.SetPlayer
            { playerState with
                RoundsStarted = playerState.RoundsStarted + 1 }

        builder.RoundUsage <- RoundUsage.Empty player
        builder.Phase <- MatchPhase.Playing
        builder.Events.Add(PendingMatchEvent.forActor MatchEventKind.RoundStarted player)

        if
            catalog.Manifest.BaseRules.Round.RequiredOpeningDraw
            && builder.CardsIn(player, CardZone.Stack) |> Seq.isEmpty
        then
            resolveWins catalog builder (ValueSome player)
        elif catalog.Manifest.BaseRules.Round.RequiredOpeningDraw then
            builder.Draw(player, 1, DrawReason.RequiredRoundDraw) |> ignore

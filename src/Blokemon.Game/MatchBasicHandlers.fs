namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchWins
open Blokemon.Game.MatchRounds

/// The commands that only move a card or close a round: no effect program runs behind any of them.
module internal MatchBasicHandlers =

    let private beginRound (catalog: AuthorityCatalog) (builder: MatchBuilder) =
        startRound catalog builder builder.OpeningPlayer

    let private settleAfterBonus (catalog: AuthorityCatalog) (builder: MatchBuilder) =
        let state = builder.Snapshot()

        let benchable =
            builder.Players
            |> Seq.toArray
            |> Array.exists (fun current ->
                (MatchRules.bonusBenchable catalog state current.Id).Length > 0)

        if benchable then
            builder.Phase <- MatchPhase.BonusPlacement
        else
            beginRound catalog builder

    let chooseMulliganBonus
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (cardsToDraw: int)
        =
        if builder.Phase <> MatchPhase.MulliganBonus then
            HandlerResult.reject CommandRejectionCode.WrongPhase
        else
            let player = builder.Player actor

            if
                player.MulliganBonusChosen
                || player.MulliganBonusAllowance = 0
                || cardsToDraw < 0
                || cardsToDraw > player.MulliganBonusAllowance
            then
                HandlerResult.reject CommandRejectionCode.RuleLimitReached
            else
                let drawn = builder.Draw(actor, cardsToDraw, DrawReason.MulliganBonus)

                // The bonus can be taken a card at a time: what is left of the allowance stays
                // on offer, and the choice only closes when the player takes none of it or has
                // taken all of it. Taking the whole allowance in one command, or declining it
                // outright, closes it exactly as it always did.
                let remaining = player.MulliganBonusAllowance - cardsToDraw
                let closed = cardsToDraw = 0 || remaining = 0

                builder.SetPlayer
                    { player with
                        MulliganBonusAllowance = remaining
                        MulliganBonusChosen = closed
                        BonusDrawn = player.BonusDrawn.AddRange drawn
                        BonusPlacementChosen = false }

                if
                    builder.Players
                    |> Seq.forall (fun current -> (builder.Player current.Id).MulliganBonusChosen)
                then
                    settleAfterBonus catalog builder

                HandlerResult.accepted

    let chooseBonusPlacement
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (bonusBooth: ImmutableArray<CardInstanceId>)
        =
        if builder.Phase <> MatchPhase.BonusPlacement then
            HandlerResult.reject CommandRejectionCode.WrongPhase
        else
            let player = builder.Player actor
            let benchable = MatchRules.bonusBenchable catalog (builder.Snapshot()) actor

            let room =
                catalog.Manifest.BaseRules.Opening.BoothLimit
                - (builder.CardsIn(actor, CardZone.Booth) |> Seq.length)

            let illegal =
                player.BonusPlacementChosen
                || bonusBooth.Length > room
                || (bonusBooth |> Seq.distinct |> Seq.length) <> bonusBooth.Length
                || bonusBooth
                   |> Seq.exists (fun card ->
                       benchable |> Array.exists (fun candidate -> candidate.Id = card) |> not)

            if illegal then
                HandlerResult.reject CommandRejectionCode.IllegalOpening
            else
                for card in bonusBooth do
                    builder.MoveCard(card, CardZone.Booth)

                    builder.SetCard
                        { builder.Card card with
                            EnteredAtOwnerRound = (builder.Player actor).RoundsStarted }

                builder.SetPlayer
                    { builder.Player actor with
                        BonusPlacementChosen = true }

                if
                    builder.Players
                    |> Seq.forall (fun current -> (builder.Player current.Id).BonusPlacementChosen)
                then
                    beginRound catalog builder

                HandlerResult.accepted

    let chooseOpening
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (oche: CardInstanceId)
        (boothChoice: ImmutableArray<CardInstanceId>)
        =
        if builder.Phase <> MatchPhase.OpeningPlacement then
            HandlerResult.reject CommandRejectionCode.WrongPhase
        elif not (MatchRules.mayPlaceOpening (builder.Snapshot()) actor) then
            HandlerResult.reject CommandRejectionCode.WrongPhase
        else
            let player = builder.Player actor
            let ocheCard = builder.FindCard oche
            let booth = boothChoice |> Seq.map builder.FindCard |> Seq.toArray

            let illegal =
                player.OpeningChosen
                || ocheCard.IsNone
                || ocheCard.Value.Owner <> actor
                || ocheCard.Value.Zone <> CardZone.Mitt
                || ocheCard.Value.Kind <> CardKind.Bloke
                || not (catalog.IsRegular ocheCard.Value.MechanicalId)
                || boothChoice.Length > catalog.Manifest.BaseRules.Opening.BoothLimit
                || (boothChoice |> Seq.distinct |> Seq.length) <> boothChoice.Length
                || Seq.contains oche boothChoice
                || booth
                   |> Array.exists (fun card ->
                       card.IsNone
                       || card.Value.Owner <> actor
                       || card.Value.Zone <> CardZone.Mitt
                       || card.Value.Kind <> CardKind.Bloke
                       || not (catalog.IsRegular card.Value.MechanicalId))

            if illegal then
                HandlerResult.reject CommandRejectionCode.IllegalOpening
            else
                builder.MoveCard(oche, CardZone.Oche)

                builder.SetCard
                    { builder.Card oche with
                        EnteredAtOwnerRound = (builder.Player actor).RoundsStarted }

                for card in boothChoice do
                    builder.MoveCard(card, CardZone.Booth)

                    builder.SetCard
                        { builder.Card card with
                            EnteredAtOwnerRound = (builder.Player actor).RoundsStarted }

                builder.SetPlayer { player with OpeningChosen = true }

                if
                    builder.Players
                    |> Seq.forall (fun current -> (builder.Player current.Id).OpeningChosen)
                then
                    // Bar Chits belong to each player's own setup and are set aside as it
                    // finishes, so the bonus that follows is drawn from what is left rather than
                    // from the pile the Bar Chits still have to come off.
                    for current in builder.Players |> Seq.toArray do
                        builder.SetAsideBarChits(
                            current.Id,
                            catalog.Manifest.BaseRules.Opening.BarChitCount
                        )

                    if
                        builder.Players
                        |> Seq.exists (fun current ->
                            (builder.Player current.Id).MulliganBonusAllowance > 0)
                    then
                        builder.Phase <- MatchPhase.MulliganBonus
                    else
                        beginRound catalog builder

                HandlerResult.accepted

    let attachVim
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (vimId: CardInstanceId)
        (blokeId: CardInstanceId)
        =
        match validatePlayingTurn builder actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            match builder.FindCard vimId, builder.FindCard blokeId with
            | ValueNone, _
            | _, ValueNone -> HandlerResult.reject CommandRejectionCode.CardNotFound
            | ValueSome vim, ValueSome target ->
                if vim.Owner <> actor || target.Owner <> actor then
                    HandlerResult.reject CommandRejectionCode.CardNotOwned
                elif
                    vim.Kind <> CardKind.Vim
                    || vim.Zone <> CardZone.Mitt
                    || not (isInPlay target)
                    || builder.RoundUsage.VimAttachments
                       >= catalog.Manifest.BaseRules.Vim.NormalAttachmentPerRound
                then
                    HandlerResult.reject CommandRejectionCode.RuleLimitReached
                else
                    builder.Attach(vim.Id, target.Id)

                    builder.RoundUsage <-
                        { builder.RoundUsage with
                            VimAttachments = builder.RoundUsage.VimAttachments + 1 }

                    HandlerResult.accepted

    let playBloke
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (blokeId: CardInstanceId)
        =
        match validatePlayingTurn builder actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            match builder.FindCard blokeId with
            | ValueNone -> HandlerResult.reject CommandRejectionCode.CardNotFound
            | ValueSome bloke ->
                if bloke.Owner <> actor then
                    HandlerResult.reject CommandRejectionCode.CardNotOwned
                elif
                    bloke.Kind <> CardKind.Bloke
                    || bloke.Zone <> CardZone.Mitt
                    || not (catalog.IsRegular bloke.MechanicalId)
                    || (builder.CardsIn(actor, CardZone.Booth) |> Seq.length)
                       >= catalog.Manifest.BaseRules.Opening.BoothLimit
                then
                    HandlerResult.reject CommandRejectionCode.RuleLimitReached
                else
                    builder.MoveCard(bloke.Id, CardZone.Booth)

                    builder.SetCard
                        { builder.Card bloke.Id with
                            EnteredAtOwnerRound = (builder.Player actor).RoundsStarted }

                    HandlerResult.accepted

    let chuckFossil
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (fossilId: CardInstanceId)
        =
        match validatePlayingTurn builder actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->

            match builder.FindCard fossilId with
            | ValueSome fossil when
                fossil.Owner = actor
                && fossil.Kind = CardKind.Kit
                && catalog.IsFossil fossil.MechanicalId
                && isInPlay fossil
                ->
                builder.ChuckBloke fossil.Id |> ignore

                if fossil.Zone = CardZone.Oche then
                    assignReplacement builder actor

                HandlerResult.accepted
            | _ -> HandlerResult.reject CommandRejectionCode.EffectUnavailable

    let endRound
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (actor: PlayerId)
        =
        match validatePlayingTurn builder actor with
        | ValueSome turn -> HandlerResult.reject turn
        | ValueNone ->
            finishOrPendRound catalog interpreter builder
            HandlerResult.accepted

    let chooseReplacement
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (boothBloke: CardInstanceId)
        =
        if
            builder.Phase <> MatchPhase.AwaitingReplacement
            || builder.ReplacementPlayer <> ValueSome actor
        then
            HandlerResult.reject CommandRejectionCode.WrongPhase
        else
            match builder.FindCard boothBloke with
            | ValueSome replacement when
                replacement.Owner = actor && replacement.Zone = CardZone.Booth
                ->
                builder.MoveCard(replacement.Id, CardZone.Oche)
                builder.ReplacementPlayer <- nextReplacement builder

                if builder.ReplacementPlayer.IsNone then
                    if builder.PendingRoundEnd then
                        builder.PendingRoundEnd <- false
                        completeRound catalog interpreter builder
                    else
                        builder.Phase <- MatchPhase.Playing

                HandlerResult.accepted
            | _ -> HandlerResult.reject CommandRejectionCode.WrongZone

    let resign (builder: MatchBuilder) (actor: PlayerId) =
        // An immediate unconditional loss: no phase or turn gate applies, so every outstanding
        // requirement is abandoned before the opponent takes the win.
        builder.PendingEffect <- ValueNone
        builder.PendingKnockout <- ValueNone

        for pending in builder.PendingBarChits |> Seq.toArray do
            builder.RemoveBarChit pending

        builder.ReplacementPlayer <- ValueNone
        builder.PendingRoundEnd <- false
        let winner = builder.Other actor
        builder.Winner <- ValueSome winner
        builder.Phase <- MatchPhase.Complete
        builder.Events.Add(PendingMatchEvent.forActor MatchEventKind.MatchWon winner)
        HandlerResult.accepted

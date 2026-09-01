namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign

/// Dealing the opening mitts and settling the mulligan bonuses: everything between a validated
/// start request and the first state anyone gets to see.
module internal MatchSetup =

    let private ensureFreshGameDecksAreValid (catalog: AuthorityCatalog) (builder: MatchBuilder) =
        let expectedCardCount = catalog.Manifest.BaseRules.Stack.CardCount

        for player in builder.Players |> Seq.map (fun player -> player.Id) |> Seq.toArray do
            let cards =
                builder.Cards |> Seq.filter (fun card -> card.Owner = player) |> Seq.toArray

            if cards.Length <> expectedCardCount then
                invalidOp
                    $"Cannot start Sudden Death for {player.Value}: expected {expectedCardCount} cards, but the match contains {cards.Length}."

            if
                cards
                |> Array.exists (fun card ->
                    card.Kind = CardKind.Bloke && catalog.IsRegular card.MechanicalId)
                |> not
            then
                invalidOp
                    $"Cannot start Sudden Death for {player.Value}: the reconstructed deck contains no Basic Pokemon."

    let dealOpeningMitts (catalog: AuthorityCatalog) (builder: MatchBuilder) =
        for player in builder.Players |> Seq.map (fun player -> player.Id) |> Seq.toArray do
            builder.Shuffle player

        for player in builder.Players |> Seq.map (fun player -> player.Id) |> Seq.toArray do
            builder.Draw(
                player,
                catalog.Manifest.BaseRules.Opening.MittSize,
                DrawReason.OpeningMitt
            )
            |> ignore

        let mutable settled = false

        while not settled do
            let mulliganPlayers =
                builder.Players
                |> Seq.map (fun player -> player.Id)
                |> Seq.filter (fun player ->
                    builder.CardsIn(player, CardZone.Mitt)
                    |> Seq.exists (fun card ->
                        card.Kind = CardKind.Bloke && catalog.IsRegular card.MechanicalId)
                    |> not)
                |> Seq.toArray

            if mulliganPlayers.Length = 0 then
                settled <- true
            else
                for player in mulliganPlayers do
                    // A mulligan is public. The rules have the player show the hand to their
                    // opponent before it goes back, and the extra card the opponent is owed is the
                    // other half of that same rule - the bonus was being settled below while the
                    // hand it is compensation for was never shown at all, so a player learned that
                    // a mulligan had happened and nothing whatever about what was in it.
                    //
                    // It is said before the cards move, because after this they are back in the
                    // Deck and there is no hand left to name.
                    builder.Events.Add
                        { PendingMatchEvent.forActor MatchEventKind.CardsRevealed player with
                            TargetCards =
                                builder.CardsIn(player, CardZone.Mitt)
                                |> Seq.map (fun card -> card.Id)
                                |> ImmutableArray.CreateRange }

                    builder.ReturnMittToStack player
                    let state = builder.Player player

                    builder.SetPlayer
                        { state with
                            MulliganCount = state.MulliganCount + 1 }

                for player in mulliganPlayers do
                    builder.Shuffle player

                for player in mulliganPlayers do
                    builder.Draw(
                        player,
                        catalog.Manifest.BaseRules.Opening.MittSize,
                        DrawReason.OpeningMitt
                    )
                    |> ignore

    let assignMulliganBonuses (builder: MatchBuilder) =
        let players = builder.Players |> Seq.toArray

        for player in players do
            let other = players |> Array.find (fun candidate -> candidate.Id <> player.Id)
            let allowance = max 0 (other.MulliganCount - player.MulliganCount)

            builder.SetPlayer
                { player with
                    MulliganBonusAllowance = allowance
                    MulliganBonusChosen = allowance = 0 }

        // The bonus is compensation drawn against an Active that is already standing, so it comes
        // after the placement rather than before it. Drawn first, it was seen before the single
        // most consequential choice of the opening.
        builder.Phase <- MatchPhase.OpeningPlacement

    let startFreshGame (catalog: AuthorityCatalog) (builder: MatchBuilder) =
        ensureFreshGameDecksAreValid catalog builder
        let prizeCards = catalog.Manifest.BaseRules.Win.SuddenDeathPrizeCards
        builder.ResetForFreshGame prizeCards
        let players = builder.Players |> Seq.map (fun player -> player.Id) |> Seq.toArray
        let openingPlayer = players[builder.Random.NextInt players.Length]
        builder.OpeningPlayer <- openingPlayer
        builder.ActivePlayer <- openingPlayer
        builder.RoundUsage <- RoundUsage.Empty openingPlayer
        builder.Phase <- MatchPhase.OpeningPlacement
        dealOpeningMitts catalog builder
        assignMulliganBonuses builder

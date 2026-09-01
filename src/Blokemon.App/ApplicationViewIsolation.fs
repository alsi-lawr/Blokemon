namespace Blokemon.App

open Blokemon.App.Contracts

module internal ApplicationViewIsolation =

    let private strings (values: string array) = Array.copy values

    let private error (value: ApiError | null) =
        match value with
        | null -> null
        | current -> ApiError(current.Code, current.Message)

    let private profile (value: ProfileView | null) =
        match value with
        | null -> null
        | current ->
            ProfileView(
                current.Id,
                current.DisplayName,
                current.Revision,
                current.StarterDeckId,
                current.RemainingPacks,
                current.StarterClaimUsed
            )

    let private cardRule (value: CardRuleView) =
        CardRuleView(value.Kind, value.Name, value.Text, strings value.EnergyCost, value.Damage)

    let private card (value: CardView) =
        CardView(
            value.Id,
            value.Name,
            value.Kind,
            value.Type,
            value.Detail,
            value.FaceHtml,
            value.Rules |> Array.map cardRule,
            value.OwnedQuantity,
            value.FreelyAvailable
        )

    let private deckEntry (value: DeckEntryView) =
        DeckEntryView(value.CardId, value.Quantity)

    let private deck (value: DeckView) =
        DeckView(
            value.Id,
            value.Name,
            value.Revision,
            value.Entries |> Array.map deckEntry,
            value.IsLegal,
            strings value.Errors,
            strings value.Warnings
        )

    let private starter (value: StarterDeckView) =
        StarterDeckView(
            value.Id,
            value.Name,
            value.Type,
            value.Role,
            value.Description,
            card value.Leader,
            value.Entries |> Array.map deckEntry,
            value.BlokemonCount,
            value.TrainerCount,
            value.EnergyCount,
            value.IsClaimed
        )

    let private packStock (value: PackStockPresentationView) =
        PackStockPresentationView(
            value.BoosterSvgMarkup,
            value.StarterDeckSvgMarkup,
            value.StarterDeckTraySvgMarkup
        )

    let private packPresentation (value: PackPresentationView) =
        PackPresentationView(packStock value.Gloss, packStock value.Kraft)

    let private lastPack (value: PackReceiptView | null) =
        match value with
        | null -> null
        | current -> PackReceiptView(current.Id, current.Sequence, current.Cards |> Array.map card)

    let private matchCard (value: MatchCardInstanceView) =
        MatchCardInstanceView(
            value.Id,
            card value.Card,
            value.OwnerName,
            value.Zone,
            value.Damage,
            value.HitPoints,
            value.AttachedEnergy |> Array.map card,
            value.AttachedTools |> Array.map card,
            value.UnderlyingCards |> Array.map card,
            strings value.Conditions
        )

    let private matchSide (value: MatchSideView) =
        MatchSideView(
            value.Name,
            value.DeckName,
            value.DeckCount,
            value.HandCount,
            value.PrizeCards,
            (match value.Active with
             | null -> null
             | active -> matchCard active),
            value.Bench |> Array.map matchCard,
            value.Hand |> Array.map matchCard,
            value.InPlayKits |> Array.map matchCard,
            value.EmptiesTray |> Array.map matchCard,
            value.HasTurn
        )

    let private matchFrame (value: MatchFrameView) =
        MatchFrameView(
            value.Id,
            value.Revision,
            value.Round,
            value.Phase,
            matchSide value.Opponent,
            matchSide value.Player,
            value.IsComplete,
            value.Winner
        )

    let private chooser (value: MatchChooserView) =
        MatchChooserView(value.Id, value.Name, value.IsLocalPlayer)

    let private mechanicalType (value: MatchMechanicalTypeOptionView) =
        MatchMechanicalTypeOptionView(value.Value, value.Label)

    let private effect (value: MatchEffectOptionView) =
        MatchEffectOptionView(value.Id, value.Label)

    let private cardTypes (value: MatchCardTypesView) =
        MatchCardTypesView(value.CardInstanceId, strings value.MechanicalTypes)

    let private requirement (value: MatchChoiceRequirementView) =
        MatchChoiceRequirementView(
            value.Id,
            value.Kind,
            value.Label,
            chooser value.Chooser,
            value.Minimum,
            value.Maximum,
            value.EligibleCards |> Array.map matchCard,
            value.EligibleMechanicalTypes |> Array.map mechanicalType,
            value.EligibleEffects |> Array.map effect,
            value.DependsOnOptional,
            value.EligibleTargets |> Array.map matchCard,
            value.RequireDifferentMechanicalTypes,
            value.EligibleCardTypes |> Array.map cardTypes
        )

    let private action (value: MatchActionView) =
        MatchActionView(
            value.Id,
            value.Kind,
            value.Label,
            value.Primary,
            value.SourceCardInstanceId,
            value.TargetCardInstanceId,
            value.EffectId,
            value.ChoiceRequirements |> Array.map requirement,
            value.DisabledReason
        )

    let private attack (value: MatchAttackView) =
        MatchAttackView(
            value.SourceCardInstanceId,
            value.EffectId,
            value.Name,
            strings value.EnergyCost,
            value.PrintedDamage,
            value.ActionId,
            value.DisabledReason
        )

    let matchView (value: MatchView | null) =
        match value with
        | null -> null
        | current ->
            MatchView(
                matchFrame current.Frame,
                current.LegalActions |> Array.map action,
                current.Attacks |> Array.map attack,
                strings current.RecentEvents,
                current.Difficulty
            )

    let private eventCue (value: MatchEventCueView) =
        MatchEventCueView(
            value.Sequence,
            value.Kind,
            value.Label,
            value.SourceCardInstanceId,
            strings value.TargetCardInstanceIds,
            value.Amount,
            value.BadgeSide,
            value.ActorIsLocalPlayer,
            value.RevealedCards |> Array.map card
        )

    let presentation (value: MatchPresentationView | null) =
        match value with
        | null -> null
        | current ->
            MatchPresentationView(
                current.Steps
                |> Array.map (fun step ->
                    MatchPresentationStepView(
                        matchFrame step.Frame,
                        step.Events |> Array.map eventCue
                    ))
            )

    let application (value: ApplicationView) =
        ApplicationView(
            profile value.Profile,
            value.Cards |> Array.map card,
            value.Decks |> Array.map deck,
            value.StarterDecks |> Array.map starter,
            packPresentation value.PackPresentation,
            lastPack value.LastPack,
            matchView value.Match,
            error value.MatchError,
            (match value.MatchRecovery with
             | null -> null
             | current -> MatchRecoveryView(current.Kind, current.Revision, current.ContentIdentity))
        )

    let matchResult (value: MatchProjectionResult) : MatchServiceResult =
        { View = matchView value.View
          Error = error value.Error
          Presentation = presentation value.Presentation }

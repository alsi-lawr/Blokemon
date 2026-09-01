namespace Blokemon.App

open System
open System.Diagnostics
open System.Text
open Blokemon.App.Contracts
open Blokemon.Game

/// The player-facing wording and the view enumerations, plus the event predicates the cue
/// projection reads. Nothing here touches the catalogue or the engine.
module internal MatchLabels =

    let humanize (value: string) =
        let result = StringBuilder(value.Length + 8)

        for index in 0 .. value.Length - 1 do
            if index > 0 && Char.IsUpper value[index] && not (Char.IsUpper value[index - 1]) then
                result.Append ' ' |> ignore

            result.Append value[index] |> ignore

        result.ToString()

    /// Which phase the match is in, carried as the state itself. The words for it belong to the
    /// surface that names it, because what the table says depends on whose turn it is as well as
    /// on the phase, and neither fact means anything to the player without the other.
    let phaseView (phase: MatchPhase) =
        match phase with
        | MatchPhase.MulliganBonus -> MatchPhaseView.MulliganBonus
        | MatchPhase.OpeningPlacement -> MatchPhaseView.OpeningPlacement
        | MatchPhase.Playing -> MatchPhaseView.Playing
        | MatchPhase.AwaitingEffectChoice -> MatchPhaseView.AwaitingEffectChoice
        | MatchPhase.AwaitingTriggerChoice -> MatchPhaseView.AwaitingTriggerChoice
        | MatchPhase.AwaitingReplacement -> MatchPhaseView.AwaitingReplacement
        | MatchPhase.Complete -> MatchPhaseView.Complete
        | MatchPhase.BonusPlacement -> MatchPhaseView.BonusPlacement
        | _ -> raise (UnreachableException())

    let cardChoiceLabel (minimum: int) (maximum: int) =
        if minimum = maximum then
            $"""Choose {minimum} {if minimum = 1 then "card" else "cards"}"""
        elif minimum = 0 then
            $"""Choose up to {maximum} {if maximum = 1 then "card" else "cards"}"""
        else
            $"Choose {minimum} to {maximum} cards"

    let requirementLabel (requirement: ChoiceRequirement) =
        match requirement.Kind with
        | ChoiceRequirementKind.Amount ->
            $"Choose an amount from {requirement.Minimum} to {requirement.Maximum}"
        | ChoiceRequirementKind.Cards when requirement.Id.Value = "opening:booth" ->
            "Choose Blokemon for the Bench"
        | ChoiceRequirementKind.Cards -> cardChoiceLabel requirement.Minimum requirement.Maximum
        | ChoiceRequirementKind.MechanicalType -> "Choose an Energy type"
        | ChoiceRequirementKind.Attack -> "Choose an attack"
        | ChoiceRequirementKind.Attachments ->
            $"""Choose targets for {requirement.Minimum} Energy {if requirement.Minimum = 1 then "card" else "cards"}"""
        | _ -> raise (UnreachableException())

    let choiceKind (kind: ChoiceRequirementKind) =
        match kind with
        | ChoiceRequirementKind.Amount -> MatchChoiceKindView.Amount
        | ChoiceRequirementKind.Cards -> MatchChoiceKindView.Cards
        | ChoiceRequirementKind.MechanicalType -> MatchChoiceKindView.MechanicalType
        | ChoiceRequirementKind.Attack -> MatchChoiceKindView.Attack
        | ChoiceRequirementKind.Attachments -> MatchChoiceKindView.Attachments
        | _ -> raise (UnreachableException())

    let actionKind (kind: LegalActionKind) =
        match kind with
        | LegalActionKind.ChooseMulliganBonus -> MatchActionKindView.ChooseMulliganBonus
        | LegalActionKind.ChooseOpening -> MatchActionKindView.ChooseOpening
        | LegalActionKind.ChooseBonusPlacement -> MatchActionKindView.ChooseBonusPlacement
        | LegalActionKind.ChooseReplacement -> MatchActionKindView.ChooseReplacement
        | LegalActionKind.AttachVim -> MatchActionKindView.AttachEnergy
        | LegalActionKind.PlayBloke -> MatchActionKindView.PlayBlokemon
        | LegalActionKind.Promote -> MatchActionKindView.Evolve
        | LegalActionKind.PlayKit -> MatchActionKindView.PlayTrainer
        | LegalActionKind.UsePartyTrick -> MatchActionKindView.UsePokemonPower
        | LegalActionKind.Attack -> MatchActionKindView.Attack
        | LegalActionKind.Taxi -> MatchActionKindView.Retreat
        | LegalActionKind.ChuckFossil -> MatchActionKindView.DiscardFossil
        | LegalActionKind.EndRound -> MatchActionKindView.EndTurn
        | LegalActionKind.ResolveEffectChoice -> MatchActionKindView.ResolveChoice
        | LegalActionKind.ResolveKnockoutTrigger -> MatchActionKindView.ResolveKnockout
        | LegalActionKind.ResolveBarChitTrigger -> MatchActionKindView.TakePrize
        | LegalActionKind.Resign -> MatchActionKindView.Resign
        | _ -> raise (UnreachableException())


    let isPublicEvent (matchEvent: MatchEvent) =
        match matchEvent.Kind with
        | MatchEventKind.MatchStarted
        | MatchEventKind.CommandApplied
        | MatchEventKind.CardsShuffled
        | MatchEventKind.CardsDrawn
        | MatchEventKind.CardsRevealed
        | MatchEventKind.BeerMatTossed
        | MatchEventKind.DamagePlaced
        | MatchEventKind.DamageHealed
        | MatchEventKind.RoughStateApplied
        | MatchEventKind.RoughStateCleared
        | MatchEventKind.AttackDeclared
        | MatchEventKind.AttackCancelled
        | MatchEventKind.BlokeSentHome
        | MatchEventKind.BarChitsTaken
        | MatchEventKind.RoundStarted
        | MatchEventKind.RoundEnded
        | MatchEventKind.SuddenDeathStarted
        | MatchEventKind.OcheSwapped
        | MatchEventKind.MatchWon -> true
        | _ -> false

    let animationKind (matchEvent: MatchEvent) =
        match matchEvent.Kind with
        | MatchEventKind.MatchStarted -> Nullable MatchAnimationKindView.Setup
        | MatchEventKind.CardsShuffled -> Nullable MatchAnimationKindView.Shuffle
        | MatchEventKind.CardsDrawn -> Nullable MatchAnimationKindView.Draw
        | MatchEventKind.CardsRevealed -> Nullable MatchAnimationKindView.Reveal
        | MatchEventKind.BeerMatTossed -> Nullable MatchAnimationKindView.Coin
        | MatchEventKind.CommandApplied ->
            match matchEvent.Command with
            | ValueSome command ->
                match command.Action with
                | MatchAction.ChooseOpening _ -> Nullable MatchAnimationKindView.Setup
                | MatchAction.AttachVim _ -> Nullable MatchAnimationKindView.Attach
                | MatchAction.Promote _ -> Nullable MatchAnimationKindView.Evolve
                | MatchAction.PlayBloke _
                | MatchAction.PlayKit _
                | MatchAction.UsePartyTrick _
                | MatchAction.Taxi _ -> Nullable MatchAnimationKindView.Play
                | _ -> Nullable()
            | ValueNone -> Nullable()
        | MatchEventKind.AttackDeclared -> Nullable MatchAnimationKindView.Attack
        | MatchEventKind.DamagePlaced -> Nullable MatchAnimationKindView.Damage
        | MatchEventKind.DamageHealed -> Nullable MatchAnimationKindView.Heal
        | MatchEventKind.RoughStateApplied
        | MatchEventKind.RoughStateCleared -> Nullable MatchAnimationKindView.Condition
        | MatchEventKind.BlokeSentHome -> Nullable MatchAnimationKindView.Knockout
        | MatchEventKind.BarChitsTaken -> Nullable MatchAnimationKindView.Prize
        | MatchEventKind.RoundStarted -> Nullable MatchAnimationKindView.Turn
        // The table has nothing of its own to show for a swap - both cards are already standing on
        // it and only trade places - so this takes the plain toast every other unremarkable cue
        // takes, and says in words the one thing the board changing cannot say by itself.
        | MatchEventKind.OcheSwapped -> Nullable MatchAnimationKindView.Other
        | MatchEventKind.MatchWon -> Nullable MatchAnimationKindView.Victory
        | _ -> Nullable()

    let commandSource (command: MatchCommand) =
        match command.Action with
        | MatchAction.ChooseOpening(oche, _) -> ValueSome oche
        | MatchAction.AttachVim(vim, _) -> ValueSome vim
        | MatchAction.PlayBloke bloke -> ValueSome bloke
        | MatchAction.Promote(promotion, _) -> ValueSome promotion
        | MatchAction.PlayKit(kit, _) -> ValueSome kit
        | MatchAction.Taxi(boothBloke, _) -> ValueSome boothBloke
        | MatchAction.UsePartyTrick(source, _) -> ValueSome source
        | MatchAction.Attack(attacker, _) -> ValueSome attacker
        | MatchAction.ChuckFossil fossil -> ValueSome fossil
        | _ -> ValueNone

    let attackDisabledReason (state: MatchState) (human: PlayerId) =
        if state.Phase <> MatchPhase.Playing then
            "Complete setup before attacking."
        elif state.ActivePlayer <> human then
            "Wait for your turn."
        else
            "Attach the required Energy or satisfy the attack's requirements."

    /// Why a move the table still offers cannot be taken. The engine keeps an action nobody can
    /// pay for in the legal set precisely so the cost can be said out loud rather than the move
    /// disappearing without explanation.
    let actionDisabledReason (action: LegalAction) : string | null =
        match action.Affordability with
        | ActionAffordability.Payable -> null
        | ActionAffordability.ShortOfTaxiFare fare -> $"Needs {fare} Vim attached to retreat."

    let resolvedAttackDamage (attack: MatchEvent) (stepEvents: MatchEvent seq) =
        stepEvents
        |> Seq.filter (fun matchEvent ->
            matchEvent.Sequence >= attack.Sequence
            && matchEvent.Kind = MatchEventKind.DamagePlaced
            && matchEvent.Actor = attack.Actor
            && matchEvent.SourceCard = attack.SourceCard
            && (matchEvent.DamageKind = ValueSome DamageKind.Attack
                || matchEvent.DamageKind = ValueSome DamageKind.BoothAttack))
        |> Seq.sumBy _.Amount

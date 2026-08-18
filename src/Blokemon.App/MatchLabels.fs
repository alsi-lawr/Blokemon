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

    let phaseLabel (phase: MatchPhase) =
        match phase with
        | MatchPhase.MulliganBonus -> "Extra draw"
        | MatchPhase.OpeningPlacement -> "Choose starting Blokemon"
        | MatchPhase.Playing -> "Battle"
        | MatchPhase.AwaitingEffectChoice -> "Choose an effect"
        | MatchPhase.AwaitingTriggerChoice -> "Make a required choice"
        | MatchPhase.AwaitingReplacement -> "Choose replacement"
        | MatchPhase.Complete -> "Complete"
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
        | ChoiceRequirementKind.Optional -> "Use this effect?"
        | ChoiceRequirementKind.Amount ->
            $"Choose an amount from {requirement.Minimum} to {requirement.Maximum}"
        | ChoiceRequirementKind.Cards when requirement.Id.Value = "opening:booth" ->
            "Choose Blokemon for the Bench"
        | ChoiceRequirementKind.Cards -> cardChoiceLabel requirement.Minimum requirement.Maximum
        | ChoiceRequirementKind.MechanicalType -> "Choose an Energy type"
        | ChoiceRequirementKind.Attack -> "Choose an attack"
        | ChoiceRequirementKind.Distribution ->
            $"""Place {requirement.Maximum} damage {if requirement.Maximum = 1 then "counter" else "counters"}"""
        | ChoiceRequirementKind.Attachments ->
            $"""Choose targets for {requirement.Minimum} Energy {if requirement.Minimum = 1 then "card" else "cards"}"""
        | _ -> raise (UnreachableException())

    let choiceKind (kind: ChoiceRequirementKind) =
        match kind with
        | ChoiceRequirementKind.Optional -> MatchChoiceKindView.Optional
        | ChoiceRequirementKind.Amount -> MatchChoiceKindView.Amount
        | ChoiceRequirementKind.Cards -> MatchChoiceKindView.Cards
        | ChoiceRequirementKind.MechanicalType -> MatchChoiceKindView.MechanicalType
        | ChoiceRequirementKind.Attack -> MatchChoiceKindView.Attack
        | ChoiceRequirementKind.Distribution -> MatchChoiceKindView.Distribution
        | ChoiceRequirementKind.Attachments -> MatchChoiceKindView.Attachments
        | _ -> raise (UnreachableException())

    let actionKind (kind: LegalActionKind) =
        match kind with
        | LegalActionKind.ChooseMulliganBonus -> MatchActionKindView.ChooseMulliganBonus
        | LegalActionKind.ChooseOpening -> MatchActionKindView.ChooseOpening
        | LegalActionKind.ChooseReplacement -> MatchActionKindView.ChooseReplacement
        | LegalActionKind.AttachVim -> MatchActionKindView.AttachEnergy
        | LegalActionKind.PlayBloke -> MatchActionKindView.PlayBlokemon
        | LegalActionKind.Promote -> MatchActionKindView.Evolve
        | LegalActionKind.PlayKit -> MatchActionKindView.PlayTrainer
        | LegalActionKind.UsePartyTrick -> MatchActionKindView.UseAbility
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

    let canReveal (state: MatchState) (human: PlayerId) (cardId: CardInstanceId) =
        let card = state.Card cardId

        card.Owner = human
        || match card.Zone with
           | CardZone.Oche
           | CardZone.Booth
           | CardZone.Attached
           | CardZone.EmptiesTray -> true
           | _ -> false

    let attackDisabledReason (state: MatchState) (human: PlayerId) =
        if state.Phase <> MatchPhase.Playing then
            "Complete setup before attacking."
        elif state.ActivePlayer <> human then
            "Wait for your turn."
        else
            "Attach the required Energy or satisfy the attack's requirements."

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

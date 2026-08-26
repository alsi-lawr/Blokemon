namespace Blokemon.Differential.Tests

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open Blokemon.ReferenceModel

type ProductionZoneCountInput =
    { Owner: PlayerId
      Zone: CardZone
      Count: int }

type ProductionChoiceInput =
    { Choice: EffectChoice
      WhenAvailable: bool }

type ProductionActionInput =
    { Command: MatchCommand
      DeclaredTargetCard: string
      DeclaredEffectId: string
      Choices: ProductionChoiceInput array }

type ProductionObligationInput =
    { Id: string
      ProgramKey: string
      Route: string
      Parameters: string array
      Cards: CardState array
      ZoneCounts: ProductionZoneCountInput array
      Players: PlayerState array
      Actions: ProductionActionInput array
      Seed: MatchSeed }

[<RequireQualifiedAccess>]
module ProductionSetup =

    let private zone value =
        match value with
        | ReferenceZone.Stack -> CardZone.Stack
        | ReferenceZone.Mitt -> CardZone.Mitt
        | ReferenceZone.Oche -> CardZone.Oche
        | ReferenceZone.Booth -> CardZone.Booth
        | ReferenceZone.Attached -> CardZone.Attached
        | ReferenceZone.EmptiesTray -> CardZone.EmptiesTray
        | ReferenceZone.Local -> CardZone.Local
        | ReferenceZone.BarChit -> CardZone.BarChit
        | other -> invalidOp $"Unsupported reference zone {other}."

    let private kind (mechanicalId: string) =
        if mechanicalId.StartsWith("VIM-", StringComparison.Ordinal) then
            CardKind.Vim
        elif mechanicalId.StartsWith("KIT-", StringComparison.Ordinal) then
            CardKind.Kit
        else
            CardKind.Bloke

    let private choice (input: ReferenceChoiceInput) =
        let id = EffectChoiceId input.RequirementId

        let value =
            match input.Value with
            | ReferenceChoiceValue.Optional accepted -> EffectChoice.Optional(id, accepted)
            | ReferenceChoiceValue.Amount amount -> EffectChoice.Amount(id, amount)
            | ReferenceChoiceValue.Cards cards ->
                EffectChoice.Cards(
                    id,
                    cards |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
                )
            | ReferenceChoiceValue.MechanicalType mechanicalType ->
                EffectChoice.MechanicalType(
                    id,
                    Enum.Parse<BlokemonMechanicalType>(string mechanicalType)
                )
            | ReferenceChoiceValue.Attack effect -> EffectChoice.Attack(id, EffectId effect)
            | ReferenceChoiceValue.Distribution allocations ->
                EffectChoice.Distribution(
                    id,
                    allocations
                    |> Seq.map (fun allocation ->
                        ({ Card = CardInstanceId allocation.Card
                           Counters = allocation.Counters }
                        : DamageAllocation))
                    |> ImmutableArray.CreateRange
                )
            | ReferenceChoiceValue.Attachments placements ->
                EffectChoice.Attachments(
                    id,
                    placements
                    |> Seq.map (fun placement ->
                        ({ Vim = CardInstanceId placement.Vim
                           Bloke = CardInstanceId placement.Bloke }
                        : VimAttachment))
                    |> ImmutableArray.CreateRange
                )

        { Choice = value
          WhenAvailable = input.WhenAvailable }

    let private action matchId index (input: ReferenceActionInput) =
        let action =
            match input.Kind with
            | ReferenceInputActionKind.Attack ->
                MatchAction.Attack(CardInstanceId input.SourceCard, EffectId input.EffectId)
            | ReferenceInputActionKind.EndRound -> MatchAction.EndRound
            | ReferenceInputActionKind.UsePartyTrick ->
                MatchAction.UsePartyTrick(CardInstanceId input.SourceCard, EffectId input.EffectId)
            | ReferenceInputActionKind.Promote ->
                MatchAction.Promote(
                    CardInstanceId input.SourceCard,
                    CardInstanceId input.TargetCard
                )
            | ReferenceInputActionKind.PlayKit ->
                MatchAction.PlayKit(
                    CardInstanceId input.SourceCard,
                    if String.IsNullOrEmpty input.TargetCard then
                        ValueNone
                    else
                        ValueSome(CardInstanceId input.TargetCard)
                )
            | ReferenceInputActionKind.ResolveKnockoutTrigger ->
                MatchAction.ResolveKnockoutTrigger(
                    if String.IsNullOrEmpty input.TargetCard then
                        ValueNone
                    else
                        ValueSome(CardInstanceId input.TargetCard)
                )
            | ReferenceInputActionKind.ResolveBarChitTrigger ->
                MatchAction.ResolveBarChitTrigger(input.TargetCard = "Booth")
            | other -> invalidOp $"Unsupported reference action input {other}."

        let choices = input.Choices |> Array.map choice

        { Command =
            { Id = CommandId $"obligation:{index}"
              MatchId = matchId
              Actor = PlayerId input.Actor
              ExpectedRevision = MatchRevision(int64 index)
              Choices = ImmutableArray<_>.Empty
              Action = action }
          DeclaredTargetCard = input.TargetCard
          DeclaredEffectId = input.EffectId
          Choices = choices }

    let materialize (input: ReferenceObligationInput) =
        let matchId = MatchId $"obligation:{input.Id}"

        { Id = input.Id
          ProgramKey = input.ProgramKey
          Route = input.InitialState.Route.Value
          Parameters = input.InitialState.Parameters
          Cards =
            input.InitialState.Cards
            |> Array.mapi (fun index card ->
                { Id = CardInstanceId card.CardId
                  MechanicalId = MechanicalCardId card.MechanicalId
                  Owner = PlayerId card.Owner
                  Kind = kind card.MechanicalId
                  Zone = zone card.Zone
                  IsFaceDown = card.Zone = ReferenceZone.BarChit
                  StackPosition = index
                  AttachedTo = ValueNone
                  Attachments = ImmutableArray<_>.Empty
                  UnderlyingCards = ImmutableArray<_>.Empty
                  Damage = 0
                  RoughStates = ImmutableArray<_>.Empty
                  EnteredAtOwnerRound = 0
                  LastPromotedRound = -1 })
          ZoneCounts =
            input.InitialState.ZoneCounts
            |> Array.map (fun count ->
                { Owner = PlayerId count.Owner
                  Zone = zone count.Zone
                  Count = count.Count })
          Players =
            input.InitialState.Players
            |> Array.map (fun player ->
                { Id = PlayerId player.Player
                  BarChitsRemaining = player.BarChitsRemaining
                  MulliganCount = 0
                  MulliganBonusAllowance = 0
                  MulliganBonusChosen = true
                  BonusDrawn = ImmutableArray<_>.Empty
                  BonusPlacementChosen = true
                  OpeningChosen = true
                  RoundsStarted = 1 })
          Actions = input.Actions |> Array.mapi (fun index value -> action matchId index value)
          Seed = MatchSeed input.RandomSeed }

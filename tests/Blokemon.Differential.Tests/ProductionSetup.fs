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

    let private ids (text: string) =
        text.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Seq.map CardInstanceId
        |> ImmutableArray.CreateRange

    let commonCommand (selected: CanonicalAction) =
        if selected.Choices.Length <> 0 then
            invalidOp "Common-foundation commands cannot dispatch program choices."

        let action =
            match selected.Kind with
            | "AttachVim" ->
                let parts = selected.Payload.Split(';')

                MatchAction.AttachVim(
                    CardInstanceId(parts[0].Substring(4)),
                    CardInstanceId(parts[1].Substring(7))
                )
            | "PlayBloke" -> MatchAction.PlayBloke(CardInstanceId(selected.Payload.Substring(6)))
            | "Promote" ->
                let parts = selected.Payload.Split(';')

                MatchAction.Promote(
                    CardInstanceId(parts[0].Substring(10)),
                    CardInstanceId(parts[1].Substring(9))
                )
            | "Attack" ->
                let parts = selected.Payload.Split(';')

                MatchAction.Attack(
                    CardInstanceId(parts[0].Substring(9)),
                    EffectId(parts[1].Substring(7))
                )
            | "Taxi" ->
                let parts = selected.Payload.Split(';')

                MatchAction.Taxi(CardInstanceId(parts[0].Substring(6)), ids (parts[1].Substring(4)))
            | "ChuckFossil" ->
                MatchAction.ChuckFossil(CardInstanceId(selected.Payload.Substring(7)))
            | "EndRound" -> MatchAction.EndRound
            | "ChooseReplacement" ->
                MatchAction.ChooseReplacement(CardInstanceId(selected.Payload.Substring(12)))
            | "Resign" -> MatchAction.Resign
            | other -> invalidOp $"Unsupported common-foundation production action {other}."

        { Id = CommandId selected.CommandId
          MatchId = MatchId selected.MatchId
          Actor = PlayerId selected.Actor
          ExpectedRevision = MatchRevision selected.ExpectedRevision
          Choices = ImmutableArray<_>.Empty
          Action = action }

    let commonState (value: CanonicalState) : MatchState =
        let optionText (projection: string -> 'value) (text: string) : 'value voption =
            if String.IsNullOrEmpty text then
                ValueNone
            else
                ValueSome(projection text)

        let pendingEffect: PendingEffectResolution voption =
            if value.PendingEffect.Present then
                if value.PendingEffect.Action.Length <> 1 then
                    invalidOp
                        "A canonical pending effect must retain exactly one suspended command."

                ValueSome
                    { Command = commonCommand value.PendingEffect.Action[0]
                      Source = CardInstanceId value.PendingEffect.Source
                      Effect = EffectId value.PendingEffect.Effect
                      Chooser = PlayerId value.PendingEffect.Chooser
                      Requirements = ImmutableArray<_>.Empty
                      BeerMatResults = ImmutableArray.CreateRange value.PendingEffect.BeerMatResults
                      AttackStarted = value.PendingEffect.AttackStarted }
            else
                ValueNone

        let pendingKnockout: PendingKnockoutResolution voption =
            if value.PendingKnockout.Present then
                ValueSome
                    { KnockedOutCard = CardInstanceId value.PendingKnockout.KnockedOutCard
                      RemainingKnockouts =
                        value.PendingKnockout.RemainingKnockouts
                        |> Seq.map CardInstanceId
                        |> ImmutableArray.CreateRange
                      TriggerSources =
                        value.PendingKnockout.TriggerSources
                        |> Seq.map CardInstanceId
                        |> ImmutableArray.CreateRange
                      TriggerSource = CardInstanceId value.PendingKnockout.TriggerSource
                      TriggerEffect = EffectId value.PendingKnockout.TriggerEffect
                      Chooser = PlayerId value.PendingKnockout.Chooser
                      EligibleVim =
                        value.PendingKnockout.EligibleVim
                        |> Seq.map CardInstanceId
                        |> ImmutableArray.CreateRange
                      AttackingCard = CardInstanceId value.PendingKnockout.AttackingCard
                      FinishRoundAfterResolution = value.PendingKnockout.FinishRoundAfterResolution
                      AttackDamageTargets =
                        value.PendingKnockout.AttackDamageTargets
                        |> Seq.map CardInstanceId
                        |> ImmutableArray.CreateRange
                      ExtraBarChits = value.PendingKnockout.ExtraBarChits }
            else
                ValueNone

        let players =
            value.Players
            |> Seq.map (fun player ->
                ({ Id = PlayerId player.Id
                   BarChitsRemaining = player.BarChitsRemaining
                   MulliganCount = player.MulliganCount
                   MulliganBonusAllowance = player.MulliganBonusAllowance
                   MulliganBonusChosen = player.MulliganBonusChosen
                   BonusDrawn =
                     player.BonusDrawn |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
                   BonusPlacementChosen = player.BonusPlacementChosen
                   OpeningChosen = player.OpeningChosen
                   RoundsStarted = player.RoundsStarted }
                : PlayerState))
            |> ImmutableArray.CreateRange

        let cards =
            value.Cards
            |> Seq.map (fun card ->
                let roughStates =
                    card.RoughStates
                    |> Seq.map (fun rough ->
                        ({ State = Enum.Parse<BlokemonRoughState>(rough.State)
                           AppliedAtOwnerRound = rough.AppliedAtOwnerRound }
                        : RoughStateEntry))
                    |> ImmutableArray.CreateRange

                ({ Id = CardInstanceId card.Id
                   MechanicalId = MechanicalCardId card.MechanicalId
                   Owner = PlayerId card.Owner
                   Kind = Enum.Parse<CardKind>(card.Kind)
                   Zone = Enum.Parse<CardZone>(card.Zone)
                   IsFaceDown = card.IsFaceDown
                   StackPosition = card.StackPosition
                   AttachedTo = card.AttachedTo |> optionText CardInstanceId
                   Attachments =
                     card.Attachments |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
                   UnderlyingCards =
                     card.UnderlyingCards |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
                   Damage = card.Damage
                   RoughStates = roughStates
                   EnteredAtOwnerRound = card.EnteredAtOwnerRound
                   LastPromotedRound = card.LastPromotedRound }
                : CardState))
            |> ImmutableArray.CreateRange

        let effects =
            value.Effects
            |> Seq.map (fun effect ->
                ({ SourceEffect = EffectId effect.SourceEffect
                   SourceCard = CardInstanceId effect.SourceCard
                   Owner = PlayerId effect.Owner
                   TargetCard = effect.TargetCard |> optionText CardInstanceId
                   Kind = Enum.Parse<TemporaryEffectKind>(effect.Kind)
                   Amount = effect.Amount
                   MechanicalTypes =
                     effect.MechanicalTypes
                     |> Seq.map (fun item -> Enum.Parse<BlokemonMechanicalType>(item))
                     |> ImmutableArray.CreateRange
                   RoughStates =
                     effect.RoughStates
                     |> Seq.map (fun item -> Enum.Parse<BlokemonRoughState>(item))
                     |> ImmutableArray.CreateRange
                   RelatedCards =
                     effect.RelatedCards |> Seq.map MechanicalCardId |> ImmutableArray.CreateRange
                   Conditions =
                     effect.Conditions
                     |> Seq.map (fun item -> Enum.Parse<BlokemonCondition>(item))
                     |> ImmutableArray.CreateRange
                   Duration = Enum.Parse<EffectDuration>(effect.Duration)
                   AppliesFromRound = effect.AppliesFromRound
                   ExpiresAfterRound = effect.ExpiresAfterRound }
                : TemporaryEffect))
            |> ImmutableArray.CreateRange

        let roundUsage: RoundUsage =
            { Player = PlayerId value.RoundUsage.Player
              VimAttachments = value.RoundUsage.VimAttachments
              MatesPlayed = value.RoundUsage.MatesPlayed
              LocalsPlayed = value.RoundUsage.LocalsPlayed
              TaxisUsed = value.RoundUsage.TaxisUsed
              EffectsUsed =
                value.RoundUsage.EffectsUsed |> Seq.map EffectId |> ImmutableArray.CreateRange
              KitsPlayed =
                value.RoundUsage.KitsPlayed
                |> Seq.map MechanicalCardId
                |> ImmutableArray.CreateRange }

        let pendingBarChits =
            value.PendingBarChits
            |> Seq.map (fun pending ->
                ({ Player = PlayerId pending.Player
                   Card = CardInstanceId pending.Card
                   Effect = EffectId pending.Effect
                   FinishRoundAfterResolution = pending.FinishRoundAfterResolution }
                : PendingBarChitResolution))
            |> ImmutableArray.CreateRange

        { Id = MatchId value.MatchId
          AuthorityVersion = value.AuthorityVersion
          Seed = MatchSeed value.Seed
          Random = MatchRandomState(value.Random.State, value.Random.ConsumptionIndex)
          Revision = MatchRevision value.Transport.Revision
          LastEventSequence = value.Transport.LastEventSequence
          Phase = Enum.Parse<MatchPhase>(value.Phase)
          OpeningPlayer = PlayerId value.OpeningPlayer
          ActivePlayer = PlayerId value.ActivePlayer
          RoundNumber = value.RoundNumber
          Players = players
          Cards = cards
          Effects = effects
          ProcessedCommands =
            value.Transport.ProcessedCommandIds
            |> Seq.map CommandId
            |> ImmutableArray.CreateRange
          RoundUsage = roundUsage
          PendingEffect = pendingEffect
          PendingKnockout = pendingKnockout
          PendingBarChits = pendingBarChits
          ReplacementPlayer = value.ReplacementPlayer |> optionText PlayerId
          PendingRoundEnd = value.PendingRoundEnd
          Winner = value.Terminal.Winner |> optionText PlayerId
          SuddenDeathCount = value.Terminal.SuddenDeathCount }

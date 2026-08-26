namespace Blokemon.Differential.Tests

open Blokemon.Game
open Blokemon.ReferenceModel

[<RequireQualifiedAccess>]
module ProductionProjection =

    let private optionText projection value =
        match value with
        | ValueSome item -> projection item
        | ValueNone -> ""

    let private requirement (value: ChoiceRequirement) =
        { Id = value.Id.Value
          Kind = string value.Kind
          Chooser = value.Chooser.Value
          Minimum = value.Minimum
          Maximum = value.Maximum
          EligibleCards = value.EligibleCards |> Seq.map _.Value |> Seq.toArray
          EligibleMechanicalTypes = value.EligibleMechanicalTypes |> Seq.map string |> Seq.toArray
          EligibleEffects = value.EligibleEffects |> Seq.map _.Value |> Seq.toArray
          DependsOnOptional = value.DependsOnOptional |> optionText _.Value
          EligibleTargets = value.EligibleTargets |> Seq.map _.Value |> Seq.toArray
          RequireDifferentMechanicalTypes = value.RequireDifferentMechanicalTypes
          EligibleCardTypes =
            value.EligibleCardTypes
            |> Seq.map (fun item ->
                { Card = item.Card.Value
                  MechanicalTypes = item.Types |> Seq.map string |> Seq.toArray })
            |> Seq.toArray }

    let private choice value =
        match value with
        | EffectChoice.Optional(id, accepted) ->
            { Kind = "Optional"
              Id = id.Value
              Values = [| accepted.ToString().ToLowerInvariant() |] }
        | EffectChoice.Amount(id, amount) ->
            { Kind = "Amount"
              Id = id.Value
              Values = [| string amount |] }
        | EffectChoice.Cards(id, cards) ->
            { Kind = "Cards"
              Id = id.Value
              Values = cards |> Seq.map _.Value |> Seq.toArray }
        | EffectChoice.MechanicalType(id, mechanicalType) ->
            { Kind = "MechanicalType"
              Id = id.Value
              Values = [| string mechanicalType |] }
        | EffectChoice.Attack(id, effect) ->
            { Kind = "Attack"
              Id = id.Value
              Values = [| effect.Value |] }
        | EffectChoice.Distribution(id, allocations) ->
            { Kind = "Distribution"
              Id = id.Value
              Values =
                allocations
                |> Seq.map (fun allocation -> $"{allocation.Card.Value}:{allocation.Counters}")
                |> Seq.toArray }
        | EffectChoice.Attachments(id, placements) ->
            { Kind = "Attachments"
              Id = id.Value
              Values =
                placements
                |> Seq.map (fun placement -> $"{placement.Vim.Value}->{placement.Bloke.Value}")
                |> Seq.toArray }

    let private ids (values: CardInstanceId seq) =
        values |> Seq.map _.Value |> String.concat ","

    let private payload action =
        match action with
        | MatchAction.ChooseMulliganBonus count -> $"cards={count}"
        | MatchAction.ChooseOpening(oche, booth) -> $"oche={oche.Value};booth={ids booth}"
        | MatchAction.ChooseBonusPlacement booth -> $"booth={ids booth}"
        | MatchAction.AttachVim(vim, target) -> $"vim={vim.Value};target={target.Value}"
        | MatchAction.PlayBloke bloke -> $"bloke={bloke.Value}"
        | MatchAction.Promote(promotion, promoted) ->
            $"promotion={promotion.Value};promoted={promoted.Value}"
        | MatchAction.PlayKit(kit, target) ->
            $"kit={kit.Value};target={target |> optionText _.Value}"
        | MatchAction.Taxi(boothBloke, vim) -> $"booth={boothBloke.Value};vim={ids vim}"
        | MatchAction.UsePartyTrick(source, effect) ->
            $"source={source.Value};effect={effect.Value}"
        | MatchAction.Attack(attacker, effect) -> $"attacker={attacker.Value};effect={effect.Value}"
        | MatchAction.ChuckFossil fossil -> $"fossil={fossil.Value}"
        | MatchAction.EndRound -> "end"
        | MatchAction.ChooseReplacement replacement -> $"replacement={replacement.Value}"
        | MatchAction.ResolveEffectChoice -> "resolve-effect-choice"
        | MatchAction.ResolveKnockoutTrigger vim -> $"vim={vim |> optionText _.Value}"
        | MatchAction.ResolveBarChitTrigger booth -> $"booth={booth.ToString().ToLowerInvariant()}"
        | MatchAction.Resign -> "resign"

    let private actionKind action =
        match action with
        | MatchAction.ChooseMulliganBonus _ -> "ChooseMulliganBonus"
        | MatchAction.ChooseOpening _ -> "ChooseOpening"
        | MatchAction.ChooseBonusPlacement _ -> "ChooseBonusPlacement"
        | MatchAction.AttachVim _ -> "AttachVim"
        | MatchAction.PlayBloke _ -> "PlayBloke"
        | MatchAction.Promote _ -> "Promote"
        | MatchAction.PlayKit _ -> "PlayKit"
        | MatchAction.Taxi _ -> "Taxi"
        | MatchAction.UsePartyTrick _ -> "UsePartyTrick"
        | MatchAction.Attack _ -> "Attack"
        | MatchAction.ChuckFossil _ -> "ChuckFossil"
        | MatchAction.EndRound -> "EndRound"
        | MatchAction.ChooseReplacement _ -> "ChooseReplacement"
        | MatchAction.ResolveEffectChoice -> "ResolveEffectChoice"
        | MatchAction.ResolveKnockoutTrigger _ -> "ResolveKnockoutTrigger"
        | MatchAction.ResolveBarChitTrigger _ -> "ResolveBarChitTrigger"
        | MatchAction.Resign -> "Resign"

    let private commandAction (command: MatchCommand) =
        { Kind = actionKind command.Action
          CommandId = command.Id.Value
          MatchId = command.MatchId.Value
          Actor = command.Actor.Value
          ExpectedRevision = command.ExpectedRevision.Value
          StableKey = ""
          Payload = payload command.Action
          Affordability = "Submitted"
          Requirements = [||]
          Choices = command.Choices |> Seq.map choice |> Seq.toArray }

    let legalAction (value: LegalAction) =
        let affordability =
            match value.Affordability with
            | ActionAffordability.Payable -> "Payable"
            | ActionAffordability.ShortOfTaxiFare fare -> $"ShortOfTaxiFare:{fare}"

        { Kind = string value.Kind
          CommandId = value.Command.Id.Value
          MatchId = value.Command.MatchId.Value
          Actor = value.Command.Actor.Value
          ExpectedRevision = value.Command.ExpectedRevision.Value
          StableKey = value.StableKey
          Payload = payload value.Command.Action
          Affordability = affordability
          Requirements = value.ChoiceRequirements |> Seq.map requirement |> Seq.toArray
          Choices = value.Command.Choices |> Seq.map choice |> Seq.toArray }

    let state (value: MatchState) =
        { MatchId = value.Id.Value
          AuthorityVersion = value.AuthorityVersion
          Seed = value.Seed.Value
          Random =
            { State = value.Random.State
              ConsumptionIndex = value.Random.ConsumptionIndex }
          Transport =
            { Revision = value.Revision.Value
              LastEventSequence = value.LastEventSequence
              ProcessedCommandIds = value.ProcessedCommands |> Seq.map _.Value |> Seq.toArray }
          Phase = string value.Phase
          OpeningPlayer = value.OpeningPlayer.Value
          ActivePlayer = value.ActivePlayer.Value
          RoundNumber = value.RoundNumber
          Players =
            value.Players
            |> Seq.map (fun player ->
                { Id = player.Id.Value
                  BarChitsRemaining = player.BarChitsRemaining
                  MulliganCount = player.MulliganCount
                  MulliganBonusAllowance = player.MulliganBonusAllowance
                  MulliganBonusChosen = player.MulliganBonusChosen
                  BonusDrawn = player.BonusDrawn |> Seq.map _.Value |> Seq.toArray
                  BonusPlacementChosen = player.BonusPlacementChosen
                  OpeningChosen = player.OpeningChosen
                  RoundsStarted = player.RoundsStarted })
            |> Seq.toArray
          Cards =
            value.Cards
            |> Seq.sortBy _.Id
            |> Seq.map (fun card ->
                { Id = card.Id.Value
                  MechanicalId = card.MechanicalId.Value
                  Owner = card.Owner.Value
                  Kind = string card.Kind
                  Zone = string card.Zone
                  IsFaceDown = card.IsFaceDown
                  StackPosition = card.StackPosition
                  AttachedTo = card.AttachedTo |> optionText _.Value
                  Attachments = card.Attachments |> Seq.map _.Value |> Seq.toArray
                  UnderlyingCards = card.UnderlyingCards |> Seq.map _.Value |> Seq.toArray
                  Damage = card.Damage
                  RoughStates =
                    card.RoughStates
                    |> Seq.map (fun rough ->
                        { State = string rough.State
                          AppliedAtOwnerRound = rough.AppliedAtOwnerRound })
                    |> Seq.toArray
                  EnteredAtOwnerRound = card.EnteredAtOwnerRound
                  LastPromotedRound = card.LastPromotedRound })
            |> Seq.toArray
          Effects =
            value.Effects
            |> Seq.map (fun effect ->
                { SourceEffect = effect.SourceEffect.Value
                  SourceCard = effect.SourceCard.Value
                  Owner = effect.Owner.Value
                  TargetCard = effect.TargetCard |> optionText _.Value
                  Kind = string effect.Kind
                  Amount = effect.Amount
                  MechanicalTypes = effect.MechanicalTypes |> Seq.map string |> Seq.toArray
                  RoughStates = effect.RoughStates |> Seq.map string |> Seq.toArray
                  RelatedCards = effect.RelatedCards |> Seq.map _.Value |> Seq.toArray
                  Conditions = effect.Conditions |> Seq.map string |> Seq.toArray
                  Duration = string effect.Duration
                  AppliesFromRound = effect.AppliesFromRound
                  ExpiresAfterRound = effect.ExpiresAfterRound })
            |> Seq.toArray
          RoundUsage =
            { Player = value.RoundUsage.Player.Value
              VimAttachments = value.RoundUsage.VimAttachments
              MatesPlayed = value.RoundUsage.MatesPlayed
              LocalsPlayed = value.RoundUsage.LocalsPlayed
              TaxisUsed = value.RoundUsage.TaxisUsed
              EffectsUsed = value.RoundUsage.EffectsUsed |> Seq.map _.Value |> Seq.toArray
              KitsPlayed = value.RoundUsage.KitsPlayed |> Seq.map _.Value |> Seq.toArray }
          PendingEffect =
            match value.PendingEffect with
            | ValueNone -> Canonical.emptyPendingEffect
            | ValueSome pending ->
                { Present = true
                  Action = [| commandAction pending.Command |]
                  Source = pending.Source.Value
                  Effect = pending.Effect.Value
                  Chooser = pending.Chooser.Value
                  Requirements = pending.Requirements |> Seq.map requirement |> Seq.toArray
                  BeerMatResults = pending.BeerMatResults |> Seq.toArray
                  AttackStarted = pending.AttackStarted }
          PendingKnockout =
            match value.PendingKnockout with
            | ValueNone -> Canonical.emptyPendingKnockout
            | ValueSome pending ->
                { Present = true
                  KnockedOutCard = pending.KnockedOutCard.Value
                  RemainingKnockouts = pending.RemainingKnockouts |> Seq.map _.Value |> Seq.toArray
                  TriggerSources = pending.TriggerSources |> Seq.map _.Value |> Seq.toArray
                  TriggerSource = pending.TriggerSource.Value
                  TriggerEffect = pending.TriggerEffect.Value
                  Chooser = pending.Chooser.Value
                  EligibleVim = pending.EligibleVim |> Seq.map _.Value |> Seq.toArray
                  AttackingCard = pending.AttackingCard.Value
                  FinishRoundAfterResolution = pending.FinishRoundAfterResolution
                  AttackDamageTargets =
                    pending.AttackDamageTargets |> Seq.map _.Value |> Seq.toArray
                  ExtraBarChits = pending.ExtraBarChits }
          PendingBarChits =
            value.PendingBarChits
            |> Seq.map (fun pending ->
                { Player = pending.Player.Value
                  Card = pending.Card.Value
                  Effect = pending.Effect.Value
                  FinishRoundAfterResolution = pending.FinishRoundAfterResolution })
            |> Seq.toArray
          ReplacementPlayer = value.ReplacementPlayer |> optionText _.Value
          PendingRoundEnd = value.PendingRoundEnd
          Terminal =
            { IsComplete = value.Phase = MatchPhase.Complete
              Winner = value.Winner |> optionText _.Value
              SuddenDeathCount = value.SuddenDeathCount } }

    let events (values: MatchEvent seq) =
        values
        |> Seq.mapi (fun index value ->
            { RelativeSequence = index + 1
              Revision = value.Revision.Value
              Kind = string value.Kind
              Actor = value.Actor |> optionText _.Value
              SourceCard = value.SourceCard |> optionText _.Value
              TargetCards = value.TargetCards |> Seq.map _.Value |> Seq.toArray
              Effect = value.Effect |> optionText _.Value
              RoughState = value.RoughState |> optionText string
              DamageKind = value.DamageKind |> optionText string
              DrawReason = value.DrawReason |> optionText string
              Amount = value.Amount
              HasBadgeSide = value.BadgeSide.IsSome
              BadgeSide = value.BadgeSide |> ValueOption.defaultValue false
              Transport =
                { HasStartRequest = value.StartRequest.IsSome
                  HasCommand = value.Command.IsSome
                  HasCommittedState = value.CommittedState.IsSome } })
        |> Seq.toArray

    let startRejections (values: DeckIssue seq) =
        values
        |> Seq.map (fun value ->
            { Code = string value.Code
              Player = value.Player |> optionText _.Value
              Card = value.Card |> optionText _.Value
              Actual = value.Actual
              Expected = value.Expected })
        |> Seq.toArray

    let commandOutcome value =
        match value with
        | CommandOutcome.Applied(next, tail) ->
            { State = state next
              Events = events tail
              Rejection = [||] }
        | CommandOutcome.Rejected(rejectedState, rejection) ->
            { State = state rejectedState
              Events = [||]
              Rejection =
                [| { Code = string rejection.Code
                     ChoiceRequirements =
                       rejection.ChoiceRequirements |> Seq.map requirement |> Seq.toArray } |] }

    let foundationLegalActions values =
        let supported =
            Set
                [ "ChooseMulliganBonus"
                  "ChooseOpening"
                  "ChooseBonusPlacement"
                  "EndRound"
                  "Resign" ]

        values
        |> Seq.map legalAction
        |> Seq.filter (fun action -> supported.Contains action.Kind)
        |> Seq.toArray

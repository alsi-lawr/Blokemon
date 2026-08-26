namespace Blokemon.ReferenceModel

type CanonicalRandomState =
    { State: uint64; ConsumptionIndex: int }

type CanonicalTransportState =
    { Revision: int64
      LastEventSequence: int64
      ProcessedCommandIds: string array }

type CanonicalTransportFieldMode =
    | Exact
    | PresenceOnly
    | Excluded

type CanonicalTransportFieldPolicy =
    { Field: string
      Mode: CanonicalTransportFieldMode
      Rationale: string }

type CanonicalPlayer =
    { Id: string
      BarChitsRemaining: int
      MulliganCount: int
      MulliganBonusAllowance: int
      MulliganBonusChosen: bool
      BonusDrawn: string array
      BonusPlacementChosen: bool
      OpeningChosen: bool
      RoundsStarted: int }

type CanonicalRoughState =
    { State: string
      AppliedAtOwnerRound: int }

type CanonicalCard =
    { Id: string
      MechanicalId: string
      Owner: string
      Kind: string
      Zone: string
      IsFaceDown: bool
      StackPosition: int
      AttachedTo: string
      Attachments: string array
      UnderlyingCards: string array
      Damage: int
      RoughStates: CanonicalRoughState array
      EnteredAtOwnerRound: int
      LastPromotedRound: int }

type CanonicalTemporaryEffect =
    { SourceEffect: string
      SourceCard: string
      Owner: string
      TargetCard: string
      Kind: string
      Amount: int
      MechanicalTypes: string array
      RoughStates: string array
      RelatedCards: string array
      Conditions: string array
      Duration: string
      AppliesFromRound: int
      ExpiresAfterRound: int }

type CanonicalRoundUsage =
    { Player: string
      VimAttachments: int
      MatesPlayed: int
      LocalsPlayed: int
      TaxisUsed: int
      EffectsUsed: string array
      KitsPlayed: string array }

type CanonicalChoiceRequirement =
    { Id: string
      Kind: string
      Chooser: string
      Minimum: int
      Maximum: int
      EligibleCards: string array
      EligibleMechanicalTypes: string array
      EligibleEffects: string array
      DependsOnOptional: string
      EligibleTargets: string array
      RequireDifferentMechanicalTypes: bool
      EligibleCardTypes: CanonicalEligibleCardType array }

and CanonicalEligibleCardType =
    { Card: string
      MechanicalTypes: string array }

type CanonicalChoice =
    { Kind: string
      Id: string
      Values: string array }

type CanonicalAction =
    { Kind: string
      CommandId: string
      MatchId: string
      Actor: string
      ExpectedRevision: int64
      StableKey: string
      Payload: string
      Affordability: string
      Requirements: CanonicalChoiceRequirement array
      Choices: CanonicalChoice array }

type CanonicalPendingEffect =
    { Present: bool
      Action: CanonicalAction array
      Source: string
      Effect: string
      Chooser: string
      Requirements: CanonicalChoiceRequirement array
      BeerMatResults: bool array
      AttackStarted: bool }

type CanonicalPendingKnockout =
    { Present: bool
      KnockedOutCard: string
      RemainingKnockouts: string array
      TriggerSources: string array
      TriggerSource: string
      TriggerEffect: string
      Chooser: string
      EligibleVim: string array
      AttackingCard: string
      FinishRoundAfterResolution: bool
      AttackDamageTargets: string array
      ExtraBarChits: int }

type CanonicalPendingBarChit =
    { Player: string
      Card: string
      Effect: string
      FinishRoundAfterResolution: bool }

type CanonicalTerminal =
    { IsComplete: bool
      Winner: string
      SuddenDeathCount: int }

type CanonicalState =
    { MatchId: string
      AuthorityVersion: string
      Seed: uint64
      Random: CanonicalRandomState
      Transport: CanonicalTransportState
      Phase: string
      OpeningPlayer: string
      ActivePlayer: string
      RoundNumber: int
      Players: CanonicalPlayer array
      Cards: CanonicalCard array
      Effects: CanonicalTemporaryEffect array
      RoundUsage: CanonicalRoundUsage
      PendingEffect: CanonicalPendingEffect
      PendingKnockout: CanonicalPendingKnockout
      PendingBarChits: CanonicalPendingBarChit array
      ReplacementPlayer: string
      PendingRoundEnd: bool
      Terminal: CanonicalTerminal }

type CanonicalEventTransport =
    { HasStartRequest: bool
      HasCommand: bool
      HasCommittedState: bool }

type CanonicalEvent =
    { RelativeSequence: int
      Revision: int64
      Kind: string
      Actor: string
      SourceCard: string
      TargetCards: string array
      Effect: string
      RoughState: string
      DamageKind: string
      DrawReason: string
      Amount: int
      HasBadgeSide: bool
      BadgeSide: bool
      Transport: CanonicalEventTransport }

type CanonicalRejection =
    { Code: string
      ChoiceRequirements: CanonicalChoiceRequirement array }

type CanonicalTransition =
    { State: CanonicalState
      Events: CanonicalEvent array
      Rejection: CanonicalRejection array }

type ReferenceDeck = { Owner: string; Cards: string array }

type ReferenceStartRequest =
    { MatchId: string
      Seed: uint64
      FirstDeck: ReferenceDeck
      SecondDeck: ReferenceDeck }

type ReferenceStartRejection =
    { Code: string
      Player: string
      Card: string
      Actual: int
      Expected: int }

type ReferenceStartOutcome =
    | Started of CanonicalTransition
    | StartRejected of ReferenceStartRejection array

type ReferenceMutation =
    | NoReferenceMutation
    | OmitResignFromLegalActions
    | SkipRequiredOpeningDraw
    | SkipDamageModifiers
    | ReverseKnockoutOrder
    | SkipBarChitAward
    | SkipReplacementAssignment
    | ForceSuddenDeathForWinner
    | AllowBaseActionWhilePending
    | StartNextRoundBeforeCheckup

[<RequireQualifiedAccess>]
module Canonical =

    let transportComparisonPolicy =
        [| { Field = "state.revision"
             Mode = Exact
             Rationale = "A committed successor must have the same revision." }
           { Field = "state.lastEventSequence"
             Mode = Exact
             Rationale = "The ordered event tail must advance the same sequence boundary." }
           { Field = "state.processedCommandIds"
             Mode = Exact
             Rationale = "Foundation commands use the same reference-derived stable identities." }
           { Field = "event.startRequest"
             Mode = PresenceOnly
             Rationale =
               "The start envelope is transport; its semantic fields are compared in state." }
           { Field = "event.command"
             Mode = PresenceOnly
             Rationale =
               "The command envelope is transport; selected action fields are compared separately." }
           { Field = "event.committedState"
             Mode = PresenceOnly
             Rationale =
               "Recursive committed-state payloads are compared through the transition state." }
           { Field = "absoluteEventSequence"
             Mode = Excluded
             Rationale =
               "Each tail is compared by relative order plus the exact state sequence boundary." } |]

    let emptyPendingEffect =
        { Present = false
          Action = [||]
          Source = ""
          Effect = ""
          Chooser = ""
          Requirements = [||]
          BeerMatResults = [||]
          AttackStarted = false }

    let emptyPendingKnockout =
        { Present = false
          KnockedOutCard = ""
          RemainingKnockouts = [||]
          TriggerSources = [||]
          TriggerSource = ""
          TriggerEffect = ""
          Chooser = ""
          EligibleVim = [||]
          AttackingCard = ""
          FinishRoundAfterResolution = false
          AttackDamageTargets = [||]
          ExtraBarChits = 0 }

    let emptyEventTransport =
        { HasStartRequest = false
          HasCommand = false
          HasCommittedState = false }

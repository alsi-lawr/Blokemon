namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign

[<RequireQualifiedAccess>]
type CpuObservationMode =
    | Fair
    | Authoritative

type CpuPlayerObservation =
    { Id: PlayerId
      BarChitsRemaining: int
      MulliganCount: int
      MulliganBonusAllowance: int
      MulliganBonusChosen: bool
      BonusDrawnCount: int
      BonusPlacementChosen: bool
      OpeningChosen: bool
      RoundsStarted: int }

type CpuHiddenZoneObservation =
    { Owner: PlayerId
      Zone: CardZone
      Count: int }

type CpuPublicMatchState =
    { Id: MatchId
      AuthorityVersion: string
      Revision: MatchRevision
      Phase: MatchPhase
      OpeningPlayer: PlayerId
      ActivePlayer: PlayerId
      RoundNumber: int
      Players: ImmutableArray<CpuPlayerObservation>
      Cards: ImmutableArray<CardState>
      HiddenZones: ImmutableArray<CpuHiddenZoneObservation>
      Effects: ImmutableArray<TemporaryEffect>
      RoundUsage: RoundUsage
      PendingEffectChooser: PlayerId voption
      PendingKnockoutChooser: PlayerId voption
      PendingBarChitPlayer: PlayerId voption
      ReplacementPlayer: PlayerId voption
      PendingRoundEnd: bool
      Winner: PlayerId voption
      SuddenDeathCount: int }

type CpuCardSelection =
    { KnownCards: ImmutableArray<CardInstanceId>
      HiddenCardCount: int }

type CpuVimAttachment =
    { Vim: CardInstanceId voption
      Bloke: CardInstanceId voption }

[<RequireQualifiedAccess>]
type CpuChoiceCandidate =
    | Amount of id: EffectChoiceId * amount: int
    | Cards of id: EffectChoiceId * cards: CpuCardSelection
    | MechanicalType of id: EffectChoiceId * mechanicalType: BlokemonMechanicalType
    | Attack of id: EffectChoiceId * effect: EffectId
    | Attachments of id: EffectChoiceId * placements: ImmutableArray<CpuVimAttachment>

type CpuChoiceRequirement =
    { Id: EffectChoiceId
      Kind: ChoiceRequirementKind
      Chooser: PlayerId
      Minimum: int
      Maximum: int
      EligibleCards: ImmutableArray<CardInstanceId>
      HiddenEligibleCardCount: int
      EligibleMechanicalTypes: ImmutableArray<BlokemonMechanicalType>
      EligibleEffects: ImmutableArray<EffectId>
      DependsOnOptional: EffectChoiceId voption
      EligibleTargets: ImmutableArray<CardInstanceId>
      HiddenEligibleTargetCount: int
      RequireDifferentMechanicalTypes: bool
      EligibleCardTypes: ImmutableArray<CardMechanicalTypes>
      PreserveCardOrder: bool }

type CpuLegalCandidate =
    { Id: CpuCandidateId
      Kind: LegalActionKind
      Action: MatchAction
      Choices: ImmutableArray<CpuChoiceCandidate>
      ChoiceRequirements: ImmutableArray<CpuChoiceRequirement>
      Affordability: ActionAffordability }

type CpuObservation =
    { Actor: PlayerId
      State: CpuPublicMatchState
      Candidates: ImmutableArray<CpuLegalCandidate>
      AuthoritativeState: MatchState voption }

module internal CpuCandidateIds =

    let forIndex (state: MatchState) index =
        CpuCandidateId $"cpu-candidate:{state.Revision.Value}:%06d{index}"

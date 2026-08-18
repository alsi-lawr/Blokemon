namespace Blokemon.Game

open Blokemon.Core.SetDesign

type RoughStateEntry =
    { State: BlokemonRoughState
      AppliedAtOwnerRound: int }

type TemporaryEffect =
    { SourceEffect: EffectId
      SourceCard: CardInstanceId
      Owner: PlayerId
      TargetCard: CardInstanceId voption
      Kind: TemporaryEffectKind
      Amount: int
      MechanicalTypes: FrozenList<BlokemonMechanicalType>
      RoughStates: FrozenList<BlokemonRoughState>
      RelatedCards: FrozenList<MechanicalCardId>
      Conditions: FrozenList<BlokemonCondition>
      Duration: EffectDuration
      AppliesFromRound: int
      ExpiresAfterRound: int }

type internal TriggerContext =
    { KnockedOutBloke: CardInstanceId voption
      AttackingBloke: CardInstanceId voption }

type PendingEffectResolution =
    { Command: MatchCommand
      Source: CardInstanceId
      Effect: EffectId
      Chooser: PlayerId
      Requirements: FrozenList<ChoiceRequirement>
      BeerMatResults: FrozenList<bool>
      AttackStarted: bool }

type PendingKnockoutResolution =
    { KnockedOutCard: CardInstanceId
      RemainingKnockouts: FrozenList<CardInstanceId>
      TriggerSources: FrozenList<CardInstanceId>
      TriggerSource: CardInstanceId
      TriggerEffect: EffectId
      Chooser: PlayerId
      EligibleVim: FrozenList<CardInstanceId>
      AttackingCard: CardInstanceId
      FinishRoundAfterResolution: bool
      AttackDamageTargets: FrozenList<CardInstanceId>
      ExtraBarChits: int }

type PendingBarChitResolution =
    { Player: PlayerId
      Card: CardInstanceId
      Effect: EffectId
      FinishRoundAfterResolution: bool }

type FrozenDeckSnapshot =
    { Owner: PlayerId
      Cards: FrozenList<MechanicalCardId> }

    static member Create(owner: PlayerId, mechanicalIds: string seq) =
        { Owner = owner
          Cards = FrozenList<MechanicalCardId>.Create(mechanicalIds |> Seq.map MechanicalCardId) }

type MatchStartRequest =
    { MatchId: MatchId
      Seed: MatchSeed
      FirstDeck: FrozenDeckSnapshot
      SecondDeck: FrozenDeckSnapshot }

type CardState =
    { Id: CardInstanceId
      MechanicalId: MechanicalCardId
      Owner: PlayerId
      Kind: CardKind
      Zone: CardZone
      IsFaceDown: bool
      StackPosition: int
      AttachedTo: CardInstanceId voption
      Attachments: FrozenList<CardInstanceId>
      UnderlyingCards: FrozenList<CardInstanceId>
      Damage: int
      RoughStates: FrozenList<RoughStateEntry>
      EnteredAtOwnerRound: int
      LastPromotedRound: int }

type PlayerState =
    { Id: PlayerId
      BarChitsRemaining: int
      MulliganCount: int
      MulliganBonusAllowance: int
      MulliganBonusChosen: bool
      OpeningChosen: bool
      RoundsStarted: int }

type RoundUsage =
    { Player: PlayerId
      VimAttachments: int
      MatesPlayed: int
      LocalsPlayed: int
      TaxisUsed: int
      EffectsUsed: FrozenList<EffectId>
      KitsPlayed: FrozenList<MechanicalCardId> }

    static member Empty(player: PlayerId) =
        { Player = player
          VimAttachments = 0
          MatesPlayed = 0
          LocalsPlayed = 0
          TaxisUsed = 0
          EffectsUsed = FrozenList.empty
          KitsPlayed = FrozenList.empty }

type MatchState =
    { Id: MatchId
      AuthorityVersion: string
      Seed: MatchSeed
      Random: MatchRandomState
      Revision: MatchRevision
      LastEventSequence: int64
      Phase: MatchPhase
      OpeningPlayer: PlayerId
      ActivePlayer: PlayerId
      RoundNumber: int
      Players: FrozenList<PlayerState>
      Cards: FrozenList<CardState>
      Effects: FrozenList<TemporaryEffect>
      ProcessedCommands: FrozenList<CommandId>
      RoundUsage: RoundUsage
      PendingEffect: PendingEffectResolution voption
      PendingKnockout: PendingKnockoutResolution voption
      PendingBarChits: FrozenList<PendingBarChitResolution>
      ReplacementPlayer: PlayerId voption
      PendingRoundEnd: bool
      Winner: PlayerId voption
      SuddenDeathCount: int }

    member this.Player(id: PlayerId) =
        this.Players |> Seq.find (fun player -> player.Id = id)

    member this.Card(id: CardInstanceId) =
        this.Cards |> Seq.find (fun card -> card.Id = id)

    member this.CardsIn(player: PlayerId, zone: CardZone) =
        this.Cards
        |> Seq.filter (fun card -> card.Owner = player && card.Zone = zone)
        |> Seq.sortBy (fun card -> card.StackPosition, card.Id)

    member this.Oche(player: PlayerId) =
        this.Cards
        |> Seq.tryFind (fun card -> card.Owner = player && card.Zone = CardZone.Oche)
        |> ValueOption.ofOption

    member this.Other(player: PlayerId) =
        (this.Players |> Seq.find (fun candidate -> candidate.Id <> player)).Id

namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign

/// What a command asks the match to do. The five properties every command carried abstractly in
/// the C# hierarchy now live on the MatchCommand envelope, so each case here is payload only.
[<RequireQualifiedAccess>]
type MatchAction =
    | ChooseMulliganBonus of cardsToDraw: int
    | ChooseOpening of oche: CardInstanceId * booth: ImmutableArray<CardInstanceId>
    | ChooseBonusPlacement of bonusBooth: ImmutableArray<CardInstanceId>
    | AttachVim of vim: CardInstanceId * vimTarget: CardInstanceId
    | PlayBloke of bloke: CardInstanceId
    | Promote of promotion: CardInstanceId * promoted: CardInstanceId
    | PlayKit of kit: CardInstanceId * kitTarget: CardInstanceId voption
    | Taxi of boothBloke: CardInstanceId * vimToChuck: ImmutableArray<CardInstanceId>
    | UsePartyTrick of trickSource: CardInstanceId * trickEffect: EffectId
    | Attack of attacker: CardInstanceId * attack: EffectId
    | ChuckFossil of fossil: CardInstanceId
    | EndRound
    | ChooseReplacement of replacement: CardInstanceId
    | ResolveEffectChoice
    | ResolveKnockoutTrigger of knockoutVim: CardInstanceId voption
    | ResolveBarChitTrigger of putOntoBooth: bool
    | Resign

/// One submitted move: who is asking, against which match and revision, what they chose, and what
/// they want done.
type MatchCommand =
    { Id: CommandId
      MatchId: MatchId
      Actor: PlayerId
      ExpectedRevision: MatchRevision
      Choices: ImmutableArray<EffectChoice>
      Action: MatchAction }

type ChoiceRequirement =
    { Id: EffectChoiceId
      Kind: ChoiceRequirementKind
      Chooser: PlayerId
      Minimum: int
      Maximum: int
      EligibleCards: ImmutableArray<CardInstanceId>
      EligibleMechanicalTypes: ImmutableArray<BlokemonMechanicalType>
      EligibleEffects: ImmutableArray<EffectId>
      DependsOnOptional: EffectChoiceId voption
      EligibleTargets: ImmutableArray<CardInstanceId>
      RequireDifferentMechanicalTypes: bool
      EligibleCardTypes: ImmutableArray<CardMechanicalTypes>
      PreserveCardOrder: bool }

/// Whether a proposed action can be paid for as the table stands. An action the player cannot
/// afford is still proposed, so the interface can show it and say what it needs rather than
/// letting the affordance cease to exist; the fare a taxi asks for is the only cost that works
/// this way today.
[<RequireQualifiedAccess>]
type ActionAffordability =
    | Payable
    | ShortOfTaxiFare of fare: int

type LegalAction =
    { Kind: LegalActionKind
      Command: MatchCommand
      ChoiceRequirements: ImmutableArray<ChoiceRequirement>
      StableKey: string
      Affordability: ActionAffordability }

[<RequireQualifiedAccess>]
module ChoiceRequirement =

    /// The C# record declared EligibleTargets, RequireDifferentMechanicalTypes and
    /// EligibleCardTypes as defaulted parameters; every construction that does not care about
    /// them goes through here instead.
    let create
        (id: EffectChoiceId)
        (kind: ChoiceRequirementKind)
        (chooser: PlayerId)
        (minimum: int)
        (maximum: int)
        (eligibleCards: ImmutableArray<CardInstanceId>)
        (eligibleMechanicalTypes: ImmutableArray<BlokemonMechanicalType>)
        (eligibleEffects: ImmutableArray<EffectId>)
        (dependsOnOptional: EffectChoiceId voption)
        =
        { Id = id
          Kind = kind
          Chooser = chooser
          Minimum = minimum
          Maximum = maximum
          EligibleCards = eligibleCards
          EligibleMechanicalTypes = eligibleMechanicalTypes
          EligibleEffects = eligibleEffects
          DependsOnOptional = dependsOnOptional
          EligibleTargets = ImmutableArray<_>.Empty
          RequireDifferentMechanicalTypes = false
          EligibleCardTypes = ImmutableArray<_>.Empty
          PreserveCardOrder = false }

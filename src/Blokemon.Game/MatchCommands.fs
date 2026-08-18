namespace Blokemon.Game

open Blokemon.Core.SetDesign

/// What a command asks the match to do. The five properties every command carried abstractly in
/// the C# hierarchy now live on the MatchCommand envelope, so each case here is payload only.
[<RequireQualifiedAccess>]
type MatchAction =
    | ChooseMulliganBonus of cardsToDraw: int
    | ChooseOpening of oche: CardInstanceId * booth: FrozenList<CardInstanceId>
    | AttachVim of vim: CardInstanceId * vimTarget: CardInstanceId
    | PlayBloke of bloke: CardInstanceId
    | Promote of promotion: CardInstanceId * promoted: CardInstanceId
    | PlayKit of kit: CardInstanceId * kitTarget: CardInstanceId voption
    | Taxi of boothBloke: CardInstanceId * vimToChuck: FrozenList<CardInstanceId>
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
      Choices: FrozenList<EffectChoice>
      Action: MatchAction }

type ChoiceRequirement =
    { Id: EffectChoiceId
      Kind: ChoiceRequirementKind
      Chooser: PlayerId
      Minimum: int
      Maximum: int
      EligibleCards: FrozenList<CardInstanceId>
      EligibleMechanicalTypes: FrozenList<BlokemonMechanicalType>
      EligibleEffects: FrozenList<EffectId>
      DependsOnOptional: EffectChoiceId voption
      EligibleTargets: FrozenList<CardInstanceId>
      RequireDifferentMechanicalTypes: bool
      EligibleCardTypes: FrozenList<CardMechanicalTypes> }

type LegalAction =
    { Kind: LegalActionKind
      Command: MatchCommand
      ChoiceRequirements: FrozenList<ChoiceRequirement>
      StableKey: string }

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
        (eligibleCards: FrozenList<CardInstanceId>)
        (eligibleMechanicalTypes: FrozenList<BlokemonMechanicalType>)
        (eligibleEffects: FrozenList<EffectId>)
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
          EligibleTargets = FrozenList.empty
          RequireDifferentMechanicalTypes = false
          EligibleCardTypes = FrozenList.empty }

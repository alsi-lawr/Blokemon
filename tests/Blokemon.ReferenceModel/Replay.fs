namespace Blokemon.ReferenceModel

open System.Text
open System.Text.Json
open System.Text.Json.Serialization

type CanonicalReplayStep =
    { Index: int
      Actor: string
      SelectionOrigin: string
      SelectionChoicePayload: string array
      SelectionChoices: CanonicalChoice array
      SelectionRandomInput: CanonicalRandomState array
      LegalActions: CanonicalAction array
      SelectedAction: CanonicalAction
      State: CanonicalState
      Events: CanonicalEvent array
      Rejection: CanonicalRejection array }

type CanonicalReplay =
    { Schema: string
      TraceId: string
      Seed: uint64
      TieBreaker: string
      StepBound: int
      ProgramRouteAcceptance: string array
      InitialState: CanonicalState
      InitialEvents: CanonicalEvent array
      Steps: CanonicalReplayStep array
      Terminal: CanonicalTerminal }

type CanonicalAggregateStarterDeck =
    { Id: string
      Seed: uint64
      Cards: string array }

type CanonicalAggregateConstructedDeck =
    { Id: string
      Seed: uint64
      Cards: string array
      RuleFamily: string
      Rationale: string
      ObjectiveLabels: string array
      Routes: string array
      RepresentativeObligationIds: string array }

type CanonicalAggregateObligationReplay =
    { Index: int
      ObligationId: string
      ProgramKey: string
      Route: string
      Runner: string
      Seed: uint64
      StepBound: int
      InitialState: CanonicalState
      LegalActions: CanonicalAction array array
      SelectedActions: CanonicalAction array
      ChoiceProbes: CanonicalTransition array
      Transitions: CanonicalTransition array }

type CanonicalAggregateReplay =
    { Schema: string
      ReferenceTieBreaker: string
      TransitionCeiling: int
      StarterDecks: CanonicalAggregateStarterDeck array
      ConstructedDecks: CanonicalAggregateConstructedDeck array
      CorpusTraces: CanonicalReplay array
      ObligationCount: int
      RouteCount: int
      Obligations: CanonicalAggregateObligationReplay array }

[<RequireQualifiedAccess>]
module CanonicalReplay =

    [<Literal>]
    let Schema = "blokemon-reference-foundation-replay-3"

    [<Literal>]
    let TieBreaker =
        "reference-phase-action; maximum-mulligan-bonus; first-program-free-opening; two-end-rounds; resign"

    [<Literal>]
    let FoundationStepBound = 24

    let private options =
        let value = JsonSerializerOptions(JsonSerializerDefaults.Web)
        value.DefaultIgnoreCondition <- JsonIgnoreCondition.Never
        value.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        value.WriteIndented <- false
        value

    let bytes replay =
        let json = JsonSerializer.Serialize(replay, options)
        Encoding.UTF8.GetBytes(json + "\n")

[<RequireQualifiedAccess>]
module CanonicalAggregateReplay =

    [<Literal>]
    let Schema = "blokemon-differential-aggregate-replay-2"

    [<Literal>]
    let ReferenceTieBreaker =
        "sorted-obligation-id; reviewed-action-order; canonical-stable-key; reviewed-choice-order; seeded-reference-rng"

    [<Literal>]
    let TransitionCeiling = 12

    let private options =
        let value = JsonSerializerOptions(JsonSerializerDefaults.Web)
        value.DefaultIgnoreCondition <- JsonIgnoreCondition.Never
        value.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        value.WriteIndented <- false
        value

    let bytes replay =
        let json = JsonSerializer.Serialize(replay, options)
        Encoding.UTF8.GetBytes(json + "\n")

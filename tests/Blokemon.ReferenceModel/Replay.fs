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

namespace Blokemon.Game.Tests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization

type ConformanceCardSetupAssertion =
    { CardId: string
      Owner: string
      MechanicalId: string
      Zone: string }

type ConformanceZoneCountAssertion =
    { Owner: string
      Zone: string
      Count: int }

type ConformancePlayerSetupAssertion =
    { Player: string
      BarChitsRemaining: int }

type ConformanceInitialStateInput =
    { Route: string
      Parameters: string array
      Cards: ConformanceCardSetupAssertion array
      ZoneCounts: ConformanceZoneCountAssertion array
      Players: ConformancePlayerSetupAssertion array }

type ConformanceChoiceInput =
    { Kind: string
      RequirementId: string
      Values: string array
      WhenAvailable: bool }

type ConformanceActionInput =
    { Kind: string
      Actor: string
      SourceCard: string
      TargetCard: string
      EffectId: string
      Choices: ConformanceChoiceInput array }

type ConformanceRandomInput = { Seed: uint64 }

type ConformanceReviewedProgram =
    { OwnerId: string
      Kind: string
      MechanicalId: string }

type ConformanceSemanticObligation =
    { Id: string
      ProgramKey: string
      Covers: string array
      ReviewedProgram: ConformanceReviewedProgram
      InitialState: ConformanceInitialStateInput
      Actions: ConformanceActionInput array
      RandomInput: ConformanceRandomInput
      ExpectedChoices: string array
      LegalActionResult: string
      CanonicalState: string array
      OrderedEvents: string array }

type ConformanceStructuralRationale =
    { Key: string
      ProgramKey: string
      Rationale: string }

type ConformanceNonMutableOperandRationale =
    { Pointer: string
      ProgramKey: string
      NamedItem: string
      Rationale: string }

type ConformanceSemanticMutation =
    { Id: string
      Pointer: string
      NamedObligation: string
      ScenarioObligation: string
      Operation: string
      ExpectedFailurePaths: string array }

type ConformanceObligationFactsData =
    { SchemaVersion: int
      Obligations: ConformanceSemanticObligation array
      StructuralRationales: ConformanceStructuralRationale array
      Mutations: ConformanceSemanticMutation array
      NonMutableOperands: ConformanceNonMutableOperandRationale array }

module internal ConformanceObligationFacts =

    let private jsonOptions =
        let options = JsonSerializerOptions(JsonSerializerDefaults.Web)
        options.PropertyNameCaseInsensitive <- false
        options.RespectRequiredConstructorParameters <- true
        options.UnmappedMemberHandling <- JsonUnmappedMemberHandling.Disallow
        options

    let path () =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "conformance-obligations.json")

    let loadFrom path =
        let facts =
            JsonSerializer.Deserialize<ConformanceObligationFactsData>(
                File.ReadAllText path,
                jsonOptions
            )

        match facts with
        | null -> raise (JsonException("The conformance obligation facts were empty."))
        | facts when facts.SchemaVersion <> 2 ->
            raise (
                JsonException($"Unsupported conformance obligation schema {facts.SchemaVersion}.")
            )
        | facts -> facts

    let load () = path () |> loadFrom

    let private blank value = String.IsNullOrWhiteSpace value

    let malformed (facts: ConformanceObligationFactsData) =
        seq {
            for obligation in facts.Obligations do
                let scalars =
                    [ obligation.Id
                      obligation.ProgramKey
                      obligation.ReviewedProgram.OwnerId
                      obligation.ReviewedProgram.Kind
                      obligation.ReviewedProgram.MechanicalId
                      obligation.InitialState.Route
                      obligation.LegalActionResult ]

                if scalars |> List.exists blank then
                    yield $"{obligation.Id}:scalar"

                let arrays = [ obligation.Covers; obligation.CanonicalState ]

                if arrays |> List.exists (fun values -> values.Length = 0) then
                    yield $"{obligation.Id}:empty-array"

                if arrays |> List.exists (Array.exists blank) then
                    yield $"{obligation.Id}:blank-array-item"

                if obligation.OrderedEvents |> Array.exists blank then
                    yield $"{obligation.Id}:blank-ordered-event"

                if obligation.InitialState.Parameters |> Array.exists blank then
                    yield $"{obligation.Id}:blank-setup-parameter"

                for card in obligation.InitialState.Cards do
                    if
                        [ card.CardId; card.Owner; card.MechanicalId; card.Zone ]
                        |> List.exists blank
                    then
                        yield $"{obligation.Id}:initial-card"

                for zoneCount in obligation.InitialState.ZoneCounts do
                    if blank zoneCount.Owner || blank zoneCount.Zone || zoneCount.Count < 0 then
                        yield $"{obligation.Id}:initial-zone-count"

                for player in obligation.InitialState.Players do
                    if blank player.Player || player.BarChitsRemaining < 0 then
                        yield $"{obligation.Id}:initial-player"

                if obligation.Actions.Length = 0 then
                    yield $"{obligation.Id}:missing-action"

                for action in obligation.Actions do
                    if [ action.Kind; action.Actor ] |> List.exists blank then
                        yield $"{obligation.Id}:action"

                    let validShape =
                        match action.Kind with
                        | "Attack" ->
                            not (blank action.SourceCard)
                            && not (blank action.TargetCard)
                            && not (blank action.EffectId)
                        | "EndRound" ->
                            blank action.SourceCard
                            && blank action.TargetCard
                            && blank action.EffectId
                            && action.Choices.Length = 0
                        | "UsePartyTrick" ->
                            not (blank action.SourceCard)
                            && blank action.TargetCard
                            && not (blank action.EffectId)
                        | "Promote" ->
                            not (blank action.SourceCard)
                            && not (blank action.TargetCard)
                            && blank action.EffectId
                        | "PlayKit" -> not (blank action.SourceCard) && blank action.EffectId
                        | "ResolveKnockoutTrigger" ->
                            not (blank action.SourceCard)
                            && not (blank action.EffectId)
                            && action.Choices.Length = 0
                        | "ResolveBarChitTrigger" ->
                            not (blank action.SourceCard)
                            && not (blank action.TargetCard)
                            && not (blank action.EffectId)
                            && action.Choices.Length = 0
                        | _ -> false

                    if not validShape then
                        yield $"{obligation.Id}:action-shape"

                    for choice in action.Choices do
                        if blank choice.Kind || blank choice.RequirementId then
                            yield $"{obligation.Id}:choice-input"

                        if choice.Values |> Array.exists blank then
                            yield $"{obligation.Id}:blank-choice-value"

            for rationale in facts.StructuralRationales do
                if
                    blank rationale.Key || blank rationale.ProgramKey || blank rationale.Rationale
                then
                    yield $"{rationale.Key}:rationale"

            for mutation in facts.Mutations do
                if
                    [ mutation.Id
                      mutation.Pointer
                      mutation.NamedObligation
                      mutation.ScenarioObligation
                      mutation.Operation ]
                    |> List.exists blank
                then
                    yield $"{mutation.Id}:mutation"

                if mutation.ExpectedFailurePaths.Length = 0 then
                    yield $"{mutation.Id}:mutation-failure-path"

                if mutation.ExpectedFailurePaths |> Array.exists blank then
                    yield $"{mutation.Id}:blank-mutation-failure-path"

                if
                    mutation.ExpectedFailurePaths
                    |> Array.distinct
                    |> Array.length
                    |> (<>) mutation.ExpectedFailurePaths.Length
                then
                    yield $"{mutation.Id}:duplicate-mutation-failure-path"

            for rationale in facts.NonMutableOperands do
                if
                    [ rationale.Pointer
                      rationale.ProgramKey
                      rationale.NamedItem
                      rationale.Rationale ]
                    |> List.exists blank
                then
                    yield $"{rationale.Pointer}:non-mutable-operand"
        }
        |> Seq.toArray

    let duplicates values =
        values
        |> Seq.countBy id
        |> Seq.choose (fun (value, count) -> if count = 1 then None else Some value)
        |> Seq.sort
        |> Seq.toArray

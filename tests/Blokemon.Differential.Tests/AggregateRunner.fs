namespace Blokemon.Differential.Tests

open System
open Blokemon.Core.SetDesign
open Blokemon.ReferenceModel

type DifferentialAggregateContext =
    { Root: string
      Reference: ReferenceAggregate
      Corpus: DifferentialAggregateCorpus
      ProductionAuthority: BlokemonRuntimeManifest }

type DifferentialAggregateRun =
    | AggregateEquivalent of CanonicalAggregateReplay * byte array
    | AggregateDiverged of DifferentialDivergence

[<RequireQualifiedAccess>]
module AggregateRunner =

    let load root =
        let reference =
            ReferenceAggregate.load (Checkout.rawAuthorityPath root) (Checkout.obligationPath root)

        UpstreamIdentity.validate
            (Checkout.obligationPath root)
            (Checkout.upstreamIdentityPath root)

        let corpus = DifferentialAggregateCorpus.load root reference

        let productionAuthority =
            DifferentialRunner.productionAuthority
                (Checkout.rawAuthorityPath root)
                NoProductionMutation

        { Root = root
          Reference = reference
          Corpus = corpus
          ProductionAuthority = productionAuthority }

    let private replay
        (input: ReferenceObligationInput)
        initialState
        runner
        stepBound
        legalActions
        selectedActions
        choiceProbes
        transitions
        =
        { Index = -1
          ObligationId = input.Id
          ProgramKey = input.ProgramKey
          Route = input.InitialState.Route.Value
          Runner = runner
          Seed = input.RandomSeed
          StepBound = stepBound
          InitialState = initialState
          LegalActions = legalActions
          SelectedActions = selectedActions
          ChoiceProbes = choiceProbes
          Transitions = transitions }
        : CanonicalAggregateObligationReplay

    let private runObligation context obligation =
        let input = ReferenceAggregate.input obligation
        let initialState = ReferenceAggregate.initialState obligation

        match obligation with
        | Deterministic value ->
            match
                DeterministicProgramRunner.runWithProductionAuthority
                    context.ProductionAuthority
                    context.Reference.Authority
                    value
                    value.Input
                    NoProgramMutation
            with
            | DeterministicEquivalent evidence ->
                Ok(
                    replay
                        input
                        initialState
                        (ReferenceAggregate.runner obligation)
                        evidence.StepBound
                        [| evidence.LegalActions |]
                        [| evidence.SelectedAction |]
                        evidence.ChoiceProbe
                        [| evidence.Transition |]
                )
            | DeterministicDiverged divergence -> Error divergence
        | Branching value ->
            match
                BranchingProgramRunner.runWithProductionAuthority
                    context.ProductionAuthority
                    context.Reference.Authority
                    value
                    value.Input
                    NoProgramMutation
            with
            | BranchingEquivalent evidence ->
                Ok(
                    replay
                        input
                        initialState
                        (ReferenceAggregate.runner obligation)
                        evidence.StepBound
                        evidence.LegalActions
                        evidence.SelectedActions
                        evidence.ChoiceProbes
                        evidence.Transitions
                )
            | BranchingDiverged divergence -> Error divergence
        | Lifecycle value ->
            match
                LifecycleProgramRunner.runWithProductionAuthority
                    context.ProductionAuthority
                    context.Reference.Authority
                    value
                    value.Input
                    NoLifecycleMutation
            with
            | LifecycleEquivalent evidence ->
                Ok(
                    replay
                        input
                        initialState
                        (ReferenceAggregate.runner obligation)
                        evidence.StepBound
                        evidence.LegalActions
                        evidence.SelectedActions
                        evidence.ChoiceProbes
                        evidence.Transitions
                )
            | LifecycleDiverged divergence -> Error divergence
        | SpecializedTrigger value ->
            match
                SpecializedTriggerProgramRunner.runWithProductionAuthority
                    context.ProductionAuthority
                    context.Reference.Authority
                    value
                    value.Input
                    NoSpecializedTriggerMutation
            with
            | SpecializedTriggerEquivalent evidence ->
                Ok(
                    replay
                        input
                        initialState
                        (ReferenceAggregate.runner obligation)
                        evidence.StepBound
                        evidence.LegalActions
                        evidence.SelectedActions
                        evidence.ChoiceProbes
                        evidence.Transitions
                )
            | SpecializedTriggerDiverged divergence -> Error divergence

    let private runCorpus context =
        let traces = DifferentialAggregateCorpus.traces context.Root context.Corpus
        let evidence = ResizeArray<CanonicalReplay>()
        let mutable divergence: DifferentialDivergence voption = ValueNone

        for trace in traces do
            if divergence.IsNone then
                match
                    DifferentialRunner.runTrace
                        context.Root
                        trace
                        ReferenceDriven
                        NoProductionMutation
                        NoReferenceMutation
                with
                | Equivalent(replay, _) ->
                    if replay.Steps.Length > context.Corpus.TransitionCeiling then
                        invalidOp
                            $"Aggregate corpus trace {trace.Id} exceeded the {context.Corpus.TransitionCeiling}-transition ceiling."

                    evidence.Add replay
                | Diverged value -> divergence <- ValueSome value
                | result -> invalidOp $"Aggregate corpus trace {trace.Id} returned {result}."

        match divergence with
        | ValueSome value -> Error value
        | ValueNone ->
            let replays = evidence.ToArray()
            let expectedIds = traces |> Array.map _.Id

            if
                replays.Length <> 7
                || replays |> Array.map _.TraceId <> expectedIds
                || (expectedIds |> Set.ofArray).Count <> 7
            then
                invalidOp "The aggregate corpus did not execute each of its seven decks once."

            Ok replays

    let private runObligations context corpusTraces =
        let evidence = ResizeArray<CanonicalAggregateObligationReplay>()
        let mutable divergence: DifferentialDivergence voption = ValueNone

        for obligation in context.Reference.Obligations do
            if divergence.IsNone then
                match runObligation context obligation with
                | Error value -> divergence <- ValueSome value
                | Ok value ->
                    if value.StepBound > context.Corpus.TransitionCeiling then
                        invalidOp
                            $"Aggregate obligation {value.ObligationId} exceeded the {context.Corpus.TransitionCeiling}-transition ceiling."

                    evidence.Add { value with Index = evidence.Count }

        match divergence with
        | ValueSome value -> AggregateDiverged value
        | ValueNone ->
            let obligations = evidence.ToArray()
            let ids = obligations |> Array.map _.ObligationId

            if
                obligations.Length <> ReferenceObligations.ObligationCount
                || ids <> Array.sort ids
                || (ids |> Set.ofArray).Count <> ReferenceObligations.ObligationCount
                || (obligations |> Seq.map _.Route |> Set.ofSeq).Count
                   <> ReferenceObligations.RouteIdentityCount
            then
                invalidOp "The aggregate replay did not execute the exact sorted 73/429 census."

            let constructedDecks =
                DifferentialAggregateCorpus.canonicalConstructed context.Corpus

            let obligationEvidenceById =
                obligations |> Seq.map (fun value -> value.ObligationId, value) |> Map.ofSeq

            for deck in constructedDecks do
                let representatives =
                    deck.RepresentativeObligationIds
                    |> Array.map (fun id -> obligationEvidenceById[id])

                if
                    representatives |> Array.map _.Route |> Set.ofArray
                    <> (deck.Routes |> Set.ofArray)
                    || representatives
                       |> Array.exists (fun value -> value.Runner <> deck.RuleFamily)
                then
                    invalidOp
                        $"Constructed corpus deck {deck.Id} does not reference its executed objective evidence."

            let replay =
                { Schema = CanonicalAggregateReplay.Schema
                  ReferenceTieBreaker = context.Corpus.ReferenceTieBreaker
                  TransitionCeiling = context.Corpus.TransitionCeiling
                  StarterDecks =
                    DifferentialAggregateCorpus.canonicalStarters context.Root context.Corpus
                  ConstructedDecks = constructedDecks
                  CorpusTraces = corpusTraces
                  ObligationCount = obligations.Length
                  RouteCount = obligations |> Seq.map _.Route |> Set.ofSeq |> Set.count
                  Obligations = obligations }

            AggregateEquivalent(replay, CanonicalAggregateReplay.bytes replay)

    let run context =
        match runCorpus context with
        | Error divergence -> AggregateDiverged divergence
        | Ok corpusTraces -> runObligations context corpusTraces

    let private starterTrace context id =
        let starter = context.Corpus.StarterDecks |> Array.find (fun value -> value.Id = id)
        FoundationTraces.starter context.Root starter.Id starter.Seed

    let productionDrivenNegative context =
        DifferentialRunner.runTrace
            context.Root
            (starterTrace context "growroom")
            ProductionDriven
            NoProductionMutation
            NoReferenceMutation

    let productionRequiredOpeningDrawMutant context =
        DifferentialRunner.runTrace
            context.Root
            (starterTrace context "growroom")
            ReferenceDriven
            ProductionSkipsRequiredOpeningDraw
            NoReferenceMutation

    let referenceOmitResignLegalSetMutant context =
        DifferentialRunner.runTrace
            context.Root
            (starterTrace context "growroom")
            ReferenceDriven
            NoProductionMutation
            OmitResignFromLegalActions

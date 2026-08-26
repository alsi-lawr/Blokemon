namespace Blokemon.Differential.Tests

open System
open System.Collections.Immutable
open System.IO
open System.Text.Json.Nodes
open Blokemon.Core.SetDesign
open Blokemon.Game
open Blokemon.ReferenceModel

type ProductionMutation =
    | NoProductionMutation
    | ProductionSkipsRequiredOpeningDraw

type SelectionOrigin =
    | ReferenceDriven
    | ProductionDriven

type DifferentialSelectionEvidence =
    { Origin: SelectionOrigin
      ObservedProductionActor: string array
      ObservedProductionAction: CanonicalAction array
      ObservedProductionRandom: CanonicalRandomState array
      AppliedActor: string
      AppliedAction: CanonicalAction
      AppliedChoicePayload: string array
      AppliedChoices: CanonicalChoice array
      AppliedRandom: CanonicalRandomState array
      LegalSelectionBoundaryReached: bool
      ReferenceTransitionAttempted: bool
      ProductionTransitionAttempted: bool
      ReferenceRevisionAfter: int64
      ProductionRevisionAfter: int64
      ReferenceRandomAfter: CanonicalRandomState
      ProductionRandomAfter: CanonicalRandomState }

type DifferentialDivergence =
    { TraceId: string
      Stage: string
      ReferenceFact: string
      ProductionFact: string
      SelectionEvidence: DifferentialSelectionEvidence array }

type DifferentialRun =
    | Equivalent of CanonicalReplay * byte array
    | StartEquivalent of ReferenceStartRejection array
    | RejectionsEquivalent of (string * string) array
    | Diverged of DifferentialDivergence

type DifferentialBootstrap =
    { Authority: ReferenceAuthority
      ObligationSetups: ProductionObligationInput array
      RouteIdentities: Set<string>
      ProgramRouteAcceptance: Set<string> }

[<RequireQualifiedAccess>]
module DifferentialRunner =

    type private DifferentialSelection =
        { Origin: SelectionOrigin
          Actor: string
          Action: CanonicalAction
          ChoicePayload: string array
          Choices: CanonicalChoice array
          RandomInput: CanonicalRandomState array
          ObservedProductionActor: string array
          ObservedProductionAction: CanonicalAction array
          ObservedProductionRandom: CanonicalRandomState array }

    type private SelectionComparison =
        { Actor: string
          Action: CanonicalAction
          ChoicePayload: string array
          Choices: CanonicalChoice array
          RandomInput: CanonicalRandomState array }

    type private ValidatedTransition =
        { Reference: CanonicalTransition
          ProductionOutcome: CommandOutcome }

    let private fact value = sprintf "%A" value

    let private mismatch traceId stage referenceFact productionFact =
        Diverged
            { TraceId = traceId
              Stage = stage
              ReferenceFact = fact referenceFact
              ProductionFact = fact productionFact
              SelectionEvidence = [||] }

    let private productionAuthority path mutation =
        let raw = File.ReadAllText path

        let requiredNode name (node: JsonNode | null) =
            match node with
            | null -> invalidOp $"The raw mechanical authority has no {name}."
            | value -> value

        let json =
            match mutation with
            | NoProductionMutation -> raw
            | ProductionSkipsRequiredOpeningDraw ->
                let root = JsonNode.Parse raw |> requiredNode "root"
                let baseRules = root["baseRules"] |> requiredNode "baseRules"
                let round = baseRules["round"] |> requiredNode "round"
                round["requiredOpeningDraw"] <- false
                root.ToJsonString()

        BlokemonSetJson.RuntimeManifest json

    let bootstrap root =
        let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

        let inventory = ReferenceObligations.load authority (Checkout.obligationPath root)

        let setups = inventory.Obligations |> Array.map ProductionSetup.materialize

        let routeIdentities = setups |> Seq.map _.Route |> Set.ofSeq

        let reviewedRouteIdentities = inventory.RouteIdentities |> Set.map _.Value

        if setups.Length <> ReferenceObligations.ObligationCount then
            invalidOp
                $"The differential bootstrap materialized {setups.Length} of {ReferenceObligations.ObligationCount} obligation setups."

        if
            routeIdentities.Count <> ReferenceObligations.RouteIdentityCount
            || routeIdentities <> reviewedRouteIdentities
        then
            invalidOp
                $"The differential bootstrap materialized an incomplete reviewed setup-route census (expected {ReferenceObligations.RouteIdentityCount}, found {routeIdentities.Count})."

        let programRouteAcceptance = inventory.AcceptedProgramRoutes |> Set.map _.Value

        if not programRouteAcceptance.IsEmpty then
            invalidOp "The differential bootstrap cannot credit unexecuted program routes."

        { Authority = authority
          ObligationSetups = setups
          RouteIdentities = routeIdentities
          ProgramRouteAcceptance = programRouteAcceptance }

    let private referenceRequest (spec: FoundationTraceSpec) =
        { MatchId = $"differential:{spec.Id}"
          Seed = spec.Seed
          FirstDeck =
            { Owner = "first"
              Cards = spec.FirstCards }
          SecondDeck =
            { Owner = "second"
              Cards = spec.SecondCards } }

    let private productionStartRequest (request: ReferenceStartRequest) : MatchStartRequest =
        { MatchId = MatchId request.MatchId
          Seed = MatchSeed request.Seed
          FirstDeck =
            FrozenDeckSnapshot.Create(request.FirstDeck.Owner |> PlayerId, request.FirstDeck.Cards)
          SecondDeck =
            FrozenDeckSnapshot.Create(
                request.SecondDeck.Owner |> PlayerId,
                request.SecondDeck.Cards
            ) }

    let private productionCommand (selected: CanonicalAction) =
        if selected.Choices.Length <> 0 then
            invalidOp "Foundation commands cannot apply program-effect choices."

        let ids (text: string) =
            text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            |> Seq.map CardInstanceId
            |> ImmutableArray.CreateRange

        let action =
            match selected.Kind with
            | "ChooseMulliganBonus" ->
                MatchAction.ChooseMulliganBonus(Int32.Parse(selected.Payload.Substring(6)))
            | "ChooseOpening" ->
                let parts = selected.Payload.Split(';')

                MatchAction.ChooseOpening(
                    CardInstanceId(parts[0].Substring(5)),
                    ids (parts[1].Substring(6))
                )
            | "ChooseBonusPlacement" ->
                MatchAction.ChooseBonusPlacement(ids (selected.Payload.Substring(6)))
            | "EndRound" -> MatchAction.EndRound
            | "Resign" -> MatchAction.Resign
            | kind -> invalidOp $"Unsupported foundation production action {kind}."

        { Id = CommandId selected.CommandId
          MatchId = MatchId selected.MatchId
          Actor = PlayerId selected.Actor
          ExpectedRevision = MatchRevision selected.ExpectedRevision
          Choices = ImmutableArray<_>.Empty
          Action = action }

    let private compareStart traceId reference production =
        match reference, production with
        | StartRejected referenceIssues, MatchStartOutcome.Rejected productionIssues ->
            let projected = ProductionProjection.startRejections productionIssues

            if referenceIssues = projected then
                Choice1Of2(StartEquivalent referenceIssues)
            else
                Choice1Of2(mismatch traceId "start-rejection" referenceIssues projected)
        | StartRejected referenceIssues, productionOutcome ->
            Choice1Of2(mismatch traceId "start-outcome" referenceIssues productionOutcome)
        | Started referenceStart, MatchStartOutcome.Rejected productionIssues ->
            Choice1Of2(mismatch traceId "start-outcome" referenceStart productionIssues)
        | Started referenceStart,
          MatchStartOutcome.Started(productionStartState, productionStartEvents) ->
            let projectedState = ProductionProjection.state productionStartState
            let projectedEvents = ProductionProjection.events productionStartEvents

            if referenceStart.State <> projectedState then
                Choice1Of2(mismatch traceId "start-state" referenceStart.State projectedState)
            elif referenceStart.Events <> projectedEvents then
                Choice1Of2(mismatch traceId "start-events" referenceStart.Events projectedEvents)
            else
                Choice2Of2(referenceStart, productionStartState)

    let runStartRequest root request =
        let authorityPath = Checkout.rawAuthorityPath root
        let referenceAuthority = ReferenceAuthority.load authorityPath
        let engine = MatchEngine(productionAuthority authorityPath NoProductionMutation)

        compareStart
            request.MatchId
            (ReferenceEngine.start referenceAuthority NoReferenceMutation request)
            (engine.Start(productionStartRequest request))
        |> function
            | Choice1Of2 result -> result
            | Choice2Of2 _ -> mismatch request.MatchId "start-outcome" "Rejected" "Started"

    let private choicePayload (action: CanonicalAction) =
        match action.Kind with
        | "ChooseMulliganBonus" -> [| action.Payload.Substring(6) |]
        | "ChooseOpening" -> [| action.Payload.Split(';').[1].Substring(6) |]
        | "ChooseBonusPlacement" -> [| action.Payload.Substring(6) |]
        | _ -> [||]

    let private randomInput (state: CanonicalState) (action: CanonicalAction) =
        match action.Kind with
        | "ChooseMulliganBonus"
        | "ChooseOpening"
        | "ChooseBonusPlacement" -> [| state.Random |]
        | _ -> [||]

    let private referenceSelection
        (state: CanonicalState)
        (actor: string)
        (action: CanonicalAction)
        : DifferentialSelection =
        { Origin = ReferenceDriven
          Actor = actor
          Action = action
          ChoicePayload = choicePayload action
          Choices = action.Choices
          RandomInput = randomInput state action
          ObservedProductionActor = [||]
          ObservedProductionAction = [||]
          ObservedProductionRandom = [||] }

    let private completeProductionObservation
        (completedEndRounds: int)
        (actions: CanonicalAction array)
        : CanonicalAction * CanonicalAction =
        let required kind =
            actions
            |> Array.tryFind (fun candidate -> candidate.Kind = kind)
            |> Option.defaultWith (fun () ->
                invalidOp $"The production-driven negative observed no {kind} action.")

        if
            actions
            |> Array.exists (fun candidate -> candidate.Kind = "ChooseMulliganBonus")
        then
            let observed =
                actions
                |> Array.filter (fun candidate -> candidate.Kind = "ChooseMulliganBonus")
                |> Array.maxBy (fun candidate -> Int32.Parse(candidate.Payload.Substring(6)))

            observed, observed
        elif actions |> Array.exists (fun candidate -> candidate.Kind = "ChooseOpening") then
            let observed = required "ChooseOpening"

            let booth =
                observed.Requirements
                |> Array.collect _.EligibleCards
                |> Array.sort
                |> Array.tryLast
                |> Option.toArray
                |> String.concat ","

            let oche = observed.Payload.Split(';').[0]

            observed,
            { observed with
                Payload = $"{oche};booth={booth}" }
        elif
            actions
            |> Array.exists (fun candidate -> candidate.Kind = "ChooseBonusPlacement")
        then
            let observed = required "ChooseBonusPlacement"

            let booth =
                observed.Requirements
                |> Array.collect _.EligibleCards
                |> Array.sort
                |> Array.tryLast
                |> Option.toArray
                |> String.concat ","

            observed,
            { observed with
                Payload = $"booth={booth}" }
        elif completedEndRounds < 2 then
            let observed = required "EndRound"
            observed, observed
        else
            let observed = required "Resign"
            observed, observed

    let private productionActorObservation
        (engine: MatchEngine)
        (productionState: MatchState)
        : PlayerId * CanonicalAction array =
        let observations =
            productionState.Players
            |> Seq.map _.Id
            |> Seq.sortBy _.Value
            |> Seq.map (fun actor ->
                actor,
                engine.GetLegalActions(productionState, actor)
                |> ProductionProjection.foundationLegalActions)
            |> Seq.toArray

        observations
        |> Array.tryFind (fun (_, actions) ->
            actions |> Array.exists (fun candidate -> candidate.Kind <> "Resign"))
        |> Option.defaultWith (fun () ->
            let actor = productionState.ActivePlayer

            actor,
            engine.GetLegalActions(productionState, actor)
            |> ProductionProjection.foundationLegalActions)

    let private productionSelection
        (completedEndRounds: int)
        (productionState: MatchState)
        (actor: PlayerId)
        (actions: CanonicalAction array)
        : DifferentialSelection =
        let observed, complete = completeProductionObservation completedEndRounds actions
        let projectedState = ProductionProjection.state productionState

        { Origin = ProductionDriven
          Actor = actor.Value
          Action = complete
          ChoicePayload = choicePayload complete
          Choices = complete.Choices
          RandomInput = randomInput projectedState complete
          ObservedProductionActor = [| actor.Value |]
          ObservedProductionAction = [| observed |]
          ObservedProductionRandom = randomInput projectedState complete }

    let private comparison (selection: DifferentialSelection) : SelectionComparison =
        { Actor = selection.Actor
          Action = selection.Action
          ChoicePayload = selection.ChoicePayload
          Choices = selection.Choices
          RandomInput = selection.RandomInput }

    let private applyReferenceRandom (random: CanonicalRandomState array) (state: CanonicalState) =
        match random with
        | [||] -> state
        | [| value |] -> { state with Random = value }
        | _ -> invalidOp "A foundation selection supplied more than one reference RNG input."

    let private applyProductionRandom (random: CanonicalRandomState array) (state: MatchState) =
        match random with
        | [||] -> state
        | [| value |] ->
            { state with
                Random = MatchRandomState(value.State, value.ConsumptionIndex) }
        | _ -> invalidOp "A foundation selection supplied more than one production RNG input."

    let private fromLegalObservation (legal: CanonicalAction array) (action: CanonicalAction) =
        legal
        |> Array.exists (fun candidate ->
            candidate = { action with
                            Payload = candidate.Payload })

    let private validateTransition
        (traceId: string)
        (step: int)
        (engine: MatchEngine)
        (referenceAuthority: ReferenceAuthority)
        (referenceMutation: ReferenceMutation)
        (referenceState: CanonicalState)
        (productionState: MatchState)
        (legal: CanonicalAction array)
        (expected: DifferentialSelection)
        (selected: DifferentialSelection)
        =
        let reachedLegalSelectionBoundary = fromLegalObservation legal selected.Action
        let referenceInputState = applyReferenceRandom selected.RandomInput referenceState

        let productionInputState =
            applyProductionRandom selected.RandomInput productionState

        let referenceTransition =
            ReferenceEngine.apply
                referenceAuthority
                referenceMutation
                referenceInputState
                selected.Action

        let productionOutcome =
            engine.Apply(productionInputState, productionCommand selected.Action)

        let productionTransition = productionOutcome |> ProductionProjection.commandOutcome

        let evidence: DifferentialSelectionEvidence =
            { Origin = selected.Origin
              ObservedProductionActor = selected.ObservedProductionActor
              ObservedProductionAction = selected.ObservedProductionAction
              ObservedProductionRandom = selected.ObservedProductionRandom
              AppliedActor = selected.Actor
              AppliedAction = selected.Action
              AppliedChoicePayload = selected.ChoicePayload
              AppliedChoices = selected.Choices
              AppliedRandom = selected.RandomInput
              LegalSelectionBoundaryReached = reachedLegalSelectionBoundary
              ReferenceTransitionAttempted = true
              ProductionTransitionAttempted = true
              ReferenceRevisionAfter = referenceTransition.State.Transport.Revision
              ProductionRevisionAfter = productionTransition.State.Transport.Revision
              ReferenceRandomAfter = referenceTransition.State.Random
              ProductionRandomAfter = productionTransition.State.Random }

        let diverged stage referenceFact productionFact =
            Error
                { TraceId = traceId
                  Stage = $"{stage}:{step}"
                  ReferenceFact = fact referenceFact
                  ProductionFact = fact productionFact
                  SelectionEvidence = [| evidence |] }

        if not reachedLegalSelectionBoundary then
            diverged "legal-selection" legal selected.Action
        elif comparison expected <> comparison selected then
            diverged "selection-inputs" (comparison expected) (comparison selected)
        elif referenceTransition <> productionTransition then
            diverged "transition" referenceTransition productionTransition
        else
            Ok
                { Reference = referenceTransition
                  ProductionOutcome = productionOutcome }

    let runTrace root spec selectionOrigin productionMutation referenceMutation =
        let authorityPath = Checkout.rawAuthorityPath root
        let referenceAuthority = ReferenceAuthority.load authorityPath
        let engine = MatchEngine(productionAuthority authorityPath productionMutation)
        let request = referenceRequest spec

        match
            compareStart
                spec.Id
                (ReferenceEngine.start referenceAuthority referenceMutation request)
                (engine.Start(productionStartRequest request))
        with
        | Choice1Of2 result -> result
        | Choice2Of2(referenceStart, productionStartState) ->
            let mutable referenceState = referenceStart.State
            let mutable productionState = productionStartState
            let steps = ResizeArray<CanonicalReplayStep>()
            let mutable divergence = ValueNone
            let mutable completedEndRounds = 0
            let mutable step = 0

            while (divergence.IsNone
                   && referenceState.Phase <> "Complete"
                   && step < CanonicalReplay.FoundationStepBound) do
                let expectedActor =
                    ReferenceEngine.nextActor referenceAuthority referenceMutation referenceState

                let actor, productionFoundation =
                    match selectionOrigin with
                    | ReferenceDriven ->
                        let actor = PlayerId expectedActor

                        actor,
                        engine.GetLegalActions(productionState, actor)
                        |> ProductionProjection.foundationLegalActions
                    | ProductionDriven -> productionActorObservation engine productionState

                let referenceLegal =
                    ReferenceEngine.legalFoundationActions
                        referenceAuthority
                        referenceMutation
                        referenceState
                        actor.Value

                if referenceLegal <> productionFoundation then
                    divergence <-
                        ValueSome
                            { TraceId = spec.Id
                              Stage = $"legal-actions:{step}"
                              ReferenceFact = fact referenceLegal
                              ProductionFact = fact productionFoundation
                              SelectionEvidence = [||] }
                else
                    let expectedLegal =
                        if actor.Value = expectedActor then
                            referenceLegal
                        else
                            ReferenceEngine.legalFoundationActions
                                referenceAuthority
                                referenceMutation
                                referenceState
                                expectedActor

                    let referenceSelected =
                        ReferenceEngine.selectFoundationAction
                            referenceAuthority
                            referenceState
                            completedEndRounds
                            expectedLegal

                    let expected = referenceSelection referenceState expectedActor referenceSelected

                    let selected =
                        match selectionOrigin with
                        | ReferenceDriven -> expected
                        | ProductionDriven ->
                            productionSelection
                                completedEndRounds
                                productionState
                                actor
                                productionFoundation

                    match
                        validateTransition
                            spec.Id
                            step
                            engine
                            referenceAuthority
                            referenceMutation
                            referenceState
                            productionState
                            referenceLegal
                            expected
                            selected
                    with
                    | Error value -> divergence <- ValueSome value
                    | Ok validated ->
                        let referenceTransition = validated.Reference

                        match validated.ProductionOutcome with
                        | CommandOutcome.Rejected _ ->
                            invalidOp "An equivalent applied transition was not applied on replay."
                        | CommandOutcome.Applied(nextProductionState, _) ->
                            steps.Add
                                { Index = step
                                  Actor = selected.Actor
                                  SelectionOrigin =
                                    match selected.Origin with
                                    | ReferenceDriven -> "ReferenceSelection"
                                    | ProductionDriven -> "ProductionObservation"
                                  SelectionChoicePayload = selected.ChoicePayload
                                  SelectionChoices = selected.Choices
                                  SelectionRandomInput = selected.RandomInput
                                  LegalActions = referenceLegal
                                  SelectedAction = selected.Action
                                  State = referenceTransition.State
                                  Events = referenceTransition.Events
                                  Rejection = referenceTransition.Rejection }

                            if selected.Action.Kind = "EndRound" then
                                completedEndRounds <- completedEndRounds + 1

                            referenceState <- referenceTransition.State
                            productionState <- nextProductionState
                            step <- step + 1

            match divergence with
            | ValueSome value -> Diverged value
            | ValueNone when referenceState.Phase <> "Complete" ->
                Diverged
                    { TraceId = spec.Id
                      Stage = "step-bound"
                      ReferenceFact = "Complete"
                      ProductionFact = referenceState.Phase
                      SelectionEvidence = [||] }
            | ValueNone ->
                let replay =
                    { Schema = CanonicalReplay.Schema
                      TraceId = spec.Id
                      Seed = spec.Seed
                      TieBreaker = CanonicalReplay.TieBreaker
                      StepBound = CanonicalReplay.FoundationStepBound
                      ProgramRouteAcceptance = [||]
                      InitialState = referenceStart.State
                      InitialEvents = referenceStart.Events
                      Steps = steps.ToArray()
                      Terminal = referenceState.Terminal }

                Equivalent(replay, CanonicalReplay.bytes replay)

    let runRejectionMatrix root spec =
        let authorityPath = Checkout.rawAuthorityPath root
        let referenceAuthority = ReferenceAuthority.load authorityPath
        let engine = MatchEngine(productionAuthority authorityPath NoProductionMutation)
        let request = referenceRequest spec

        match
            compareStart
                spec.Id
                (ReferenceEngine.start referenceAuthority NoReferenceMutation request)
                (engine.Start(productionStartRequest request))
        with
        | Choice1Of2 result -> result
        | Choice2Of2(referenceStart, productionStartState) ->
            let comparisons = ResizeArray<string * string>()
            let mutable divergence = ValueNone

            let compare name referenceState productionState action =
                if divergence.IsNone then
                    let referenceResult =
                        ReferenceEngine.apply
                            referenceAuthority
                            NoReferenceMutation
                            referenceState
                            action

                    let productionResult =
                        engine.Apply(productionState, productionCommand action)
                        |> ProductionProjection.commandOutcome

                    if referenceResult <> productionResult then
                        divergence <-
                            ValueSome
                                { TraceId = spec.Id
                                  Stage = $"rejection:{name}"
                                  ReferenceFact = fact referenceResult
                                  ProductionFact = fact productionResult
                                  SelectionEvidence = [||] }
                    else
                        comparisons.Add(name, referenceResult.Rejection[0].Code)

            let referenceState = referenceStart.State
            let productionState = productionStartState

            ReferenceEngine.submittedAction
                referenceState
                "rejection:wrong-phase"
                "first"
                referenceState.Transport.Revision
                "EndRound"
                "end"
            |> compare "wrong-phase" referenceState productionState

            ReferenceEngine.submittedAction
                referenceState
                "rejection:wrong-match"
                "first"
                referenceState.Transport.Revision
                "Resign"
                "resign"
            |> ReferenceEngine.withMatchId "other-match"
            |> compare "wrong-match" referenceState productionState

            ReferenceEngine.submittedAction
                referenceState
                "rejection:stale"
                "first"
                (referenceState.Transport.Revision - 1L)
                "Resign"
                "resign"
            |> compare "stale-revision" referenceState productionState

            ReferenceEngine.submittedAction
                referenceState
                "rejection:unknown"
                "outsider"
                referenceState.Transport.Revision
                "Resign"
                "resign"
            |> compare "unknown-actor" referenceState productionState

            let actor =
                ReferenceEngine.nextActor referenceAuthority NoReferenceMutation referenceState

            let opening =
                ReferenceEngine.legalFoundationActions
                    referenceAuthority
                    NoReferenceMutation
                    referenceState
                    actor
                |> ReferenceEngine.selectFoundationAction referenceAuthority referenceState 0

            let nextReference =
                ReferenceEngine.apply referenceAuthority NoReferenceMutation referenceState opening

            let nextProduction = engine.Apply(productionState, productionCommand opening)

            match nextProduction with
            | CommandOutcome.Rejected _ ->
                divergence <-
                    ValueSome
                        { TraceId = spec.Id
                          Stage = "rejection:duplicate-setup"
                          ReferenceFact = "Applied"
                          ProductionFact = "Rejected"
                          SelectionEvidence = [||] }
            | CommandOutcome.Applied(nextProductionState, _) ->
                compare "duplicate-command" nextReference.State nextProductionState opening

            let resignation =
                ReferenceEngine.submittedAction
                    referenceState
                    "rejection:complete-setup"
                    "first"
                    referenceState.Transport.Revision
                    "Resign"
                    "resign"

            let completedReference =
                ReferenceEngine.apply
                    referenceAuthority
                    NoReferenceMutation
                    referenceState
                    resignation

            match engine.Apply(productionState, productionCommand resignation) with
            | CommandOutcome.Rejected _ ->
                divergence <-
                    ValueSome
                        { TraceId = spec.Id
                          Stage = "rejection:complete-setup"
                          ReferenceFact = "Applied"
                          ProductionFact = "Rejected"
                          SelectionEvidence = [||] }
            | CommandOutcome.Applied(completedProduction, _) ->
                let afterComplete =
                    ReferenceEngine.submittedAction
                        completedReference.State
                        "rejection:match-complete"
                        "first"
                        completedReference.State.Transport.Revision
                        "Resign"
                        "resign"

                compare "match-complete" completedReference.State completedProduction afterComplete

            match divergence with
            | ValueSome value -> Diverged value
            | ValueNone -> RejectionsEquivalent(comparisons.ToArray())

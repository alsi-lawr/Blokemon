namespace Blokemon.Differential.Tests

open System.Collections.Immutable
open System.Text
open Blokemon.Game
open Blokemon.ReferenceModel

type LifecycleProgramEvidence =
    { ObligationId: string
      Route: string
      LegalActions: CanonicalAction array array
      SelectedActions: CanonicalAction array
      ChoiceProbes: CanonicalTransition array
      Transitions: CanonicalTransition array
      StepBound: int
      ReplayBytes: byte array }

type LifecycleProgramRun =
    | LifecycleEquivalent of LifecycleProgramEvidence
    | LifecycleDiverged of DifferentialDivergence

[<RequireQualifiedAccess>]
module LifecycleProgramRunner =

    [<Literal>]
    let MaximumStepBound = 12

    let private fact value = sprintf "%A" value

    let private divergence traceId stage referenceFact productionFact =
        LifecycleDiverged
            { TraceId = traceId
              Stage = stage
              ReferenceFact = fact referenceFact
              ProductionFact = fact productionFact
              SelectionEvidence = [||] }

    let private replayBytes obligationId route legal selected probes transitions =
        String.concat
            "\n"
            [ "blokemon-reference-lifecycle-replay-1"
              obligationId
              route
              fact legal
              fact selected
              fact probes
              fact transitions ]
        |> fun value -> Encoding.UTF8.GetBytes(value + "\n")

    let internal runWithProductionAuthority
        productionAuthority
        (authority: ReferenceAuthority)
        (obligation: ReferenceLifecycleObligation)
        (productionInput: ReferenceObligationInput)
        mutation
        =
        let input = obligation.Input

        let engine = MatchEngine(productionAuthority)
        let productionSetup = ProductionSetup.materialize productionInput
        let mutable referenceState = obligation.InitialState

        let mutable productionState =
            ProductionSetup.lifecycleState productionAuthority productionInput

        let legalEvidence = ResizeArray<CanonicalAction array>()
        let selectedEvidence = ResizeArray<CanonicalAction>()
        let probes = ResizeArray<CanonicalTransition>()
        let transitions = ResizeArray<CanonicalTransition>()
        let mutable step = 0
        let mutable result: LifecycleProgramRun voption = ValueNone
        let mutable stop = false

        let compareTransition stage (reference: CanonicalTransition) production =
            if reference.Rejection <> production.Rejection then
                ValueSome(
                    divergence
                        input.Id
                        ($"{stage}-rejection:{step}")
                        reference.Rejection
                        production.Rejection
                )
            elif reference.State <> production.State then
                ValueSome(
                    divergence
                        input.Id
                        ($"{stage}-state:{step}")
                        (DeterministicProgramRunner.stateDifference reference.State production.State)
                        "see reference comparison"
                )
            elif reference.Events <> production.Events then
                ValueSome(
                    divergence
                        input.Id
                        ($"{stage}-events:{step}")
                        reference.Events
                        production.Events
                )
            else
                ValueNone

        let runStep
            (actionInput: ReferenceActionInput)
            (productionAction: ProductionActionInput)
            resolving
            =
            if result.IsNone && not stop then
                if step >= MaximumStepBound then
                    result <- ValueSome(divergence input.Id "step-bound" MaximumStepBound step)
                else
                    let actor =
                        if resolving then
                            referenceState.PendingEffect.Chooser
                        else
                            actionInput.Actor

                    let referenceLegal =
                        ReferenceLifecyclePrograms.legalActions authority referenceState actor
                        |> Array.map (fun value -> { value with Choices = [||] })

                    let productionLegal =
                        engine.GetLegalActions(productionState, PlayerId actor)
                        |> ProductionProjection.lifecycleLegalActions
                        |> Array.map (fun value -> { value with Choices = [||] })

                    if referenceLegal <> productionLegal then
                        result <-
                            ValueSome(
                                divergence
                                    input.Id
                                    ($"legal-actions:{step}")
                                    referenceLegal
                                    productionLegal
                            )
                    else
                        legalEvidence.Add referenceLegal

                        let selected =
                            if resolving then
                                ReferenceLifecyclePrograms.selectResolution
                                    referenceState
                                    actionInput
                                    step
                            else
                                ReferenceLifecyclePrograms.selectAction
                                    authority
                                    referenceState
                                    actionInput
                                    step

                        let productionCommand =
                            if resolving then
                                let translated = ProductionSetup.resolutionCommand selected

                                let structured =
                                    ProductionSetup.structuredResolutionCommand
                                        selected
                                        productionAction

                                if translated <> structured then
                                    result <-
                                        ValueSome(
                                            divergence
                                                input.Id
                                                ($"action-adapter:{step}")
                                                translated
                                                structured
                                        )

                                structured
                            else
                                let translated = ProductionSetup.lifecycleCommand selected

                                let structured =
                                    ProductionSetup.structuredProgramCommand
                                        selected
                                        productionAction

                                if translated <> structured then
                                    result <-
                                        ValueSome(
                                            divergence
                                                input.Id
                                                ($"action-adapter:{step}")
                                                translated
                                                structured
                                        )

                                structured

                        if result.IsNone then
                            selectedEvidence.Add selected

                            if
                                selected.Requirements
                                |> Array.exists (fun requirement ->
                                    requirement.Chooser = selected.Actor)
                            then
                                let probeAction =
                                    { selected with
                                        CommandId = $"obligation:{input.Id}:probe:{step}"
                                        Choices = [||] }

                                let referenceProbe =
                                    if resolving then
                                        ReferenceLifecyclePrograms.resolveEffectChoice
                                            authority
                                            mutation
                                            referenceState
                                            probeAction
                                    else
                                        ReferenceLifecyclePrograms.apply
                                            authority
                                            mutation
                                            referenceState
                                            probeAction

                                let productionProbeCommand =
                                    { productionCommand with
                                        Id = CommandId probeAction.CommandId
                                        Choices = ImmutableArray<_>.Empty }

                                let productionProbe =
                                    engine.Apply(productionState, productionProbeCommand)
                                    |> ProductionProjection.commandOutcome

                                probes.Add referenceProbe

                                match
                                    compareTransition "choice-probe" referenceProbe productionProbe
                                with
                                | ValueSome value -> result <- ValueSome value
                                | ValueNone -> ()

                            if result.IsNone then
                                let referenceTransition =
                                    if resolving then
                                        ReferenceLifecyclePrograms.resolveEffectChoice
                                            authority
                                            mutation
                                            referenceState
                                            selected
                                    else
                                        ReferenceLifecyclePrograms.apply
                                            authority
                                            mutation
                                            referenceState
                                            selected

                                let productionOutcome =
                                    engine.Apply(productionState, productionCommand)

                                let productionTransition =
                                    ProductionProjection.commandOutcome productionOutcome

                                transitions.Add referenceTransition

                                match
                                    compareTransition
                                        "transition"
                                        referenceTransition
                                        productionTransition
                                with
                                | ValueSome value -> result <- ValueSome value
                                | ValueNone ->
                                    step <- step + 1

                                    match productionOutcome with
                                    | CommandOutcome.Applied(next, _) ->
                                        referenceState <- referenceTransition.State
                                        productionState <- next
                                    | CommandOutcome.Rejected _ -> stop <- true

        let projectedInitial = ProductionProjection.state productionState

        if projectedInitial <> referenceState then
            result <-
                ValueSome(divergence input.Id "initial-adapter" referenceState projectedInitial)
        elif productionSetup.Actions.Length <> productionInput.Actions.Length then
            result <-
                ValueSome(
                    divergence
                        input.Id
                        "production-action-count"
                        productionInput.Actions.Length
                        productionSetup.Actions.Length
                )
        else
            for actionIndex in 0 .. input.Actions.Length - 1 do
                if result.IsNone && not stop then
                    let actionInput = input.Actions[actionIndex]
                    let productionAction = productionSetup.Actions[actionIndex]

                    let unavailable = input.InitialState.Route.Value = "activated-unavailable"

                    if unavailable then
                        let referenceLegal =
                            ReferenceLifecyclePrograms.legalActions
                                authority
                                referenceState
                                actionInput.Actor
                            |> Array.map (fun value -> { value with Choices = [||] })

                        let productionLegal =
                            engine.GetLegalActions(productionState, productionAction.Command.Actor)
                            |> ProductionProjection.lifecycleLegalActions
                            |> Array.map (fun value -> { value with Choices = [||] })

                        if referenceLegal <> productionLegal then
                            result <-
                                ValueSome(
                                    divergence
                                        input.Id
                                        "legal-actions:0"
                                        referenceLegal
                                        productionLegal
                                )
                        elif
                            referenceLegal
                            |> Array.exists (fun value -> value.Kind = "UsePartyTrick")
                        then
                            result <-
                                ValueSome(
                                    divergence
                                        input.Id
                                        "unavailable-action"
                                        "no UsePartyTrick action"
                                        referenceLegal
                                )
                        else
                            legalEvidence.Add referenceLegal
                    else
                        runStep actionInput productionAction false

                        while result.IsNone
                              && not stop
                              && referenceState.PendingEffect.Present
                              && productionState.PendingEffect.IsSome do
                            runStep actionInput productionAction true

        match result with
        | ValueSome value -> value
        | ValueNone ->
            let legal = legalEvidence.ToArray()
            let selected = selectedEvidence.ToArray()
            let probeValues = probes.ToArray()
            let transitionValues = transitions.ToArray()

            LifecycleEquivalent
                { ObligationId = input.Id
                  Route = input.InitialState.Route.Value
                  LegalActions = legal
                  SelectedActions = selected
                  ChoiceProbes = probeValues
                  Transitions = transitionValues
                  StepBound = step
                  ReplayBytes =
                    replayBytes
                        input.Id
                        input.InitialState.Route.Value
                        legal
                        selected
                        probeValues
                        transitionValues }

    let runWithProductionInput root authority obligation productionInput mutation =
        let productionAuthority =
            DifferentialRunner.productionAuthority
                (Checkout.rawAuthorityPath root)
                NoProductionMutation

        runWithProductionAuthority productionAuthority authority obligation productionInput mutation

    let run root authority obligation mutation =
        runWithProductionInput root authority obligation obligation.Input mutation

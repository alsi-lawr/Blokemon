namespace Blokemon.Differential.Tests

open System.Collections.Immutable
open System.Text
open Blokemon.Game
open Blokemon.ReferenceModel

type SpecializedTriggerProgramEvidence =
    { ObligationId: string
      Route: string
      LegalActions: CanonicalAction array array
      SelectedActions: CanonicalAction array
      ChoiceProbes: CanonicalTransition array
      Transitions: CanonicalTransition array
      StepBound: int
      ReplayBytes: byte array }

type SpecializedTriggerProgramRun =
    | SpecializedTriggerEquivalent of SpecializedTriggerProgramEvidence
    | SpecializedTriggerDiverged of DifferentialDivergence

[<RequireQualifiedAccess>]
module SpecializedTriggerProgramRunner =

    [<Literal>]
    let MaximumStepBound = 4

    let private fact value = sprintf "%A" value

    let private divergence traceId stage referenceFact productionFact =
        SpecializedTriggerDiverged
            { TraceId = traceId
              Stage = stage
              ReferenceFact = fact referenceFact
              ProductionFact = fact productionFact
              SelectionEvidence = [||] }

    let private replayBytes obligationId route legal selected probes transitions =
        String.concat
            "\n"
            [ "blokemon-reference-specialized-trigger-replay-1"
              obligationId
              route
              fact legal
              fact selected
              fact probes
              fact transitions ]
        |> fun value -> Encoding.UTF8.GetBytes(value + "\n")

    let private stateFact (state: CanonicalState) =
        {| Phase = state.Phase
           ActivePlayer = state.ActivePlayer
           RoundNumber = state.RoundNumber
           Players = state.Players |> Array.map (fun value -> value.Id, value.BarChitsRemaining)
           Cards =
            state.Cards
            |> Array.map (fun value ->
                value.Id,
                value.Zone,
                value.Damage,
                value.IsFaceDown,
                value.StackPosition,
                value.AttachedTo,
                value.Attachments,
                value.RoughStates)
           PendingKnockout = state.PendingKnockout
           PendingBarChits = state.PendingBarChits
           ReplacementPlayer = state.ReplacementPlayer
           PendingRoundEnd = state.PendingRoundEnd
           Terminal = state.Terminal
           Random = state.Random
           RoundUsage = state.RoundUsage
           Transport = state.Transport |}

    let internal runWithProductionAuthority
        productionAuthority
        (authority: ReferenceAuthority)
        (obligation: ReferenceSpecializedTriggerObligation)
        (productionInput: ReferenceObligationInput)
        mutation
        =
        let input = obligation.Input

        let engine = MatchEngine(productionAuthority)
        let productionSetup = ProductionSetup.materialize productionInput
        let mutable referenceState = obligation.InitialState

        let mutable productionState =
            ProductionSetup.specializedTriggerState productionAuthority productionInput

        let legalEvidence = ResizeArray<CanonicalAction array>()
        let selectedEvidence = ResizeArray<CanonicalAction>()
        let probes = ResizeArray<CanonicalTransition>()
        let transitions = ResizeArray<CanonicalTransition>()
        let mutable step = 0
        let mutable result: SpecializedTriggerProgramRun voption = ValueNone
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
                        (stateFact reference.State)
                        (stateFact production.State)
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
                    if step >= MaximumStepBound then
                        result <- ValueSome(divergence input.Id "step-bound" MaximumStepBound step)
                    else
                        let actionInput = input.Actions[actionIndex]
                        let productionAction = productionSetup.Actions[actionIndex]

                        let referenceLegal =
                            ReferenceSpecializedTriggerPrograms.legalActions
                                authority
                                referenceState
                                actionInput.Actor
                            |> Array.map (fun value -> { value with Choices = [||] })

                        let productionLegal =
                            engine.GetLegalActions(productionState, productionAction.Command.Actor)
                            |> ProductionProjection.specializedTriggerLegalActions
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
                                ReferenceSpecializedTriggerPrograms.selectAction
                                    authority
                                    referenceState
                                    actionInput
                                    step

                            let translated = ProductionSetup.specializedTriggerCommand selected

                            let structured =
                                ProductionSetup.structuredProgramCommand selected productionAction

                            if translated <> structured then
                                result <-
                                    ValueSome(
                                        divergence
                                            input.Id
                                            ($"action-adapter:{step}")
                                            translated
                                            structured
                                    )
                            else
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
                                        ReferenceSpecializedTriggerPrograms.apply
                                            authority
                                            mutation
                                            referenceState
                                            probeAction

                                    let productionProbeCommand =
                                        { structured with
                                            Id = CommandId probeAction.CommandId
                                            Choices = ImmutableArray<_>.Empty }

                                    let productionProbe =
                                        engine.Apply(productionState, productionProbeCommand)
                                        |> ProductionProjection.commandOutcome

                                    probes.Add referenceProbe

                                    match
                                        compareTransition
                                            "choice-probe"
                                            referenceProbe
                                            productionProbe
                                    with
                                    | ValueSome value -> result <- ValueSome value
                                    | ValueNone -> ()

                                if result.IsNone then
                                    let referenceTransition =
                                        ReferenceSpecializedTriggerPrograms.apply
                                            authority
                                            mutation
                                            referenceState
                                            selected

                                    let productionOutcome =
                                        engine.Apply(productionState, structured)

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

        match result with
        | ValueSome value -> value
        | ValueNone ->
            let legal = legalEvidence.ToArray()
            let selected = selectedEvidence.ToArray()
            let probeValues = probes.ToArray()
            let transitionValues = transitions.ToArray()

            SpecializedTriggerEquivalent
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

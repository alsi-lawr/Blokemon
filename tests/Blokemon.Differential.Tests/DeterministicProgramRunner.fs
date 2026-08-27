namespace Blokemon.Differential.Tests

open System.Text
open Blokemon.Game
open Blokemon.ReferenceModel

type DeterministicProgramEvidence =
    { ObligationId: string
      Route: string
      LegalActions: CanonicalAction array
      SelectedAction: CanonicalAction
      ChoiceProbe: CanonicalTransition array
      Transition: CanonicalTransition
      StepBound: int
      ReplayBytes: byte array }

type DeterministicProgramRun =
    | DeterministicEquivalent of DeterministicProgramEvidence
    | DeterministicDiverged of DifferentialDivergence

[<RequireQualifiedAccess>]
module DeterministicProgramRunner =

    [<Literal>]
    let StepBound = 1

    let private fact value = sprintf "%A" value

    let private divergence traceId stage referenceFact productionFact =
        DeterministicDiverged
            { TraceId = traceId
              Stage = stage
              ReferenceFact = fact referenceFact
              ProductionFact = fact productionFact
              SelectionEvidence = [||] }

    let stateDifference (reference: CanonicalState) (production: CanonicalState) =
        let differingFields pairs =
            pairs |> Array.choose (fun (name, same) -> if same then None else Some name)

        let cardChanges =
            Set.union
                (reference.Cards |> Seq.map _.Id |> Set.ofSeq)
                (production.Cards |> Seq.map _.Id |> Set.ofSeq)
            |> Seq.choose (fun id ->
                let left = reference.Cards |> Array.tryFind (fun value -> value.Id = id)
                let right = production.Cards |> Array.tryFind (fun value -> value.Id = id)

                match left, right with
                | Some leftCard, Some rightCard when leftCard <> rightCard ->
                    Some(
                        id,
                        differingFields
                            [| "MechanicalId", leftCard.MechanicalId = rightCard.MechanicalId
                               "Owner", leftCard.Owner = rightCard.Owner
                               "Kind", leftCard.Kind = rightCard.Kind
                               "Zone", leftCard.Zone = rightCard.Zone
                               "IsFaceDown", leftCard.IsFaceDown = rightCard.IsFaceDown
                               "StackPosition", leftCard.StackPosition = rightCard.StackPosition
                               "AttachedTo", leftCard.AttachedTo = rightCard.AttachedTo
                               "Attachments", leftCard.Attachments = rightCard.Attachments
                               "UnderlyingCards",
                               leftCard.UnderlyingCards = rightCard.UnderlyingCards
                               "Damage", leftCard.Damage = rightCard.Damage
                               "RoughStates", leftCard.RoughStates = rightCard.RoughStates
                               "EnteredAtOwnerRound",
                               leftCard.EnteredAtOwnerRound = rightCard.EnteredAtOwnerRound
                               "LastPromotedRound",
                               leftCard.LastPromotedRound = rightCard.LastPromotedRound |]
                    )
                | None, Some _ -> Some(id, [| "MissingFromReference" |])
                | Some _, None -> Some(id, [| "MissingFromProduction" |])
                | _ -> None)
            |> Seq.toArray

        let effectIdentity (value: CanonicalTemporaryEffect) =
            value.SourceEffect,
            value.SourceCard,
            value.TargetCard,
            value.Kind,
            value.Amount,
            value.MechanicalTypes,
            value.RoughStates,
            value.RelatedCards,
            value.Conditions,
            value.Duration,
            value.AppliesFromRound,
            value.ExpiresAfterRound

        let referenceEffects = reference.Effects |> Array.map effectIdentity
        let productionEffects = production.Effects |> Array.map effectIdentity

        let onlyIn left right =
            left |> Array.filter (fun value -> not (Array.contains value right))

        let topLevel =
            differingFields
                [| "Phase", reference.Phase = production.Phase
                   "ActivePlayer", reference.ActivePlayer = production.ActivePlayer
                   "RoundNumber", reference.RoundNumber = production.RoundNumber
                   "Players", reference.Players = production.Players
                   "RoundUsage", reference.RoundUsage = production.RoundUsage
                   "PendingEffect", reference.PendingEffect = production.PendingEffect
                   "PendingKnockout", reference.PendingKnockout = production.PendingKnockout
                   "PendingBarChits", reference.PendingBarChits = production.PendingBarChits
                   "ReplacementPlayer", reference.ReplacementPlayer = production.ReplacementPlayer
                   "PendingRoundEnd", reference.PendingRoundEnd = production.PendingRoundEnd
                   "Terminal", reference.Terminal = production.Terminal
                   "Random", reference.Random = production.Random
                   "Transport", reference.Transport = production.Transport |]

        let pendingEffect =
            differingFields
                [| "Present", reference.PendingEffect.Present = production.PendingEffect.Present
                   "Action", reference.PendingEffect.Action = production.PendingEffect.Action
                   "Source", reference.PendingEffect.Source = production.PendingEffect.Source
                   "Effect", reference.PendingEffect.Effect = production.PendingEffect.Effect
                   "Chooser", reference.PendingEffect.Chooser = production.PendingEffect.Chooser
                   "Requirements",
                   reference.PendingEffect.Requirements = production.PendingEffect.Requirements
                   "BeerMatResults",
                   reference.PendingEffect.BeerMatResults = production.PendingEffect.BeerMatResults
                   "AttackStarted",
                   reference.PendingEffect.AttackStarted = production.PendingEffect.AttackStarted |]

        let transport =
            differingFields
                [| "Revision", reference.Transport.Revision = production.Transport.Revision
                   "LastEventSequence",
                   reference.Transport.LastEventSequence = production.Transport.LastEventSequence
                   "ProcessedCommandIds",
                   reference.Transport.ProcessedCommandIds =
                       production.Transport.ProcessedCommandIds |]

        $"top={fact topLevel}; cards={fact cardChanges}; pending={fact pendingEffect}; transport={fact transport}; reference-only-effects={fact (onlyIn referenceEffects productionEffects)}; production-only-effects={fact (onlyIn productionEffects referenceEffects)}"

    let private replayBytes
        obligationId
        route
        (legal: CanonicalAction array)
        selected
        probes
        transition
        =
        let text =
            String.concat
                "\n"
                [ "blokemon-reference-deterministic-replay-1"
                  obligationId
                  route
                  fact legal
                  fact selected
                  fact probes
                  fact transition ]

        Encoding.UTF8.GetBytes(text + "\n")

    let internal runWithProductionAuthority
        productionAuthority
        (authority: ReferenceAuthority)
        (obligation: ReferenceDeterministicObligation)
        (productionInput: ReferenceObligationInput)
        mutation
        =
        let input = obligation.Input
        let initial = obligation.InitialState

        let engine = MatchEngine(productionAuthority)
        let productionSetup = ProductionSetup.materialize productionInput

        let mutable productionState =
            ProductionSetup.deterministicState productionAuthority productionInput

        let projectedInitial = ProductionProjection.state productionState

        if projectedInitial <> initial then
            divergence input.Id "initial-adapter" initial projectedInitial
        elif input.Actions.Length <> StepBound then
            divergence input.Id "step-bound" StepBound input.Actions.Length
        elif productionSetup.Actions.Length <> StepBound then
            divergence input.Id "production-step-bound" StepBound productionSetup.Actions.Length
        else
            let actionInput = input.Actions[0]
            let productionActionInput = productionSetup.Actions[0]

            let referenceLegal =
                ReferenceDeterministicPrograms.legalActions authority initial actionInput.Actor

            let productionLegal =
                engine.GetLegalActions(productionState, productionActionInput.Command.Actor)
                |> ProductionProjection.commonLegalActions
                |> Array.map (fun value -> { value with Choices = [||] })

            if referenceLegal <> productionLegal then
                divergence input.Id "legal-actions:0" referenceLegal productionLegal
            else
                let selected =
                    ReferenceDeterministicPrograms.selectAction authority initial actionInput 0

                let translatedProductionCommand = ProductionSetup.programCommand selected

                let structuredProductionCommand =
                    ProductionSetup.structuredProgramCommand selected productionActionInput

                if translatedProductionCommand <> structuredProductionCommand then
                    divergence
                        input.Id
                        "action-adapter:0"
                        translatedProductionCommand
                        structuredProductionCommand
                else
                    let probes = ResizeArray<CanonicalTransition>()
                    let mutable probeDivergence = ValueNone

                    if
                        selected.Requirements
                        |> Array.exists (fun requirement -> requirement.Chooser = selected.Actor)
                    then
                        let probe =
                            { selected with
                                CommandId = $"obligation:{input.Id}:choice-probe"
                                Choices = [||] }

                        let referenceProbe =
                            ReferenceDeterministicPrograms.apply authority mutation initial probe

                        let productionProbeCommand =
                            { structuredProductionCommand with
                                Id = CommandId probe.CommandId
                                Choices = System.Collections.Immutable.ImmutableArray<_>.Empty }

                        let productionProbe =
                            engine.Apply(productionState, productionProbeCommand)
                            |> ProductionProjection.commandOutcome

                        probes.Add referenceProbe

                        if referenceProbe <> productionProbe then
                            probeDivergence <-
                                ValueSome(
                                    divergence
                                        input.Id
                                        "choice-rejection:0"
                                        referenceProbe
                                        productionProbe
                                )

                    match probeDivergence with
                    | ValueSome result -> result
                    | ValueNone ->
                        let referenceTransition =
                            ReferenceDeterministicPrograms.apply authority mutation initial selected

                        let productionOutcome =
                            engine.Apply(productionState, structuredProductionCommand)

                        let productionTransition =
                            productionOutcome |> ProductionProjection.commandOutcome

                        if referenceTransition.Rejection <> productionTransition.Rejection then
                            divergence
                                input.Id
                                "transition-rejection:0"
                                referenceTransition.Rejection
                                productionTransition.Rejection
                        elif referenceTransition.State <> productionTransition.State then
                            divergence
                                input.Id
                                "transition-state:0"
                                (stateDifference
                                    referenceTransition.State
                                    productionTransition.State)
                                "see reference comparison"
                        elif referenceTransition.Events <> productionTransition.Events then
                            divergence
                                input.Id
                                "transition-events:0"
                                referenceTransition.Events
                                productionTransition.Events
                        elif
                            referenceTransition.State.Random <> initial.Random
                            || productionTransition.State.Random <> initial.Random
                        then
                            divergence
                                input.Id
                                "deterministic-rng:0"
                                initial.Random
                                (referenceTransition.State.Random, productionTransition.State.Random)
                        else
                            match productionOutcome with
                            | CommandOutcome.Rejected _ ->
                                divergence
                                    input.Id
                                    "selected-rejection:0"
                                    "Applied"
                                    productionTransition
                            | CommandOutcome.Applied(next, _) ->
                                productionState <- next
                                let probeValues = probes.ToArray()

                                DeterministicEquivalent
                                    { ObligationId = input.Id
                                      Route = input.InitialState.Route.Value
                                      LegalActions = referenceLegal
                                      SelectedAction = selected
                                      ChoiceProbe = probeValues
                                      Transition = referenceTransition
                                      StepBound = StepBound
                                      ReplayBytes =
                                        replayBytes
                                            input.Id
                                            input.InitialState.Route.Value
                                            referenceLegal
                                            selected
                                            probeValues
                                            referenceTransition }

    let runWithProductionInput root authority obligation productionInput mutation =
        let productionAuthority =
            DifferentialRunner.productionAuthority
                (Checkout.rawAuthorityPath root)
                NoProductionMutation

        runWithProductionAuthority productionAuthority authority obligation productionInput mutation

    let run root authority obligation mutation =
        runWithProductionInput root authority obligation obligation.Input mutation

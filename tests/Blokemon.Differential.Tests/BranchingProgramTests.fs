namespace Blokemon.Differential.Tests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json.Nodes
open Blokemon.ReferenceModel
open FsUnit
open TUnit.Core

type BranchingProgramTests() =

    let root = Checkout.repositoryRoot ()
    let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

    let obligations () =
        ReferenceObligations.load authority (Checkout.obligationPath root)
        |> ReferenceBranchingPrograms.materialize authority

    let obligation id =
        obligations () |> Array.find (fun value -> value.Input.Id = id)

    let equivalent name result =
        match result with
        | BranchingEquivalent evidence -> evidence
        | BranchingDiverged divergence ->
            failwith
                $"{name} diverged at {divergence.Stage}. Reference: {divergence.ReferenceFact}. Production: {divergence.ProductionFact}."

    let required description (node: JsonNode | null) =
        match node with
        | null -> failwith $"The test mutation could not find {description}."
        | value -> value

    let temporaryJson (node: JsonNode) action =
        let path = Path.Combine(Path.GetTempPath(), $"blokemon-136-{Guid.NewGuid():N}.json")

        try
            File.WriteAllText(path, node.ToJsonString())
            action path
        finally
            File.Delete path

    static member ObligationIds() : IEnumerable<objnull array> =
        let root = Checkout.repositoryRoot ()
        let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

        ReferenceObligations.load authority (Checkout.obligationPath root)
        |> ReferenceBranchingPrograms.reconcile
        |> Seq.map (fun value -> [| value.Id :> objnull |])

    [<Test>]
    member _.``branching ledger should reconcile only its accepted route and obligation slice``() =
        let values = obligations ()

        values.Length |> should equal ReferenceBranchingPrograms.AcceptedObligationCount

        values
        |> Seq.map (_.Input.InitialState.Route.Value)
        |> Set.ofSeq
        |> should equal ReferenceBranchingPrograms.acceptedRoutes

        ReferenceBranchingPrograms.acceptedRoutes.Count
        |> should equal ReferenceBranchingPrograms.AcceptedRouteCount

        ReferenceBranchingPrograms.acceptedObligationIds.Count
        |> should equal ReferenceBranchingPrograms.AcceptedObligationCount

        (DifferentialRunner.bootstrap root).ProgramRouteAcceptance
        |> _.Count
        |> should equal 0

    [<Test>]
    [<MethodDataSource(nameof BranchingProgramTests.ObligationIds)>]
    member _.``each accepted branching obligation should match every public engine boundary``
        (obligationId: string)
        =
        let evidence =
            BranchingProgramRunner.run root authority (obligation obligationId) NoProgramMutation
            |> equivalent obligationId

        evidence.ObligationId |> should equal obligationId

        evidence.StepBound
        |> should be (lessThanOrEqualTo BranchingProgramRunner.MaximumStepBound)

        evidence.Transitions.Length |> should equal evidence.StepBound

        let finalTransition = evidence.Transitions |> Array.last

        if obligationId = "bring-the-trades-rejects-duplicate-types" then
            finalTransition.Rejection
            |> Array.map _.Code
            |> should equal [| "InvalidChoice" |]
        else
            finalTransition.Rejection |> should equal [||]

    [<Test>]
    member _.``branching obligation replays should be bounded and byte identical``() =
        for value in obligations () do
            let first =
                BranchingProgramRunner.run root authority value NoProgramMutation
                |> equivalent value.Input.Id

            let second =
                BranchingProgramRunner.run root authority value NoProgramMutation
                |> equivalent value.Input.Id

            first.StepBound
            |> should be (lessThanOrEqualTo BranchingProgramRunner.MaximumStepBound)

            first.ReplayBytes |> should equal second.ReplayBytes

    [<Test>]
    member _.``named branching random and selection mutants should diverge at owned transitions``
        ()
        =
        let cases =
            [| "shirt-off-badge", FlipProgramBeerMatResult
               "conditional-adjust-blk-006-b01-true", InvertProgramBranch
               "vernalanche-one-qualifying-top-card", ReverseProgramCandidateOrder |]

        for id, mutation in cases do
            match BranchingProgramRunner.run root authority (obligation id) mutation with
            | BranchingDiverged divergence ->
                divergence.TraceId |> should equal id

                divergence.Stage.StartsWith("transition-", StringComparison.Ordinal)
                |> should be True
            | result -> failwith $"The named {mutation} mutant survived: {result}."

    [<Test>]
    member _.``production choice drift should fail before branching route credit``() =
        let baseline = obligation "optional-maximum-blk-008-b01"
        let input = baseline.Input
        let action = input.Actions[0]

        let driftedChoices =
            action.Choices
            |> Array.map (fun value ->
                match value.Value with
                | ReferenceChoiceValue.Cards _ ->
                    { value with
                        Value = ReferenceChoiceValue.Cards [||] }
                | _ -> value)

        let productionInput =
            { input with
                Actions = [| { action with Choices = driftedChoices } |] }

        match
            BranchingProgramRunner.runWithProductionInput
                root
                authority
                baseline
                productionInput
                NoProgramMutation
        with
        | BranchingDiverged divergence ->
            divergence.TraceId |> should equal baseline.Input.Id
            divergence.Stage |> should equal "action-adapter:1"
        | result -> failwith $"The production selection drift survived: {result}."

    [<Test>]
    member _.``raw conditional operand drift should change independent reference execution``() =
        let baseline = obligation "conditional-adjust-blk-006-b01-true"

        let authorityNode =
            JsonNode.Parse(File.ReadAllText(Checkout.rawAuthorityPath root))
            |> required "authority root"

        let owner =
            authorityNode["collectibles"]
            |> required "collectibles"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                (value["id"] |> required "collectible id").GetValue<string>() = "BLK-006")

        let attack =
            owner["attacks"]
            |> required "attacks"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                (value["mechanicalId"] |> required "attack id").GetValue<string>() = "BLK-006-B01")

        let conditional =
            attack["program"]
            |> required "program"
            |> _.AsArray()
            |> fun values -> values[1] |> required "conditional"

        let adjustment =
            conditional["then"]
            |> required "then branch"
            |> _.AsArray()
            |> fun values -> values[0] |> required "adjustment"

        adjustment["amount"] <- JsonValue.Create 101 |> required "mutated adjustment"

        temporaryJson authorityNode (fun path ->
            let changedAuthority = ReferenceAuthority.load path

            let selected =
                ReferenceBranchingPrograms.selectAction
                    authority
                    baseline.InitialState
                    baseline.Input.Actions[0]
                    0

            let original =
                ReferenceBranchingPrograms.apply
                    authority
                    NoProgramMutation
                    baseline.InitialState
                    selected

            let changed =
                ReferenceBranchingPrograms.apply
                    changedAuthority
                    NoProgramMutation
                    baseline.InitialState
                    selected

            original |> should not' (equal changed))

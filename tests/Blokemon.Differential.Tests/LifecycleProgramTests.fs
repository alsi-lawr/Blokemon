namespace Blokemon.Differential.Tests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json.Nodes
open Blokemon.ReferenceModel
open FsUnit
open TUnit.Core

type LifecycleProgramTests() =

    let root = Checkout.repositoryRoot ()
    let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

    let obligations () =
        ReferenceObligations.load authority (Checkout.obligationPath root)
        |> ReferenceLifecyclePrograms.materialize authority

    let obligation id =
        obligations () |> Array.find (fun value -> value.Input.Id = id)

    let equivalent name result =
        match result with
        | LifecycleEquivalent evidence -> evidence
        | LifecycleDiverged divergence ->
            failwith
                $"{name} diverged at {divergence.Stage}. Reference: {divergence.ReferenceFact}. Production: {divergence.ProductionFact}."

    let required description (node: JsonNode | null) =
        match node with
        | null -> failwith $"The test mutation could not find {description}."
        | value -> value

    let temporaryJson (node: JsonNode) action =
        let path = Path.Combine(Path.GetTempPath(), $"blokemon-137-{Guid.NewGuid():N}.json")

        try
            File.WriteAllText(path, node.ToJsonString())
            action path
        finally
            File.Delete path

    static member ObligationIds() : IEnumerable<objnull array> =
        let root = Checkout.repositoryRoot ()
        let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

        ReferenceObligations.load authority (Checkout.obligationPath root)
        |> ReferenceLifecyclePrograms.reconcile
        |> Seq.map (fun value -> [| value.Id :> objnull |])

    [<Test>]
    member _.``lifecycle ledger should reconcile only its accepted route and obligation slice``() =
        let values = obligations ()

        values.Length |> should equal ReferenceLifecyclePrograms.AcceptedObligationCount

        values
        |> Seq.map (_.Input.InitialState.Route.Value)
        |> Set.ofSeq
        |> should equal ReferenceLifecyclePrograms.acceptedRoutes

        ReferenceLifecyclePrograms.acceptedRoutes.Count
        |> should equal ReferenceLifecyclePrograms.AcceptedRouteCount

        ReferenceLifecyclePrograms.acceptedObligationIds.Count
        |> should equal ReferenceLifecyclePrograms.AcceptedObligationCount

        ReferenceLifecyclePrograms.acceptedRoutes.Contains "promotion-trigger"
        |> should be False

        Set.intersect
            ReferenceLifecyclePrograms.acceptedRoutes
            ReferenceBranchingPrograms.acceptedRoutes
        |> should be Empty

        Set.intersect
            ReferenceLifecyclePrograms.acceptedObligationIds
            ReferenceBranchingPrograms.acceptedObligationIds
        |> should be Empty

    [<Test>]
    [<MethodDataSource(nameof LifecycleProgramTests.ObligationIds)>]
    member _.``each accepted lifecycle obligation should match every public engine boundary``
        (obligationId: string)
        =
        let value = obligation obligationId

        let evidence =
            LifecycleProgramRunner.run root authority value NoLifecycleMutation
            |> equivalent obligationId

        evidence.ObligationId |> should equal obligationId

        evidence.StepBound
        |> should be (lessThanOrEqualTo LifecycleProgramRunner.MaximumStepBound)

        if value.Input.InitialState.Route.Value = "activated-unavailable" then
            evidence.StepBound |> should equal 0
            evidence.Transitions |> should be Empty
        else
            evidence.Transitions.Length |> should equal evidence.StepBound
            evidence.Transitions |> Array.last |> _.Rejection |> should be Empty

    [<Test>]
    member _.``lifecycle obligation replays should be bounded and byte identical``() =
        for value in obligations () do
            let first =
                LifecycleProgramRunner.run root authority value NoLifecycleMutation
                |> equivalent value.Input.Id

            let second =
                LifecycleProgramRunner.run root authority value NoLifecycleMutation
                |> equivalent value.Input.Id

            first.StepBound
            |> should be (lessThanOrEqualTo LifecycleProgramRunner.MaximumStepBound)

            first.ReplayBytes |> should equal second.ReplayBytes

    [<Test>]
    member _.``promotion decline should retain the pile and clear transient target state``() =
        let evidence =
            LifecycleProgramRunner.run
                root
                authority
                (obligation "promotion-decline-blk-044-t01")
                NoLifecycleMutation
            |> equivalent "promotion-decline-blk-044-t01"

        let transition = evidence.Transitions |> Array.exactlyOne

        let promoted =
            transition.State.Cards |> Array.find (fun value -> value.Id = "promotion")

        let underlying =
            transition.State.Cards |> Array.find (fun value -> value.Id = "attacker")

        let retainedVim =
            transition.State.Cards |> Array.find (fun value -> value.Id = "retained-vim")

        promoted.Zone |> should equal "Oche"
        promoted.Damage |> should equal 20
        promoted.Attachments |> should equal [| "retained-vim" |]
        promoted.RoughStates |> should be Empty
        underlying.Zone |> should equal "Attached"
        underlying.RoughStates |> should be Empty
        retainedVim.AttachedTo |> should equal "promotion"

        evidence.SelectedActions
        |> Array.exactlyOne
        |> _.Requirements
        |> Array.map (fun value -> value.Kind)
        |> should equal [| "Optional"; "Attachments" |]

    [<Test>]
    member _.``named lifecycle and refresh mutants should diverge at owned transitions``() =
        let cases =
            [| "play-kit-007-r01", SkipLifecycleUsage
               "continuous-blk-009-t01-applies", SkipContinuousEffectRegistration |]

        for id, mutation in cases do
            match LifecycleProgramRunner.run root authority (obligation id) mutation with
            | LifecycleDiverged divergence ->
                divergence.TraceId |> should equal id

                divergence.Stage.StartsWith("transition-", StringComparison.Ordinal)
                |> should be True
            | result -> failwith $"The named {mutation} lifecycle mutant survived: {result}."

    [<Test>]
    member _.``structured lifecycle drift should fail before route credit``() =
        let node =
            JsonNode.Parse(File.ReadAllText(Checkout.obligationPath root))
            |> required "obligation root"

        let assigned =
            node["obligations"]
            |> required "obligations"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                (value["id"] |> required "obligation id").GetValue<string>() = "play-kit-001-r01")

        let initialState = assigned["initialState"] |> required "initial state"

        initialState["route"] <- JsonValue.Create "promotion-trigger" |> required "mutated route"

        temporaryJson node (fun path ->
            let inventory = ReferenceObligations.load authority path

            (fun () -> ReferenceLifecyclePrograms.reconcile inventory |> ignore)
            |> should throw typeof<InvalidOperationException>)

    [<Test>]
    member _.``lifecycle obligation identity drift should fail before route credit``() =
        let node =
            JsonNode.Parse(File.ReadAllText(Checkout.obligationPath root))
            |> required "obligation root"

        let assigned =
            node["obligations"]
            |> required "obligations"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                (value["id"] |> required "obligation id").GetValue<string>() = "local-decline-kit-006-r01")

        assigned["id"] <- JsonValue.Create "stale-local-decline-kit-006-r01" |> required "stale id"

        temporaryJson node (fun path ->
            let inventory = ReferenceObligations.load authority path

            (fun () -> ReferenceLifecyclePrograms.reconcile inventory |> ignore)
            |> should throw typeof<InvalidOperationException>)

    [<Test>]
    member _.``production lifecycle materialization should expose structured input drift``() =
        let baseline = obligation "play-kit-007-r01"
        let input = baseline.Input
        let initial = input.InitialState

        let routeDrift =
            (obligation "continuous-blk-009-t01-applies").Input.InitialState.Route

        let actionDrift =
            { input.Actions[0] with
                SourceCard = "stale-kit" }

        let cases =
            [| "parameter",
               { input with
                   InitialState =
                       { initial with
                           Parameters =
                               initial.Parameters
                               |> Array.mapi (fun index value ->
                                   if index = 0 then "KIT-008" else value) } },
               "initial-adapter"
               "seed",
               { input with
                   RandomSeed = input.RandomSeed + 1UL },
               "initial-adapter"
               "route",
               { input with
                   InitialState = { initial with Route = routeDrift } },
               "initial-adapter"
               "action",
               { input with
                   Actions = [| actionDrift |] },
               "action-adapter:0" |]

        for name, productionInput, expectedStage in cases do
            match
                LifecycleProgramRunner.runWithProductionInput
                    root
                    authority
                    baseline
                    productionInput
                    NoLifecycleMutation
            with
            | LifecycleDiverged divergence ->
                divergence.TraceId |> should equal baseline.Input.Id
                divergence.Stage |> should equal expectedStage
            | result -> failwith $"The production {name} lifecycle drift survived: {result}."

    [<Test>]
    member _.``raw continuous operand drift should change independent reference execution``() =
        let baseline = obligation "continuous-blk-009-t01-applies"

        let authorityNode =
            JsonNode.Parse(File.ReadAllText(Checkout.rawAuthorityPath root))
            |> required "authority root"

        let owner =
            authorityNode["collectibles"]
            |> required "collectibles"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                (value["id"] |> required "collectible id").GetValue<string>() = "BLK-009")

        let trick =
            owner["partyTricks"]
            |> required "party tricks"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                (value["mechanicalId"] |> required "trick id").GetValue<string>() = "BLK-009-T01")

        let reduction =
            trick["program"]
            |> required "program"
            |> _.AsArray()
            |> fun values -> values[1] |> required "reduction"

        reduction["amount"] <- JsonValue.Create 31 |> required "mutated amount"

        temporaryJson authorityNode (fun path ->
            let changedAuthority = ReferenceAuthority.load path

            let selected =
                ReferenceLifecyclePrograms.selectAction
                    authority
                    baseline.InitialState
                    baseline.Input.Actions[0]
                    0

            let original =
                ReferenceLifecyclePrograms.apply
                    authority
                    NoLifecycleMutation
                    baseline.InitialState
                    selected

            let changed =
                ReferenceLifecyclePrograms.apply
                    changedAuthority
                    NoLifecycleMutation
                    baseline.InitialState
                    selected

            original |> should not' (equal changed))

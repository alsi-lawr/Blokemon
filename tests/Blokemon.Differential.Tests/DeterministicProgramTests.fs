namespace Blokemon.Differential.Tests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.ReferenceModel
open FsUnit
open TUnit.Core

type DeterministicProgramTests() =

    let root = Checkout.repositoryRoot ()
    let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

    let obligations () =
        ReferenceObligations.load authority (Checkout.obligationPath root)
        |> ReferenceDeterministicPrograms.materialize authority

    let obligation id =
        obligations () |> Array.find (fun value -> value.Input.Id = id)

    let equivalent name result =
        match result with
        | DeterministicEquivalent evidence -> evidence
        | DeterministicDiverged divergence ->
            failwith
                $"{name} diverged at {divergence.Stage}. Reference: {divergence.ReferenceFact}. Production: {divergence.ProductionFact}."

    let required description (node: JsonNode | null) =
        match node with
        | null -> failwith $"The test mutation could not find {description}."
        | value -> value

    let temporaryJson (node: JsonNode) action =
        let path = Path.Combine(Path.GetTempPath(), $"blokemon-135-{Guid.NewGuid():N}.json")

        try
            File.WriteAllText(path, node.ToJsonString())
            action path
        finally
            File.Delete path

    static member ObligationIds() : IEnumerable<objnull array> =
        let root = Checkout.repositoryRoot ()
        let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

        ReferenceObligations.load authority (Checkout.obligationPath root)
        |> ReferenceDeterministicPrograms.reconcile
        |> Seq.map (fun value -> [| value.Id :> objnull |])

    [<Test>]
    member _.``deterministic ledger should reconcile only its accepted route and obligation slice``
        ()
        =
        let values = obligations ()

        values.Length
        |> should equal ReferenceDeterministicPrograms.AcceptedObligationCount

        values
        |> Seq.map (_.Input.InitialState.Route.Value)
        |> Set.ofSeq
        |> should equal ReferenceDeterministicPrograms.acceptedRoutes

        ReferenceDeterministicPrograms.acceptedRoutes.Count
        |> should equal ReferenceDeterministicPrograms.AcceptedRouteCount

        ReferenceDeterministicPrograms.acceptedObligationIds.Count
        |> should equal ReferenceDeterministicPrograms.AcceptedObligationCount

        (DifferentialRunner.bootstrap root).ProgramRouteAcceptance
        |> _.Count
        |> should equal 0

    [<Test>]
    [<MethodDataSource(nameof DeterministicProgramTests.ObligationIds)>]
    member _.``each accepted deterministic obligation should match the public engine exactly``
        (obligationId: string)
        =
        let evidence =
            DeterministicProgramRunner.run
                root
                authority
                (obligation obligationId)
                NoProgramMutation
            |> equivalent obligationId

        evidence.ObligationId |> should equal obligationId
        evidence.StepBound |> should equal 1
        evidence.Transition.Rejection |> should equal [||]

    [<Test>]
    member _.``deterministic obligation replays should be bounded and byte identical``() =
        for value in obligations () do
            let first =
                DeterministicProgramRunner.run root authority value NoProgramMutation
                |> equivalent value.Input.Id

            let second =
                DeterministicProgramRunner.run root authority value NoProgramMutation
                |> equivalent value.Input.Id

            first.StepBound |> should equal 1
            first.ReplayBytes |> should equal second.ReplayBytes

    [<Test>]
    member _.``structured route drift should fail before deterministic credit``() =
        let node =
            JsonNode.Parse(File.ReadAllText(Checkout.obligationPath root))
            |> required "obligation root"

        let assigned =
            node["obligations"]
            |> required "obligations"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                let initialState = value["initialState"] |> required "initial state"
                (initialState["route"] |> required "route").GetValue<string>() = "trivial-damage")

        let assignedInitialState =
            assigned["initialState"] |> required "assigned initial state"

        assignedInitialState["route"] <-
            JsonValue.Create "shirt-off-badge" |> required "mutated route"

        temporaryJson node (fun path ->
            let inventory = ReferenceObligations.load authority path

            (fun () -> ReferenceDeterministicPrograms.reconcile inventory |> ignore)
            |> should throw typeof<InvalidOperationException>)

    [<Test>]
    member _.``obligation identity drift should fail before deterministic credit``() =
        let node =
            JsonNode.Parse(File.ReadAllText(Checkout.obligationPath root))
            |> required "obligation root"

        let assigned =
            node["obligations"]
            |> required "obligations"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                let id = (value["id"] |> required "obligation id").GetValue<string>()

                id = "trivial-damage-blk-002-b02")

        assigned["id"] <- JsonValue.Create "stale-trivial-damage-blk-002-b02" |> required "stale id"

        temporaryJson node (fun path ->
            let inventory = ReferenceObligations.load authority path

            (fun () -> ReferenceDeterministicPrograms.reconcile inventory |> ignore)
            |> should throw typeof<InvalidOperationException>)

    [<Test>]
    member _.``production materialization should expose every structured input drift before route credit``
        ()
        =
        let baseline = obligation "trivial-damage-blk-002-b02"
        let input = baseline.Input
        let initial = input.InitialState

        let routeDrift = (obligation "damage-heal-blk-001-b01").Input.InitialState.Route

        let cardDrift: ReferenceCardSetupInput =
            { CardId = "production-only-card"
              Owner = "first"
              MechanicalId = "KIT-001"
              Zone = ReferenceZone.Mitt }

        let playerDrift: ReferencePlayerSetupInput =
            { Player = "first"
              BarChitsRemaining = 5 }

        let zoneDrift: ReferenceZoneCountInput =
            { Owner = "first"
              Zone = ReferenceZone.Mitt
              Count = 1 }

        let actionDrift =
            { input.Actions[0] with
                EffectId = "stale-effect" }

        let cases =
            [| "parameter",
               { input with
                   InitialState =
                       { initial with
                           Parameters =
                               initial.Parameters
                               |> Array.mapi (fun index value ->
                                   if index = 1 then "BLK-004" else value) } },
               "initial-adapter"
               "seed",
               { input with
                   RandomSeed = input.RandomSeed + 1UL },
               "initial-adapter"
               "route",
               { input with
                   InitialState = { initial with Route = routeDrift } },
               "initial-adapter"
               "card",
               { input with
                   InitialState = { initial with Cards = [| cardDrift |] } },
               "initial-adapter"
               "player",
               { input with
                   InitialState =
                       { initial with
                           Players = [| playerDrift |] } },
               "initial-adapter"
               "zone count",
               { input with
                   InitialState =
                       { initial with
                           ZoneCounts = [| zoneDrift |] } },
               "initial-adapter"
               "action",
               { input with
                   Actions = [| actionDrift |] },
               "action-adapter:0" |]

        for name, productionInput, expectedStage in cases do
            match
                DeterministicProgramRunner.runWithProductionInput
                    root
                    authority
                    baseline
                    productionInput
                    NoProgramMutation
            with
            | DeterministicDiverged divergence ->
                divergence.TraceId |> should equal baseline.Input.Id
                divergence.Stage |> should equal expectedStage
            | result -> failwith $"The production {name} drift survived: {result}."

    [<Test>]
    member _.``raw operand drift should change reference execution without fixture output``() =
        let baseline = obligation "trivial-damage-blk-002-b02"

        let authorityNode =
            JsonNode.Parse(File.ReadAllText(Checkout.rawAuthorityPath root))
            |> required "authority root"

        let owner =
            authorityNode["collectibles"]
            |> required "collectibles"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                (value["id"] |> required "collectible id").GetValue<string>() = "BLK-002")

        let attack =
            owner["attacks"]
            |> required "attacks"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                let mechanicalId =
                    (value["mechanicalId"] |> required "attack mechanical id").GetValue<string>()

                mechanicalId = "BLK-002-B02")

        let instruction =
            attack["program"]
            |> required "program"
            |> _.AsArray()
            |> fun values -> values[0] |> required "first program instruction"

        instruction["amount"] <- JsonValue.Create 81 |> required "mutated instruction amount"
        attack["printedDamage"] <- JsonValue.Create 81 |> required "mutated printed damage"

        temporaryJson authorityNode (fun path ->
            let changedAuthority = ReferenceAuthority.load path

            let baselineSelected =
                ReferenceDeterministicPrograms.selectAction
                    authority
                    baseline.InitialState
                    baseline.Input.Actions[0]
                    0

            let changedSelected =
                ReferenceDeterministicPrograms.selectAction
                    changedAuthority
                    baseline.InitialState
                    baseline.Input.Actions[0]
                    0

            let original =
                ReferenceDeterministicPrograms.apply
                    authority
                    NoProgramMutation
                    baseline.InitialState
                    baselineSelected

            let changed =
                ReferenceDeterministicPrograms.apply
                    changedAuthority
                    NoProgramMutation
                    baseline.InitialState
                    changedSelected

            original |> should not' (equal changed))

    [<Test>]
    member _.``named deterministic primitive mutants should diverge at their owned transition``() =
        let cases =
            [| "trivial-damage-blk-002-b02", SkipProgramDamagePlacement
               "trivial-chuck-blk-004-b01", SkipProgramCardMovement |]

        for id, mutation in cases do
            match DeterministicProgramRunner.run root authority (obligation id) mutation with
            | DeterministicDiverged divergence ->
                divergence.TraceId |> should equal id
                divergence.Stage |> should equal "transition-state:0"
            | result -> failwith $"The named {mutation} primitive mutant survived: {result}."

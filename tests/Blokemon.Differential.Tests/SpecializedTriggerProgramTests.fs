namespace Blokemon.Differential.Tests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json.Nodes
open Blokemon.ReferenceModel
open FsUnit
open TUnit.Core

type SpecializedTriggerProgramTests() =

    let root = Checkout.repositoryRoot ()
    let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

    let inventory () =
        ReferenceObligations.load authority (Checkout.obligationPath root)

    let obligations () =
        inventory () |> ReferenceSpecializedTriggerPrograms.materialize authority

    let obligation id =
        obligations () |> Array.find (fun value -> value.Input.Id = id)

    let card (state: CanonicalState) id =
        state.Cards |> Array.find (fun value -> value.Id = id)

    let equivalent name result =
        match result with
        | SpecializedTriggerEquivalent evidence -> evidence
        | SpecializedTriggerDiverged divergence ->
            failwith
                $"{name} diverged at {divergence.Stage}. Reference: {divergence.ReferenceFact}. Production: {divergence.ProductionFact}."

    let required description (node: JsonNode | null) =
        match node with
        | null -> failwith $"The test mutation could not find {description}."
        | value -> value

    let temporaryJson (node: JsonNode) action =
        let path = Path.Combine(Path.GetTempPath(), $"blokemon-138-{Guid.NewGuid():N}.json")

        try
            File.WriteAllText(path, node.ToJsonString())
            action path
        finally
            File.Delete path

    static member ObligationIds() : IEnumerable<objnull array> =
        let root = Checkout.repositoryRoot ()
        let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

        ReferenceObligations.load authority (Checkout.obligationPath root)
        |> ReferenceSpecializedTriggerPrograms.reconcile
        |> Seq.map (fun value -> [| value.Id :> objnull |])

    [<Test>]
    member _.``specialised trigger ledger should complete the exact aggregate inventory``() =
        let inventory = inventory ()
        let values = obligations ()

        values.Length
        |> should equal ReferenceSpecializedTriggerPrograms.AcceptedObligationCount

        values
        |> Seq.map (_.Input.InitialState.Route.Value)
        |> Set.ofSeq
        |> should equal ReferenceSpecializedTriggerPrograms.acceptedRoutes

        ReferenceSpecializedTriggerPrograms.acceptedRoutes.Count
        |> should equal ReferenceSpecializedTriggerPrograms.AcceptedRouteCount

        ReferenceSpecializedTriggerPrograms.acceptedObligationIds.Count
        |> should equal ReferenceSpecializedTriggerPrograms.AcceptedObligationCount

        let routeLedgers =
            [| ReferenceDeterministicPrograms.acceptedRoutes
               ReferenceBranchingPrograms.acceptedRoutes
               ReferenceLifecyclePrograms.acceptedRoutes
               ReferenceSpecializedTriggerPrograms.acceptedRoutes |]

        let obligationLedgers =
            [| ReferenceDeterministicPrograms.acceptedObligationIds
               ReferenceBranchingPrograms.acceptedObligationIds
               ReferenceLifecyclePrograms.acceptedObligationIds
               ReferenceSpecializedTriggerPrograms.acceptedObligationIds |]

        for left in 0 .. routeLedgers.Length - 1 do
            for right in left + 1 .. routeLedgers.Length - 1 do
                Set.intersect routeLedgers[left] routeLedgers[right] |> should be Empty

                Set.intersect obligationLedgers[left] obligationLedgers[right]
                |> should be Empty

        let routes = routeLedgers |> Array.reduce Set.union
        let ids = obligationLedgers |> Array.reduce Set.union

        routes.Count |> should equal ReferenceObligations.RouteIdentityCount
        ids.Count |> should equal ReferenceObligations.ObligationCount

        routes
        |> should equal (inventory.RouteIdentities |> Seq.map _.Value |> Set.ofSeq)

        ids |> should equal (inventory.Obligations |> Seq.map _.Id |> Set.ofSeq)

    [<Test>]
    [<MethodDataSource(nameof SpecializedTriggerProgramTests.ObligationIds)>]
    member _.``each accepted specialised trigger obligation should match every public engine boundary``
        (obligationId: string)
        =
        let value = obligation obligationId

        let evidence =
            SpecializedTriggerProgramRunner.run root authority value NoSpecializedTriggerMutation
            |> equivalent obligationId

        evidence.ObligationId |> should equal obligationId

        evidence.StepBound
        |> should be (lessThanOrEqualTo SpecializedTriggerProgramRunner.MaximumStepBound)

        evidence.Transitions.Length |> should equal evidence.StepBound
        evidence.Transitions |> Array.last |> _.Rejection |> should be Empty

    [<Test>]
    member _.``specialised trigger obligation replays should be bounded and byte identical``() =
        for value in obligations () do
            let first =
                SpecializedTriggerProgramRunner.run
                    root
                    authority
                    value
                    NoSpecializedTriggerMutation
                |> equivalent value.Input.Id

            let second =
                SpecializedTriggerProgramRunner.run
                    root
                    authority
                    value
                    NoSpecializedTriggerMutation
                |> equivalent value.Input.Id

            first.StepBound
            |> should be (lessThanOrEqualTo SpecializedTriggerProgramRunner.MaximumStepBound)

            first.ReplayBytes |> should equal second.ReplayBytes

    [<Test>]
    member _.``knockout and Bar Chit attacks should suspend and resume in exact queue order``() =
        let knockout =
            SpecializedTriggerProgramRunner.run
                root
                authority
                (obligation "knockout-trigger-blk-026-fire")
                NoSpecializedTriggerMutation
            |> equivalent "knockout-trigger-blk-026-fire"

        let knockoutAttack = knockout.Transitions[0].State
        knockoutAttack.Phase |> should equal "AwaitingTriggerChoice"
        knockoutAttack.PendingKnockout.Present |> should be True
        knockoutAttack.PendingKnockout.TriggerSource |> should equal "trigger-source"
        knockoutAttack.PendingKnockout.EligibleVim |> should equal [| "movable-vim" |]
        (card knockoutAttack "defender").Zone |> should equal "Oche"

        let knockoutResolved = knockout.Transitions[1].State
        knockoutResolved.PendingKnockout.Present |> should be False
        (card knockoutResolved "defender").Zone |> should equal "EmptiesTray"

        (card knockoutResolved "movable-vim").AttachedTo
        |> should equal "trigger-source"

        let barChit =
            SpecializedTriggerProgramRunner.run
                root
                authority
                (obligation "bar-chit-trigger-blk-113-badge")
                NoSpecializedTriggerMutation
            |> equivalent "bar-chit-trigger-blk-113-badge"

        let barAttack = barChit.Transitions[0].State

        barAttack.PendingBarChits
        |> Array.map _.Card
        |> should equal [| "triggered-prize" |]

        barAttack.Phase |> should equal "AwaitingTriggerChoice"

        let barResolved = barChit.Transitions[1].State
        barResolved.PendingBarChits |> should be Empty
        barResolved.Terminal.IsComplete |> should be True
        barResolved.Terminal.Winner |> should equal "first"
        (card barResolved "triggered-prize").Zone |> should equal "Booth"
        (card barResolved "extra-prize").Zone |> should equal "Mitt"

    [<Test>]
    member _.``reactive triggers should preserve before damage after damage and send home timing``
        ()
        =
        let recovery =
            SpecializedTriggerProgramRunner.run
                root
                authority
                (obligation "doormans-grit-recovers-on-badge")
                NoSpecializedTriggerMutation
            |> equivalent "doormans-grit-recovers-on-badge"
            |> _.Transitions
            |> Array.exactlyOne

        recovery.Events
        |> Array.findIndex (fun event -> event.Kind = "DamagePlaced")
        |> should
            be
            (lessThan (
                recovery.Events |> Array.findIndex (fun event -> event.Kind = "DamageHealed")
            ))

        recovery.Events
        |> Array.exists (fun event -> event.Kind = "BlokeSentHome")
        |> should be False

        let retaliation =
            SpecializedTriggerProgramRunner.run
                root
                authority
                (obligation "one-spark-too-many-retaliates-on-badge")
                NoSpecializedTriggerMutation
            |> equivalent "one-spark-too-many-retaliates-on-badge"
            |> _.Transitions
            |> Array.exactlyOne

        let defenderHome =
            retaliation.Events
            |> Array.findIndex (fun event ->
                event.Kind = "BlokeSentHome" && event.SourceCard = "defender")

        let trigger =
            retaliation.Events
            |> Array.findIndex (fun event ->
                event.Kind = "TriggerResolved" && event.Effect = "BLK-110-T01")

        let attackerHome =
            retaliation.Events
            |> Array.findIndex (fun event ->
                event.Kind = "BlokeSentHome" && event.SourceCard = "attacker")

        defenderHome |> should be (lessThan trigger)
        trigger |> should be (lessThan attackerHome)

    [<Test>]
    member _.``Booth damage should not dispatch Paul Chuckle as attack damage``() =
        let transition =
            SpecializedTriggerProgramRunner.run
                root
                authority
                (obligation "paul-chuckle-booth-damage-does-not-fire")
                NoSpecializedTriggerMutation
            |> equivalent "paul-chuckle-booth-damage-does-not-fire"
            |> _.Transitions
            |> Array.exactlyOne

        transition.Events
        |> Array.exists (fun event -> event.Effect = "BLK-107-T01")
        |> should be False

        transition.Events
        |> Array.filter (fun event -> event.Kind = "DamagePlaced")
        |> Array.exactlyOne
        |> _.DamageKind
        |> should equal "BoothAttack"

    [<Test>]
    member _.``decline blank and full Booth branches should leave no stale trigger resolution``() =
        let cases =
            [| "knockout-trigger-blk-026-decline"
               "bar-chit-trigger-blk-113-decline"
               "bar-chit-trigger-blk-113-blank"
               "bar-chit-trigger-blk-113-full-booth-nonfire" |]

        for id in cases do
            let evidence =
                SpecializedTriggerProgramRunner.run
                    root
                    authority
                    (obligation id)
                    NoSpecializedTriggerMutation
                |> equivalent id

            let finalState = evidence.Transitions |> Array.last |> _.State
            finalState.PendingKnockout.Present |> should be False
            finalState.PendingBarChits |> should be Empty

        let fullBooth =
            SpecializedTriggerProgramRunner.run
                root
                authority
                (obligation "bar-chit-trigger-blk-113-full-booth-nonfire")
                NoSpecializedTriggerMutation
            |> equivalent "bar-chit-trigger-blk-113-full-booth-nonfire"
            |> _.Transitions
            |> Array.exactlyOne

        fullBooth.Events
        |> Array.exists (fun event -> event.Kind = "TriggerQueued")
        |> should be False

    [<Test>]
    member _.``trigger eligibility pending and promotion mutants should diverge``() =
        let cases =
            [| "doormans-grit-recovers-on-badge", SkipSpecializedTriggerDispatch
               "knockout-trigger-blk-026-fire", DropSpecializedPendingResolution
               "paul-chuckle-booth-damage-does-not-fire", TreatBoothDamageAsAttack
               "promotion-makes-three-vim-offer", SkipSpecializedPromotionTrigger |]

        for id, mutation in cases do
            match SpecializedTriggerProgramRunner.run root authority (obligation id) mutation with
            | SpecializedTriggerDiverged divergence ->
                divergence.TraceId |> should equal id

                divergence.Stage.StartsWith("transition-", StringComparison.Ordinal)
                |> should be True
            | result ->
                failwith $"The named {mutation} specialised trigger mutant survived: {result}."

    [<Test>]
    member _.``knockout trigger source order mutant should diverge at the first transition``() =
        let baseline = obligation "knockout-trigger-blk-026-fire"

        let input =
            { baseline.Input with
                InitialState =
                    { baseline.Input.InitialState with
                        Cards =
                            [| { CardId = "a-trigger-source"
                                 Owner = "second"
                                 MechanicalId = "BLK-026"
                                 Zone = ReferenceZone.Booth } |] } }

        let withTwoSources: ReferenceSpecializedTriggerObligation =
            { Input = input
              InitialState = ReferenceSpecializedTriggerPrograms.materializeInput authority input }

        match
            SpecializedTriggerProgramRunner.run
                root
                authority
                withTwoSources
                ReverseSpecializedTriggerSourceOrder
        with
        | SpecializedTriggerDiverged divergence ->
            divergence.TraceId |> should equal "knockout-trigger-blk-026-fire"
            divergence.Stage |> should equal "transition-state:0"
        | result -> failwith $"The specialised trigger source-order mutant survived: {result}."

    [<Test>]
    member _.``structured specialised ledger drift should fail before route credit``() =
        let node =
            JsonNode.Parse(File.ReadAllText(Checkout.obligationPath root))
            |> required "obligation root"

        let assigned =
            node["obligations"]
            |> required "obligations"
            |> _.AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.find (fun value ->
                (value["id"] |> required "obligation id").GetValue<string>() = "knockout-trigger-blk-026-fire")

        assigned["id"] <-
            JsonValue.Create "stale-knockout-trigger-blk-026-fire" |> required "stale id"

        temporaryJson node (fun path ->
            let changed = ReferenceObligations.load authority path

            (fun () -> ReferenceSpecializedTriggerPrograms.reconcile changed |> ignore)
            |> should throw typeof<InvalidOperationException>)

    [<Test>]
    member _.``production specialised materialization should expose structured input drift``() =
        let baseline = obligation "doormans-grit-recovers-on-badge"
        let input = baseline.Input
        let initial = input.InitialState
        let routeDrift = (obligation "paul-chuckle-trigger-fire").Input.InitialState.Route

        let actionDrift =
            { input.Actions[0] with
                SourceCard = "stale-attacker" }

        let cases =
            [| "parameter",
               { input with
                   InitialState =
                       { initial with
                           Parameters =
                               initial.Parameters
                               |> Array.mapi (fun index value ->
                                   if index = 0 then "BLK-110" else value) } },
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
                SpecializedTriggerProgramRunner.runWithProductionInput
                    root
                    authority
                    baseline
                    productionInput
                    NoSpecializedTriggerMutation
            with
            | SpecializedTriggerDiverged divergence ->
                divergence.TraceId |> should equal baseline.Input.Id
                divergence.Stage |> should equal expectedStage
            | result -> failwith $"The production {name} specialised drift survived: {result}."

    [<Test>]
    member _.``raw trigger kind and operand drift should change independent reference dispatch``() =
        let baseline = obligation "paul-chuckle-trigger-fire"

        let mutateTrigger change =
            let node =
                JsonNode.Parse(File.ReadAllText(Checkout.rawAuthorityPath root))
                |> required "authority root"

            let owner =
                node["collectibles"]
                |> required "collectibles"
                |> _.AsArray()
                |> Seq.choose Option.ofObj
                |> Seq.find (fun value ->
                    (value["id"] |> required "collectible id").GetValue<string>() = "BLK-107")

            let trick =
                owner["partyTricks"]
                |> required "party tricks"
                |> _.AsArray()
                |> Seq.choose Option.ofObj
                |> Seq.find (fun value ->
                    (value["mechanicalId"] |> required "trick id").GetValue<string>() = "BLK-107-T01")

            change trick
            node

        let changes =
            [| ("trigger-kind",
                fun (trick: JsonNode) ->
                    trick["trigger"] <- JsonValue.Create "Activated" |> required "mutated trigger")
               ("operand",
                fun (trick: JsonNode) ->
                    let conditional =
                        trick["program"]
                        |> required "program"
                        |> _.AsArray()
                        |> fun values -> values[0] |> required "conditional"

                    let counters =
                        conditional["then"]
                        |> required "then"
                        |> _.AsArray()
                        |> fun values -> values[1] |> required "counter instruction"

                    counters["amount"] <- JsonValue.Create 4 |> required "mutated amount") |]

        for name, change in changes do
            temporaryJson (mutateTrigger change) (fun path ->
                let changedAuthority = ReferenceAuthority.load path

                match
                    SpecializedTriggerProgramRunner.run
                        root
                        changedAuthority
                        baseline
                        NoSpecializedTriggerMutation
                with
                | SpecializedTriggerDiverged divergence ->
                    divergence.TraceId |> should equal baseline.Input.Id

                    divergence.Stage.StartsWith("transition-", StringComparison.Ordinal)
                    |> should be True
                | result -> failwith $"The raw {name} trigger drift survived: {result}.")

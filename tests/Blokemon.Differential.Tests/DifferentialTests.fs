namespace Blokemon.Differential.Tests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.ReferenceModel
open FsUnit
open TUnit.Core

type DifferentialTests() =

    let root = Checkout.repositoryRoot ()
    let normalTrace = FoundationTraces.starter root "growroom" 101973UL
    let mulliganTrace = FoundationTraces.mulligan 3UL
    let aggregate = lazy (AggregateRunner.load root)

    let replay name result =
        match result with
        | Equivalent(value, bytes) -> value, bytes
        | Diverged divergence ->
            failwith
                $"{name} diverged at {divergence.Stage}. Reference: {divergence.ReferenceFact}. Production: {divergence.ProductionFact}."
        | other -> failwith $"{name} returned an unexpected differential outcome {other}."

    let temporaryJson (node: JsonNode) action =
        let path = Path.Combine(Path.GetTempPath(), $"blokemon-133-{Guid.NewGuid():N}.json")

        try
            File.WriteAllText(path, node.ToJsonString())
            action path
        finally
            File.Delete path

    let required description (node: JsonNode | null) =
        match node with
        | null -> failwith $"The test mutation could not find {description}."
        | value -> value

    [<Test>]
    member _.``reference model should have no direct or emitted production dependency``() =
        let hasProductionDependency (references: string array) =
            references
            |> Array.exists (fun name ->
                name.StartsWith("Blokemon.Game", StringComparison.Ordinal)
                || name.StartsWith("Blokemon.App", StringComparison.Ordinal)
                || name.StartsWith("Blokemon.Core", StringComparison.Ordinal))

        DependencyEvidence.directAssemblyReferences ()
        |> hasProductionDependency
        |> should equal false

        DependencyEvidence.transitiveAssemblyReferences ()
        |> hasProductionDependency
        |> should equal false

    [<Test>]
    member _.``runner bootstrap should materialize every reviewed input without program route credit``
        ()
        =
        let bootstrap = DifferentialRunner.bootstrap root

        bootstrap.Authority.Programs.Length |> should equal 298
        bootstrap.Authority.BaseRules.OpcodeInventory.Count |> should equal 49

        bootstrap.ObligationSetups.Length
        |> should equal ReferenceObligations.ObligationCount

        bootstrap.RouteIdentities.Count
        |> should equal ReferenceObligations.RouteIdentityCount

        bootstrap.ProgramRouteAcceptance.Count |> should equal 0

        bootstrap.ObligationSetups
        |> Array.forall (fun input -> input.Actions.Length > 0)
        |> should equal true

    [<Test>]
    member _.``raw instruction schema drift should fail before reference execution``() =
        let authorityNode =
            JsonNode.Parse(File.ReadAllText(Checkout.rawAuthorityPath root))
            |> required "raw authority root"

        let instruction =
            authorityNode["collectibles"]
            |> required "collectibles"
            |> _.AsArray()
            |> fun values -> values[0]
            |> required "first collectible"
            |> fun value -> value["attacks"]
            |> required "attacks"
            |> _.AsArray()
            |> fun values -> values[0]
            |> required "first attack"
            |> fun value -> value["program"]
            |> required "program"
            |> _.AsArray()
            |> fun values -> values[0]
            |> required "first instruction"
            |> _.AsObject()

        instruction["unexpectedSemanticField"] <- JsonValue.Create true

        temporaryJson authorityNode (fun path ->
            (fun () -> ReferenceAuthority.load path |> ignore)
            |> should throw typeof<JsonException>)

    [<Test>]
    member _.``reviewed program identity drift should fail before setup materialization``() =
        let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

        let obligationsNode =
            JsonNode.Parse(File.ReadAllText(Checkout.obligationPath root))
            |> required "structured obligation root"

        let reviewed =
            obligationsNode["obligations"]
            |> required "obligations"
            |> _.AsArray()
            |> fun values -> values[0]
            |> required "first obligation"
            |> fun value -> value["reviewedProgram"]
            |> required "reviewed program"
            |> _.AsObject()

        reviewed["mechanicalId"] <- JsonValue.Create "BLK-999-B01"

        temporaryJson obligationsNode (fun path ->
            (fun () -> ReferenceObligations.load authority path |> ignore)
            |> should throw typeof<JsonException>)

    [<Test>]
    member _.``invalid start should compare the complete ordered rejection``() =
        let request =
            { MatchId = "differential:invalid-start"
              Seed = 7UL
              FirstDeck =
                { Owner = "first"
                  Cards = [| "UNKNOWN" |] }
              SecondDeck =
                { Owner = "second"
                  Cards = normalTrace.SecondCards } }

        match DifferentialRunner.runStartRequest root request with
        | StartEquivalent issues ->
            issues
            |> Array.map _.Code
            |> should equal [| "WrongCardCount"; "UnknownMechanicalCard"; "MissingRegularBloke" |]
        | result -> failwith $"The invalid start comparison returned {result}."

    [<Test>]
    member _.``foundation trace should compare opening two rounds and resignation exactly``() =
        let value, _ =
            DifferentialRunner.runTrace
                root
                normalTrace
                ReferenceDriven
                NoProductionMutation
                NoReferenceMutation
            |> replay normalTrace.Id

        let selected = value.Steps |> Array.map _.SelectedAction.Kind
        selected |> Array.filter ((=) "ChooseOpening") |> Array.length |> should equal 2
        selected |> Array.filter ((=) "EndRound") |> Array.length |> should equal 2
        selected[selected.Length - 1] |> should equal "Resign"

        Array.append value.InitialEvents (value.Steps |> Array.collect _.Events)
        |> Array.filter (fun event -> event.DrawReason = "RequiredRoundDraw")
        |> Array.length
        |> should equal 3

        value.Terminal.IsComplete |> should equal true

        value.Steps
        |> Array.forall (fun step -> step.SelectionOrigin = "ReferenceSelection")
        |> should equal true

        let opening =
            value.Steps
            |> Array.find (fun step -> step.SelectedAction.Kind = "ChooseOpening")

        opening.SelectionChoicePayload.Length |> should equal 1
        opening.SelectionChoicePayload[0] |> should not' (equal "")
        opening.SelectionChoices |> should equal [||]
        opening.SelectionRandomInput |> should equal [| value.InitialState.Random |]

        value.Steps
        |> Array.filter (fun step ->
            step.SelectedAction.Kind = "EndRound" || step.SelectedAction.Kind = "Resign")
        |> Array.forall (fun step ->
            step.SelectionChoicePayload.Length = 0
            && step.SelectionChoices.Length = 0
            && step.SelectionRandomInput.Length = 0)
        |> should equal true

    [<Test>]
    member _.``mulligan trace should compare bonus bar chit and placement phases exactly``() =
        let value, _ =
            DifferentialRunner.runTrace
                root
                mulliganTrace
                ReferenceDriven
                NoProductionMutation
                NoReferenceMutation
            |> replay mulliganTrace.Id

        let mulligans = value.InitialState.Players |> Array.map _.MulliganCount
        mulligans[0] <> mulligans[1] |> should equal true

        value.Steps
        |> Array.exists (fun step -> step.SelectedAction.Kind = "ChooseMulliganBonus")
        |> should equal true

        let bonus =
            value.Steps
            |> Array.find (fun step -> step.SelectedAction.Kind = "ChooseMulliganBonus")

        bonus.SelectionChoicePayload.Length |> should equal 1
        bonus.SelectionChoices |> should equal [||]
        bonus.SelectionRandomInput.Length |> should equal 1

        value.Steps
        |> Array.exists (fun step -> step.SelectedAction.Kind = "ChooseBonusPlacement")
        |> should equal true

        value.Steps
        |> Array.find (fun step -> step.SelectedAction.Kind = "ChooseBonusPlacement")
        |> _.SelectedAction.Payload
        |> should not' (equal "booth=")

        value.Steps
        |> Array.exists (fun step ->
            step.State.Cards
            |> Array.filter (fun card -> card.Zone = "BarChit")
            |> Array.length = 12)
        |> should equal true

    [<Test>]
    member _.``foundation command boundary should compare every owned rejection exactly``() =
        match DifferentialRunner.runRejectionMatrix root normalTrace with
        | RejectionsEquivalent evidence ->
            evidence
            |> should
                equal
                [| "wrong-phase", "WrongPhase"
                   "wrong-match", "WrongMatch"
                   "stale-revision", "StaleRevision"
                   "unknown-actor", "UnknownActor"
                   "duplicate-command", "DuplicateCommand"
                   "match-complete", "MatchComplete" |]
        | result -> failwith $"The rejection comparison returned {result}."

    [<Test>]
    member _.``aggregate production observation should reach and fail the shared transition validator``
        ()
        =
        match AggregateRunner.productionDrivenNegative aggregate.Value with
        | Diverged divergence ->
            divergence.Stage |> should equal "selection-inputs:0"
            divergence.ReferenceFact <> divergence.ProductionFact |> should equal true
            divergence.SelectionEvidence.Length |> should equal 1

            let evidence = divergence.SelectionEvidence[0]
            evidence.Origin |> should equal ProductionDriven
            evidence.ObservedProductionActor |> should equal [| "first" |]
            evidence.ObservedProductionAction.Length |> should equal 1

            let observed = evidence.ObservedProductionAction[0]
            observed.Actor |> should equal "first"
            observed.Kind |> should equal "ChooseOpening"
            observed.Payload |> should equal "oche=C1-008;booth="

            let productionChoice =
                observed.Requirements
                |> Array.collect _.EligibleCards
                |> Array.sort
                |> Array.last

            evidence.AppliedActor |> should equal evidence.ObservedProductionActor[0]

            { evidence.AppliedAction with
                Payload = observed.Payload }
            |> should equal observed

            evidence.AppliedAction.Payload
            |> should equal $"oche=C1-008;booth={productionChoice}"

            evidence.AppliedChoicePayload |> should equal [| productionChoice |]
            evidence.AppliedChoices |> should equal [||]
            evidence.AppliedAction.Choices |> should equal evidence.AppliedChoices

            evidence.ObservedProductionRandom.Length |> should equal 1
            evidence.AppliedRandom |> should equal evidence.ObservedProductionRandom
            evidence.ReferenceRandomAfter |> should equal evidence.AppliedRandom[0]
            evidence.ProductionRandomAfter |> should equal evidence.AppliedRandom[0]

            evidence.LegalSelectionBoundaryReached |> should equal true
            evidence.ReferenceTransitionAttempted |> should equal true
            evidence.ProductionTransitionAttempted |> should equal true

            evidence.ReferenceRevisionAfter
            |> should equal (evidence.AppliedAction.ExpectedRevision + 1L)

            evidence.ProductionRevisionAfter |> should equal evidence.ReferenceRevisionAfter

        | result -> failwith $"Production selection was not rejected: {result}."

    [<Test>]
    member _.``production required opening draw mutant should be attributed and leave authority unchanged``
        ()
        =
        let before = File.ReadAllBytes(Checkout.rawAuthorityPath root)

        match AggregateRunner.productionRequiredOpeningDrawMutant aggregate.Value with
        | Diverged divergence ->
            divergence.TraceId |> should equal "starter:growroom"

            divergence.Stage.StartsWith("transition:", StringComparison.Ordinal)
            |> should equal true
        | result -> failwith $"The production draw mutant survived: {result}."

        File.ReadAllBytes(Checkout.rawAuthorityPath root) |> should equal before

    [<Test>]
    member _.``reference omit resign legal set mutant should be attributed and leave authority unchanged``
        ()
        =
        let before = File.ReadAllBytes(Checkout.rawAuthorityPath root)

        match AggregateRunner.referenceOmitResignLegalSetMutant aggregate.Value with
        | Diverged divergence ->
            divergence.TraceId |> should equal "starter:growroom"

            divergence.Stage.StartsWith("legal-actions:", StringComparison.Ordinal)
            |> should equal true
        | result -> failwith $"The reference legal-set mutant survived: {result}."

        File.ReadAllBytes(Checkout.rawAuthorityPath root) |> should equal before

    [<Test>]
    member _.``two clean foundation runs should produce bounded byte identical replays``() =
        for spec in [| normalTrace; mulliganTrace |] do
            let firstReplay, firstBytes =
                DifferentialRunner.runTrace
                    root
                    spec
                    ReferenceDriven
                    NoProductionMutation
                    NoReferenceMutation
                |> replay spec.Id

            let secondReplay, secondBytes =
                DifferentialRunner.runTrace
                    root
                    spec
                    ReferenceDriven
                    NoProductionMutation
                    NoReferenceMutation
                |> replay spec.Id

            firstReplay.Steps.Length <= firstReplay.StepBound |> should equal true
            firstReplay.ProgramRouteAcceptance |> should equal [||]
            firstBytes |> should equal secondBytes
            firstReplay |> should equal secondReplay

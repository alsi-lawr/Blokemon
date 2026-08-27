namespace Blokemon.Differential.Tests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.ReferenceModel
open FsUnit
open TUnit.Core

type AggregateTests() =

    let root = Checkout.repositoryRoot ()
    let aggregate = lazy (AggregateRunner.load root)

    let equivalent result =
        match result with
        | AggregateEquivalent(replay, bytes) -> replay, bytes
        | AggregateDiverged divergence ->
            failwith
                $"Aggregate obligation {divergence.TraceId} diverged at {divergence.Stage}. Reference: {divergence.ReferenceFact}. Production: {divergence.ProductionFact}."

    let temporaryJson (node: JsonNode) action =
        let path = Path.Combine(Path.GetTempPath(), $"blokemon-118-{Guid.NewGuid():N}.json")

        try
            File.WriteAllText(path, node.ToJsonString())
            action path
        finally
            File.Delete path

    let required description (node: JsonNode | null) =
        match node with
        | null -> failwith $"The aggregate drift probe could not find {description}."
        | value -> value

    let assertUpstreamDrift (changed: JsonNode) =
        (fun () ->
            temporaryJson changed (fun path ->
                UpstreamIdentity.validate path (Checkout.upstreamIdentityPath root)))
        |> should throw typeof<JsonException>

    let assertCorpusDrift context (changed: JsonNode) =
        (fun () ->
            temporaryJson changed (fun path ->
                DifferentialAggregateCorpus.loadFromPath root path context.Reference |> ignore))
        |> should throw typeof<JsonException>

    let doubledCardCounts cards =
        cards
        |> Array.countBy id
        |> Array.map (fun (card, count) -> card, count * 2)
        |> Map.ofArray

    let replayCardCounts (replay: CanonicalReplay) =
        replay.InitialState.Cards |> Array.countBy _.MechanicalId |> Map.ofArray

    let validatedReplayBytes () =
        let replay, bytes = AggregateRunner.run aggregate.Value |> equivalent

        replay.Schema |> should equal CanonicalAggregateReplay.Schema
        replay.ObligationCount |> should equal ReferenceObligations.ObligationCount
        replay.RouteCount |> should equal ReferenceObligations.RouteIdentityCount

        replay.Obligations.Length |> should equal ReferenceObligations.ObligationCount

        replay.Obligations
        |> Array.map _.ObligationId
        |> fun ids -> ids = Array.sort ids && (ids |> Set.ofArray).Count = ids.Length
        |> should equal true

        replay.Obligations
        |> Array.forall (fun obligation ->
            obligation.StepBound <= replay.TransitionCeiling
            && obligation.LegalActions.Length > 0
            && obligation.ProgramKey.Length > 0
            && obligation.Route.Length > 0)
        |> should equal true

        let deckEvidence =
            Array.append
                (replay.StarterDecks
                 |> Array.map (fun deck -> $"starter:{deck.Id}", deck.Seed, deck.Cards))
                (replay.ConstructedDecks
                 |> Array.map (fun deck -> $"constructed:{deck.Id}", deck.Seed, deck.Cards))

        replay.CorpusTraces
        |> Array.map _.TraceId
        |> should equal (deckEvidence |> Array.map (fun (id, _, _) -> id))

        for id, seed, cards in deckEvidence do
            let trace = replay.CorpusTraces |> Array.find (fun value -> value.TraceId = id)
            trace.Seed |> should equal seed
            trace.InitialState.Seed |> should equal seed
            replayCardCounts trace |> should equal (doubledCardCounts cards)
            trace.Steps.Length > 0 |> should equal true
            trace.Steps.Length <= replay.TransitionCeiling |> should equal true
            trace.ProgramRouteAcceptance |> should equal [||]

            trace.Steps
            |> Array.forall (fun step ->
                step.SelectionOrigin = "ReferenceSelection"
                && step.LegalActions.Length > 0
                && step.State.Seed = seed)
            |> should equal true

            Array.append trace.InitialEvents (trace.Steps |> Array.collect _.Events)
            |> Array.isEmpty
            |> should equal false

            trace.Terminal
            |> should equal trace.Steps[trace.Steps.Length - 1].State.Terminal

            trace.Terminal.IsComplete |> should equal true

        bytes

    [<Test>]
    member _.``checked corpus should retain legal decks and exact objective route coverage``() =
        let context = aggregate.Value
        let corpus = context.Corpus

        corpus.StarterDecks
        |> Array.map _.Id
        |> Set.ofArray
        |> should equal (Set [ "growroom"; "brick-lane-heat"; "early-shift" ])

        corpus.ConstructedDecks.Length |> should equal 4

        let canonicalStarters = DifferentialAggregateCorpus.canonicalStarters root corpus

        let canonicalConstructed = DifferentialAggregateCorpus.canonicalConstructed corpus

        canonicalStarters
        |> Array.forall (fun deck -> deck.Cards.Length = 60)
        |> should equal true

        canonicalConstructed
        |> Array.forall (fun deck ->
            deck.Cards.Length = 60
            && deck.ObjectiveLabels.Length > 0
            && deck.Routes.Length = deck.RepresentativeObligationIds.Length)
        |> should equal true

        corpus.TransitionCeiling
        |> should equal CanonicalAggregateReplay.TransitionCeiling

        corpus.ReferenceTieBreaker
        |> should equal CanonicalAggregateReplay.ReferenceTieBreaker

        context.Reference.Obligations.Length
        |> should equal ReferenceObligations.ObligationCount

        context.Reference.RouteIdentities.Count
        |> should equal ReferenceObligations.RouteIdentityCount

        let objectiveLabelDrift =
            JsonNode.Parse(File.ReadAllText(Checkout.corpusPath root))
            |> required "aggregate corpus root"

        objectiveLabelDrift["constructedDecks"]
        |> required "constructedDecks"
        |> _.AsArray()
        |> fun decks -> decks[0]
        |> required "constructedDecks[0]"
        |> fun deck -> deck["coverage"]
        |> required "constructedDecks[0].coverage"
        |> fun coverage -> coverage["outcomes"]
        |> required "constructedDecks[0].coverage.outcomes"
        |> _.AsArray()
        |> fun outcomes -> outcomes[0] <- "unreviewed-objective"

        assertCorpusDrift context objectiveLabelDrift

        let representativeDrift =
            JsonNode.Parse(File.ReadAllText(Checkout.corpusPath root))
            |> required "aggregate corpus root"

        representativeDrift["constructedDecks"]
        |> required "constructedDecks"
        |> _.AsArray()
        |> fun decks -> decks[0]
        |> required "constructedDecks[0]"
        |> fun deck -> deck["coverage"]
        |> required "constructedDecks[0].coverage"
        |> fun coverage -> coverage["representativeObligationIds"]
        |> required "constructedDecks[0].coverage.representativeObligationIds"
        |> _.AsArray()
        |> fun representatives -> representatives[0] <- "shirt-off-badge"

        assertCorpusDrift context representativeDrift

    [<Test>]
    member _.``checked upstream identity projection should fail closed on metadata and authored evidence drift``
        ()
        =
        UpstreamIdentity.validate
            (Checkout.obligationPath root)
            (Checkout.upstreamIdentityPath root)

        let changed =
            JsonNode.Parse(File.ReadAllText(Checkout.obligationPath root))
            |> required "root"

        let rootNode = changed
        let mutations = rootNode["mutations"] |> required "mutations" |> _.AsArray()
        let mutation = required "mutations[0]" mutations[0]
        mutation["operation"] <- "aggregate-drift-probe"
        assertUpstreamDrift changed

        let semanticInputs =
            ReferenceObligations.load
                aggregate.Value.Reference.Authority
                (Checkout.obligationPath root)

        for field in [ "expectedChoices"; "legalActionResult"; "canonicalState"; "orderedEvents" ] do
            let behavioralDrift =
                JsonNode.Parse(File.ReadAllText(Checkout.obligationPath root))
                |> required "root"

            let obligation =
                behavioralDrift["obligations"]
                |> required "obligations"
                |> _.AsArray()
                |> fun obligations -> obligations[0]
                |> required "obligations[0]"

            match field with
            | "legalActionResult" -> obligation[field] <- "aggregate-drift-probe"
            | _ ->
                obligation[field]
                |> required $"obligations[0].{field}"
                |> _.AsArray()
                |> _.Add("aggregate-drift-probe")

            temporaryJson behavioralDrift (fun path ->
                ReferenceObligations.load aggregate.Value.Reference.Authority path
                |> should equal semanticInputs)

            assertUpstreamDrift behavioralDrift

    [<Test>]
    member _.``aggregate should execute every sorted obligation once with byte identical exact replays``
        ()
        =
        let firstBytes = validatedReplayBytes ()
        let secondBytes = validatedReplayBytes ()
        firstBytes |> should equal secondBytes

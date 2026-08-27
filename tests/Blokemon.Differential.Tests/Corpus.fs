namespace Blokemon.Differential.Tests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Blokemon.ReferenceModel

type FoundationTraceSpec =
    { Id: string
      Seed: uint64
      FirstCards: string array
      SecondCards: string array }

type CorpusDeckEntry = { CardId: string; Quantity: int }

type CorpusStarterDeck = { Id: string; Seed: uint64 }

type CorpusCoverageObjective =
    { RuleFamily: string
      Rationale: string
      Routes: string array
      Outcomes: string array
      RepresentativeObligationIds: string array }

type CorpusConstructedDeck =
    { Id: string
      Seed: uint64
      Entries: CorpusDeckEntry array
      Coverage: CorpusCoverageObjective }

type DifferentialAggregateCorpus =
    { Schema: string
      ReplaySchema: string
      TransitionCeiling: int
      ReferenceTieBreaker: string
      StarterDecks: CorpusStarterDeck array
      ConstructedDecks: CorpusConstructedDeck array }

[<RequireQualifiedAccess>]
module Checkout =

    let repositoryRoot () =
        let rec find (directory: DirectoryInfo) =
            if File.Exists(Path.Combine(directory.FullName, "Blokemon.slnx")) then
                directory.FullName
            else
                match directory.Parent |> Option.ofObj with
                | Some parent -> find parent
                | None ->
                    raise (DirectoryNotFoundException("Could not locate the Blokemon checkout."))

        find (DirectoryInfo AppContext.BaseDirectory)

    let rawAuthorityPath root =
        Path.Combine(root, "content", "authorities", "mechanics.json")

    let starterDeckPath root =
        Path.Combine(root, "content", "authorities", "starter-decks.json")

    let obligationPath root =
        Path.Combine(
            root,
            "tests",
            "Blokemon.Game.Tests",
            "Fixtures",
            "conformance-obligations.json"
        )

    let corpusPath root =
        Path.Combine(
            root,
            "tests",
            "Blokemon.Differential.Tests",
            "Fixtures",
            "aggregate-corpus.json"
        )

    let upstreamIdentityPath root =
        Path.Combine(
            root,
            "tests",
            "Blokemon.Differential.Tests",
            "Fixtures",
            "upstream-obligation-identities.json"
        )

[<RequireQualifiedAccess>]
module FoundationTraces =

    let starter root id seed =
        let decks = ReferenceAuthority.loadStarterDecks (Checkout.starterDeckPath root)
        let deck = decks |> Array.find (fun candidate -> candidate.Id = id)
        let cards = ReferenceAuthority.expandStarterDeck deck

        { Id = $"starter:{id}"
          Seed = seed
          FirstCards = cards
          SecondCards = cards }

    let mulligan seed =
        let first = Array.append (Array.create 4 "BLK-001") (Array.create 56 "VIM-BLAZED")
        let second = Array.append (Array.create 4 "BLK-004") (Array.create 56 "VIM-CURRY")

        { Id = "constructed:mulligan-bonus"
          Seed = seed
          FirstCards = first
          SecondCards = second }

[<RequireQualifiedAccess>]
module DifferentialAggregateCorpus =

    [<Literal>]
    let Schema = "blokemon-differential-aggregate-corpus-1"

    let private options =
        let value = JsonSerializerOptions(JsonSerializerDefaults.Web)
        value.PropertyNameCaseInsensitive <- false
        value.RespectRequiredConstructorParameters <- true
        value.UnmappedMemberHandling <- JsonUnmappedMemberHandling.Disallow
        value

    let private fail message = raise (JsonException message)

    let private requireArray context (values: 'value array) =
        if obj.ReferenceEquals(box values, null) then
            fail $"The aggregate corpus {context} is null."

        values

    let private requireText context (value: string) =
        if String.IsNullOrWhiteSpace value then
            fail $"The aggregate corpus {context} is blank."

        value

    let private duplicates values =
        values
        |> Seq.countBy id
        |> Seq.choose (fun (value, count) -> if count = 1 then None else Some value)
        |> Seq.toArray

    let private expand entries =
        entries |> Array.collect (fun entry -> Array.create entry.Quantity entry.CardId)

    type private AcceptedObjectiveLedger =
        { Routes: Set<string>
          ObligationIds: Set<string>
          Labels: Set<string> }

    let private acceptedObjectiveLedger family =
        match family with
        | "deterministic" ->
            { Routes = ReferenceDeterministicPrograms.acceptedRoutes
              ObligationIds = ReferenceDeterministicPrograms.acceptedObligationIds
              Labels =
                Set [ "choice-rejection"; "deterministic-rng"; "exact-state"; "ordered-events" ] }
        | "branching" ->
            { Routes = ReferenceBranchingPrograms.acceptedRoutes
              ObligationIds = ReferenceBranchingPrograms.acceptedObligationIds
              Labels =
                Set
                    [ "badge"
                      "blank"
                      "invalid-choice"
                      "maximum-choice"
                      "minimum-choice"
                      "optional-decline"
                      "predicate-false"
                      "predicate-true" ] }
        | "lifecycle" ->
            { Routes = ReferenceLifecyclePrograms.acceptedRoutes
              ObligationIds = ReferenceLifecyclePrograms.acceptedObligationIds
              Labels =
                Set
                    [ "action-available"
                      "action-unavailable"
                      "continuous-not-register"
                      "continuous-register"
                      "optional-decline"
                      "usage-limit" ] }
        | "specialized-trigger" ->
            { Routes = ReferenceSpecializedTriggerPrograms.acceptedRoutes
              ObligationIds = ReferenceSpecializedTriggerPrograms.acceptedObligationIds
              Labels =
                Set
                    [ "badge"
                      "blank"
                      "decline"
                      "nonfire"
                      "ordered-trigger-queue"
                      "pending-resolution" ] }
        | _ -> fail $"The aggregate corpus names unknown rule family {family}."

    let private validateDeck
        (authority: ReferenceAuthority)
        context
        (entries: CorpusDeckEntry array)
        =
        let entries = requireArray $"{context}.entries" entries

        if entries.Length = 0 then
            fail $"The aggregate corpus {context} has no entries."

        for index in 0 .. entries.Length - 1 do
            let entry = entries[index]
            requireText $"{context}.entries[{index}].cardId" entry.CardId |> ignore

            if entry.Quantity <= 0 then
                fail $"The aggregate corpus {context} has a non-positive quantity."

        let duplicateCards = entries |> Seq.map _.CardId |> duplicates

        if duplicateCards.Length <> 0 then
            fail
                $"The aggregate corpus {context} repeats card entries: {String.Join(',', duplicateCards)}."

        let cards = expand entries
        let rules = authority.BaseRules.Stack

        if cards.Length <> rules.CardCount then
            fail
                $"The aggregate corpus {context} contains {cards.Length} cards instead of {rules.CardCount}."

        for cardId, copies in cards |> Array.countBy id do
            let card =
                authority.Cards.TryFind cardId
                |> Option.defaultWith (fun () ->
                    fail $"The aggregate corpus {context} names unknown card {cardId}.")

            let copyLimit =
                if rules.BasicVimExempt && card.Kind = ReferenceCardKind.Vim then
                    Int32.MaxValue
                else
                    min rules.MechanicalCopyLimit card.StackCopyLimit

            if copies > copyLimit then
                fail
                    $"The aggregate corpus {context} contains {copies} copies of {cardId}; the limit is {copyLimit}."

        if
            rules.RequiresRegularBloke
            && not (
                cards
                |> Array.exists (fun cardId ->
                    authority.Cards[cardId].Rank = ValueSome ReferenceRank.Regular)
            )
        then
            fail $"The aggregate corpus {context} contains no Regular Bloke."

        cards

    let private starterEntries (deck: ReferenceStarterDeck) =
        deck.Entries
        |> Array.map (fun (cardId, quantity) -> { CardId = cardId; Quantity = quantity })

    let loadFromPath root path (aggregate: ReferenceAggregate) =
        let deserialized =
            JsonSerializer.Deserialize<DifferentialAggregateCorpus | null>(
                File.ReadAllText path,
                options
            )

        let corpus =
            match deserialized with
            | null -> fail "The aggregate corpus is empty."
            | value -> value

        if corpus.Schema <> Schema then
            fail $"The aggregate corpus schema {corpus.Schema} is unsupported."

        if corpus.ReplaySchema <> CanonicalAggregateReplay.Schema then
            fail "The aggregate corpus replay schema drifted."

        if corpus.TransitionCeiling <> CanonicalAggregateReplay.TransitionCeiling then
            fail "The aggregate corpus transition ceiling drifted."

        if corpus.ReferenceTieBreaker <> CanonicalAggregateReplay.ReferenceTieBreaker then
            fail "The aggregate corpus reference tie-breaker drifted."

        let starters = requireArray "starterDecks" corpus.StarterDecks
        let constructed = requireArray "constructedDecks" corpus.ConstructedDecks

        if constructed.Length = 0 then
            fail "The aggregate corpus has no finite constructed-deck matrix."

        let duplicateCorpusIds =
            Seq.append (starters |> Seq.map _.Id) (constructed |> Seq.map _.Id)
            |> duplicates

        if duplicateCorpusIds.Length <> 0 then
            fail
                $"The aggregate corpus repeats deck identities: {String.Join(',', duplicateCorpusIds)}."

        let checkedInStarterIds =
            starters
            |> Array.map (fun deck -> requireText "starterDecks[].id" deck.Id)
            |> Set.ofArray

        let starterDecks =
            ReferenceAuthority.loadStarterDecks (Checkout.starterDeckPath root)

        let authorityStarterIds = starterDecks |> Seq.map _.Id |> Set.ofSeq
        let requiredStarterIds = Set [ "growroom"; "brick-lane-heat"; "early-shift" ]

        if
            checkedInStarterIds <> requiredStarterIds
            || checkedInStarterIds <> authorityStarterIds
            || starters.Length <> requiredStarterIds.Count
        then
            fail "The aggregate corpus must name exactly the three current starter decks."

        for starter in starterDecks do
            validateDeck aggregate.Authority $"starterDecks[{starter.Id}]" (starterEntries starter)
            |> ignore

        let obligationById =
            aggregate.Obligations
            |> Seq.map (fun obligation -> (ReferenceAggregate.input obligation).Id, obligation)
            |> Map.ofSeq

        let runnerSlices =
            aggregate.Obligations
            |> Seq.groupBy ReferenceAggregate.runner
            |> Seq.map (fun (runner, obligations) ->
                runner,
                (obligations
                 |> Seq.map (ReferenceAggregate.input >> _.InitialState.Route.Value)
                 |> Set.ofSeq,
                 obligations |> Seq.map (ReferenceAggregate.input >> _.Id) |> Set.ofSeq))
            |> Map.ofSeq

        let mutable coveredRoutes = Set.empty

        for index in 0 .. constructed.Length - 1 do
            let deck = constructed[index]
            let context = $"constructedDecks[{index}]"
            requireText $"{context}.id" deck.Id |> ignore
            validateDeck aggregate.Authority context deck.Entries |> ignore

            let coverage = deck.Coverage

            if isNull (box coverage) then
                fail $"The aggregate corpus {context}.coverage is null."

            let family = requireText $"{context}.coverage.ruleFamily" coverage.RuleFamily
            requireText $"{context}.coverage.rationale" coverage.Rationale |> ignore

            let routes =
                requireArray $"{context}.coverage.routes" coverage.Routes
                |> Array.map (requireText $"{context}.coverage.routes[]")

            let outcomes =
                requireArray $"{context}.coverage.outcomes" coverage.Outcomes
                |> Array.map (requireText $"{context}.coverage.outcomes[]")

            let representatives =
                requireArray
                    $"{context}.coverage.representativeObligationIds"
                    coverage.RepresentativeObligationIds
                |> Array.map (requireText $"{context}.coverage.representativeObligationIds[]")

            if
                routes.Length = 0
                || outcomes.Length = 0
                || representatives.Length <> routes.Length
                || duplicates routes |> Array.isEmpty |> not
                || duplicates outcomes |> Array.isEmpty |> not
                || duplicates representatives |> Array.isEmpty |> not
            then
                fail
                    $"The aggregate corpus {context} coverage objective is incomplete or duplicated."

            let ledger = acceptedObjectiveLedger family

            let materializedRoutes, materializedObligationIds =
                runnerSlices.TryFind family
                |> Option.defaultWith (fun () ->
                    fail $"The aggregate has no materialized {family} stage slice.")

            if
                materializedRoutes <> ledger.Routes
                || materializedObligationIds <> ledger.ObligationIds
            then
                fail
                    $"The aggregate corpus {context} disagrees with the accepted {family} stage ledger."

            let objectiveRoutes = routes |> Set.ofArray

            if objectiveRoutes <> ledger.Routes then
                fail
                    $"The aggregate corpus {context} does not cover the accepted {family} route ledger."

            if outcomes |> Set.ofArray <> ledger.Labels then
                fail
                    $"The aggregate corpus {context} objective labels disagree with the accepted finite corpus ledger."

            if not (Set.intersect coveredRoutes objectiveRoutes).IsEmpty then
                fail $"The aggregate corpus {context} duplicates an objective route."

            coveredRoutes <- Set.union coveredRoutes objectiveRoutes

            let representativeRoutes =
                representatives
                |> Array.map (fun id ->
                    let obligation =
                        obligationById.TryFind id
                        |> Option.defaultWith (fun () ->
                            fail
                                $"The aggregate corpus {context} names unknown representative obligation {id}.")

                    if not (ledger.ObligationIds.Contains id) then
                        fail
                            $"The aggregate corpus {context} representative {id} is absent from the accepted {family} obligation ledger."

                    if ReferenceAggregate.runner obligation <> family then
                        fail
                            $"The aggregate corpus {context} representative {id} belongs to another rule family."

                    (ReferenceAggregate.input obligation).InitialState.Route.Value)
                |> Set.ofArray

            if representativeRoutes <> objectiveRoutes then
                fail
                    $"The aggregate corpus {context} does not name one representative obligation per objective route."

        if coveredRoutes <> aggregate.RouteIdentities then
            fail "The aggregate corpus does not cover the accepted aggregate stage-route ledgers."

        corpus

    let load root aggregate =
        loadFromPath root (Checkout.corpusPath root) aggregate

    let starterTraces root (corpus: DifferentialAggregateCorpus) =
        let decks = ReferenceAuthority.loadStarterDecks (Checkout.starterDeckPath root)
        let deckById = decks |> Seq.map (fun deck -> deck.Id, deck) |> Map.ofSeq

        corpus.StarterDecks
        |> Array.sortBy _.Id
        |> Array.map (fun value ->
            let cards = ReferenceAuthority.expandStarterDeck deckById[value.Id]

            { Id = $"starter:{value.Id}"
              Seed = value.Seed
              FirstCards = cards
              SecondCards = cards })

    let constructedTraces (corpus: DifferentialAggregateCorpus) =
        corpus.ConstructedDecks
        |> Array.sortBy _.Id
        |> Array.map (fun value ->
            let cards = expand value.Entries

            { Id = $"constructed:{value.Id}"
              Seed = value.Seed
              FirstCards = cards
              SecondCards = cards })

    let traces root corpus =
        Array.append (starterTraces root corpus) (constructedTraces corpus)

    let canonicalStarters root (corpus: DifferentialAggregateCorpus) =
        starterTraces root corpus
        |> Array.map (fun trace ->
            { Id = trace.Id.Substring("starter:".Length)
              Seed = trace.Seed
              Cards = trace.FirstCards }
            : CanonicalAggregateStarterDeck)

    let canonicalConstructed (corpus: DifferentialAggregateCorpus) =
        corpus.ConstructedDecks
        |> Array.sortBy _.Id
        |> Array.map (fun value ->
            { Id = value.Id
              Seed = value.Seed
              Cards = expand value.Entries
              RuleFamily = value.Coverage.RuleFamily
              Rationale = value.Coverage.Rationale
              ObjectiveLabels = value.Coverage.Outcomes
              Routes = value.Coverage.Routes
              RepresentativeObligationIds = value.Coverage.RepresentativeObligationIds }
            : CanonicalAggregateConstructedDeck)

namespace Blokemon.Game.Tests

open System
open System.Diagnostics
open System.IO
open Blokemon.Game
open FsUnit
open TUnit.Core
open FuzzHarness

type FuzzHarnessTests() =

    [<Test>]
    [<Arguments(0)>]
    [<Arguments(78)>]
    [<Arguments(156)>]
    member _.``seeded self-play should obey every reached rulebook clause``(seed: int) =
        let result = defaultBout (uint64 seed)

        Console.WriteLine(
            $"BLOKEMON-079 seed={seed}; status={result.Status}; steps={result.Steps}; assertions={result.Assertions}"
        )

        assertNoFindings [ result ]
        result.Assertions |> should be (greaterThan 0)

    [<Test>]
    member _.``default seeded decks should cover every authority content card``() =
        (allPrograms.Length, Clauses.programShapes.Id)
        |> should equal (reconciliationProgramIds.Length, Clauses.programShapes.Id)

        (allContentIds.Length, Clauses.authorityInventory.Id)
        |> should equal (165, Clauses.authorityInventory.Id)

        (reconciliationAuthorityVersion, Clauses.programShapes.Id)
        |> should equal (MatchScenario.Authority.ManifestVersion, Clauses.programShapes.Id)

        (reconciliationProgramIds, Clauses.programShapes.Id)
        |> should equal (allPrograms, Clauses.programShapes.Id)

        (MatchScenario.Authority.BaseRules.BigHitters.BlokeIds.Length, Clauses.bigHitterAward.Id)
        |> should equal (11, Clauses.bigHitterAward.Id)

        (coveredContent DefaultSeeds, Clauses.authorityInventory.Id)
        |> should equal (allContentIds |> Set.ofArray, Clauses.authorityInventory.Id)

    [<Test>]
    member _.``the default sweep should report program coverage and incomplete bouts``() =
        let results, duration = defaultSweepEvidence ()

        let path, observed, neverObserved, incomplete =
            coverageReport
                "BLOKEMON-079-program-coverage.md"
                DefaultSeeds
                DefaultStepCeiling
                duration
                results

        File.Exists path |> should be True
        observed + neverObserved |> should equal allPrograms.Length

        results
        |> Array.filter (fun result -> result.Status = Incomplete)
        |> Array.length
        |> should equal incomplete

        Console.WriteLine $"BLOKEMON-079 coverage report: {path}"
        assertNoFindings results

    [<Test>]
    member _.``reaching the step ceiling should retain assertions and report an incomplete bout``
        ()
        =
        let result = runBout DefaultSeeds[0] 0
        assertNoFindings [ result ]
        result.Status |> should equal Incomplete
        result.StopReason |> should equal StepCeilingReached
        result.Seed |> should equal DefaultSeeds[0]
        result.Assertions |> should be (greaterThan 0)
        result.FinalState.Phase |> should not' (equal MatchPhase.Complete)

    [<Test>]
    member _.``the same seed should reproduce identical supported event bytes and final state``() =
        let first = runBout DefaultSeeds[0] DefaultStepCeiling
        let repeated = runBout DefaultSeeds[0] DefaultStepCeiling
        assertNoFindings [ first; repeated ]

        (canonicalEventBytes repeated.Events, Clauses.deterministicEvents.Id)
        |> should equal (canonicalEventBytes first.Events, Clauses.deterministicEvents.Id)

        (repeated.FinalState, Clauses.deterministicFinalState.Id)
        |> should equal (first.FinalState, Clauses.deterministicFinalState.Id)

    [<Test>]
    member _.``the production persisted-document replay path should reproduce the identical final state``
        ()
        =
        task {
            let! replayed = productionPersistedReplay ()

            (replayed.PersistedCommands > 0, Clauses.persistedReplayState.Id)
            |> should equal (true, Clauses.persistedReplayState.Id)

            (replayed.SecondState, Clauses.persistedReplayState.Id)
            |> should equal (replayed.FirstState, Clauses.persistedReplayState.Id)

            (canonicalEventBytes replayed.SecondEvents, Clauses.persistedReplayEvents.Id)
            |> should
                equal
                (canonicalEventBytes replayed.FirstEvents, Clauses.persistedReplayEvents.Id)
        }

    [<Test>]
    [<Explicit>]
    member _.``the opt-in larger seeded sweep should obey every reached rulebook clause``() =
        let stopwatch = Stopwatch.StartNew()

        let results =
            LargeSweepSeeds |> Array.map (fun seed -> runBout seed LargeSweepStepCeiling)

        stopwatch.Stop()

        let path, observed, neverObserved, _ =
            coverageReport
                "BLOKEMON-079-program-coverage-large.md"
                LargeSweepSeeds
                LargeSweepStepCeiling
                stopwatch.Elapsed
                results

        observed + neverObserved |> should equal allPrograms.Length
        File.Exists path |> should be True
        Console.WriteLine $"BLOKEMON-079 large-sweep coverage report: {path}"
        assertNoFindings results

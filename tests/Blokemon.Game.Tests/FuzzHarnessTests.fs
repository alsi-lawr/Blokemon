namespace Blokemon.Game.Tests

open System
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
        (allPrograms.Length, Clauses.mechanicalAuthority.Id)
        |> should equal (310, Clauses.mechanicalAuthority.Id)

        (allContentIds.Length, Clauses.mechanicalAuthority.Id)
        |> should equal (165, Clauses.mechanicalAuthority.Id)

        (reconciliationAuthorityVersion, Clauses.mechanicalAuthority.Id)
        |> should equal (MatchScenario.Authority.ManifestVersion, Clauses.mechanicalAuthority.Id)

        (reconciliationProgramIds, Clauses.mechanicalAuthority.Id)
        |> should equal (allPrograms, Clauses.mechanicalAuthority.Id)

        (coveredContent DefaultSeeds, Clauses.stack.Id)
        |> should equal (allContentIds |> Set.ofArray, Clauses.stack.Id)

    [<Test>]
    member _.``the default sweep should report program coverage and incomplete bouts``() =
        let results = defaultSweepResults ()

        let path, observed, neverObserved, incomplete =
            coverageReport
                "BLOKEMON-079-program-coverage.md"
                DefaultSeeds
                DefaultStepCeiling
                results

        File.Exists path |> should be True
        observed + neverObserved |> should equal 310

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
    member _.``the same seed and persisted harness commands should reproduce identical events and final state``
        ()
        =
        let first = runBout DefaultSeeds[0] DefaultStepCeiling
        let repeated = runBout DefaultSeeds[0] DefaultStepCeiling
        assertNoFindings [ first; repeated ]

        (canonicalEventBytes repeated.Events, Clauses.deterministicState.Id)
        |> should equal (canonicalEventBytes first.Events, Clauses.deterministicState.Id)

        (repeated.FinalState, Clauses.deterministicState.Id)
        |> should equal (first.FinalState, Clauses.deterministicState.Id)

        let replayedState, replayedEvents = first |> persist |> replayPersisted

        (replayedState, Clauses.deterministicState.Id)
        |> should equal (first.FinalState, Clauses.deterministicState.Id)

        (canonicalEventBytes replayedEvents, Clauses.deterministicState.Id)
        |> should equal (canonicalEventBytes first.Events, Clauses.deterministicState.Id)

    [<Test>]
    member _.``the production persisted-document replay path should reproduce the identical final state``
        ()
        =
        task {
            let! original, replayed = productionPersistedReplay ()

            (replayed, Clauses.deterministicState.Id)
            |> should equal (original, Clauses.deterministicState.Id)
        }

    [<Test>]
    [<Explicit>]
    member _.``the opt-in larger seeded sweep should obey every reached rulebook clause``() =
        let results =
            LargeSweepSeeds |> Array.map (fun seed -> runBout seed LargeSweepStepCeiling)

        let path, observed, neverObserved, _ =
            coverageReport
                "BLOKEMON-079-program-coverage-large.md"
                LargeSweepSeeds
                LargeSweepStepCeiling
                results

        observed + neverObserved |> should equal 310
        File.Exists path |> should be True
        Console.WriteLine $"BLOKEMON-079 large-sweep coverage report: {path}"
        assertNoFindings results

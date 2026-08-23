namespace Blokemon.Game.Tests

open System
open Blokemon.Core.SetDesign
open FsUnit
open TUnit.Core
open ConformanceCensus

type ConformanceCensusTests() =

    [<Test>]
    member _.``the current authority should use every declared opcode and condition``() =
        usedOpcodes |> should equal declaredOpcodes
        usedConditions |> should equal declaredConditions

    [<Test>]
    member _.``every used opcode should have semantic or precise structural evidence``() =
        opcodeEvidence |> Map.keys |> Set.ofSeq |> should equal usedOpcodes

        opcodeEvidence
        |> Map.toSeq
        |> Seq.filter (fun (_, evidence) -> System.String.IsNullOrWhiteSpace evidence)
        |> Seq.toList
        |> should be Empty

        opcodeEvidence
        |> Map.toSeq
        |> Seq.filter (fun (_, evidence) -> evidence.StartsWith("STRUCTURAL EXCLUSION"))
        |> Seq.map fst
        |> Seq.toList
        |> should equal [ BlokemonOpcode.TriggeredPartyTrick ]

    [<Test>]
    member _.``every used condition and non-activated trigger should have exact paired evidence``
        ()
        =
        usedConditions
        |> Seq.filter (conditionEvidence >> System.String.IsNullOrWhiteSpace)
        |> Seq.toList
        |> should be Empty

        nonActivatedTriggers
        |> Seq.filter (triggerEvidence >> System.String.IsNullOrWhiteSpace)
        |> Seq.toList
        |> should be Empty

    [<Test>]
    member _.``every recursive nontrivial program should have an exact composition reference``() =
        recursiveNontrivialPrograms
        |> Seq.map (fun (row, _) -> row.MechanicalId, executionEvidence row)
        |> Seq.filter (snd >> System.String.IsNullOrWhiteSpace)
        |> Seq.toList
        |> should be Empty

    [<Test>]
    member _.``nontrivial programs should match the fixture partition``() =
        let fixture = ConformanceFixture.load ()
        let expectedStructuralPrograms = fixture.Authority.StructuralPrograms

        let actualStructuralPrograms =
            (ConformanceFixture.authorityFacts ()).StructuralPrograms

        let expectedStructuralIds =
            expectedStructuralPrograms |> Seq.map _.MechanicalId |> Set.ofSeq

        let expectedExecutableIds =
            fixture.CompositionHashes |> Seq.map _.MechanicalId |> Set.ofSeq

        let structuralIds =
            structuralNontrivialProgramExclusions
            |> Seq.map (fun (row, _) -> row.MechanicalId)
            |> Set.ofSeq

        let executableIds =
            executableNontrivialPrograms
            |> Seq.map (fun (row, _) -> row.MechanicalId)
            |> Set.ofSeq

        let allNontrivialIds =
            recursiveNontrivialPrograms
            |> Seq.map (fun (row, _) -> row.MechanicalId)
            |> Set.ofSeq

        actualStructuralPrograms |> should equal expectedStructuralPrograms
        structuralIds |> should equal expectedStructuralIds
        executableIds |> should equal expectedExecutableIds

        (structuralNontrivialProgramExclusions.Length,
         executableNontrivialPrograms.Length,
         recursiveNontrivialPrograms.Length)
        |> should
            equal
            (fixture.Authority.Totals.StructuralNontrivialPrograms,
             fixture.Authority.Totals.ExecutableNontrivialPrograms,
             fixture.Authority.Totals.RecursiveNontrivialPrograms)

        Set.intersect structuralIds executableIds |> should be Empty
        Set.union structuralIds executableIds |> should equal allNontrivialIds

        for row, _ in structuralNontrivialProgramExclusions do
            (fun () -> compositionHash row |> ignore)
            |> should throw typeof<InvalidOperationException>

namespace Blokemon.Game.Tests

open System
open System.IO
open Blokemon.Core.SetDesign
open FsUnit
open TUnit.Core
open ConformanceCensus

type ConformanceCensusTests() =

    [<Test>]
    member _.``the corrected authority should have the independently derived census totals``() =
        totals
        |> should
            equal
            { ProgramBearingCards = 165
              Programs = 298
              RecursiveInstructions = 626
              DeclaredAndUsedOpcodes = 49
              DeclaredAndUsedConditions = 31
              NonActivatedTriggers = 26
              RecursiveNontrivialPrograms = 192
              BigHitters = 11 }

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
    member _.``nontrivial programs should partition into 178 executable and 14 exact structural rows``
        ()
        =
        let expectedStructuralIds =
            Set.ofList
                [ "KIT-001-R02"
                  "KIT-002-R02"
                  "KIT-003-R02"
                  "KIT-004-R02"
                  "KIT-005-R02"
                  "KIT-006-R02"
                  "KIT-007-R02"
                  "KIT-008-R02"
                  "KIT-009-R02"
                  "KIT-010-R02"
                  "KIT-011-R02"
                  "KIT-012-R02"
                  "KIT-013-R02"
                  "KIT-014-R02" ]

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

        structuralIds |> should equal expectedStructuralIds
        structuralNontrivialProgramExclusions.Length |> should equal 14
        executableNontrivialPrograms.Length |> should equal 178
        Set.intersect structuralIds executableIds |> should be Empty
        Set.union structuralIds executableIds |> should equal allNontrivialIds

        structuralNontrivialProgramExclusions
        |> Seq.map (fun (row, count) ->
            let flattened = instructions row.Program |> Seq.toArray

            row.Kind,
            count,
            (flattened |> Array.map _.Opcode |> Array.toList),
            (flattened |> Array.collect _.Predicates |> Array.map _.Condition |> Array.toList),
            executionEvidence row)
        |> Seq.toList
        |> should
            equal
            (List.replicate
                14
                (ProgramKind.HouseRule,
                 2,
                 [ BlokemonOpcode.Conditional; BlokemonOpcode.ContinuousPartyTrick ],
                 [ BlokemonCondition.Optional ],
                 declarativeKitStructuralRationale))

        for row, _ in structuralNontrivialProgramExclusions do
            (fun () -> compositionHash row |> ignore)
            |> should throw typeof<InvalidOperationException>

    [<Test>]
    [<Explicit>]
    member _.``the opt-in BLOKEMON-080 evidence generator should reconcile and write the deterministic reports``
        ()
        =
        let censusPath, coveragePath, historical, observed, unobserved =
            ConformanceEvidence.generate ()

        File.Exists censusPath |> should be True
        File.Exists coveragePath |> should be True
        historical.Length |> should equal 222

        historical
        |> Array.countBy _.Disposition
        |> Map.ofArray
        |> should
            equal
            (Map.ofList
                [ ConformanceEvidence.Removed, 13
                  ConformanceEvidence.Changed, 1
                  ConformanceEvidence.Surviving, 208 ])

        observed + unobserved |> should equal totals.Programs

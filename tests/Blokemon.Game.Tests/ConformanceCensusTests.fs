namespace Blokemon.Game.Tests

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

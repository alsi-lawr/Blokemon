namespace Blokemon.Game.Tests

open System
open System.IO
open FsUnit
open TUnit.Core

type ConformanceFixtureTests() =

    [<Test>]
    member _.``the checked-in fixture should match the current authority census and inventories``
        ()
        =
        let expected = ConformanceFixture.load ()
        ConformanceFixture.authorityFacts () |> should equal expected.Authority

    [<Test>]
    [<Explicit>]
    member _.``the fixture generator should write current conformance facts to the caller path``() =
        let output =
            match
                Environment.GetEnvironmentVariable ConformanceFixture.OutputEnvironmentVariable
            with
            | null
            | "" ->
                failwith
                    $"{ConformanceFixture.OutputEnvironmentVariable} must name the fixture output path."
            | path -> path

        let fixture = currentCompositionHashes () |> ConformanceFixture.derive

        ConformanceFixture.write output fixture
        File.Exists output |> should be True
        ConformanceFixture.loadFrom output |> should equal fixture

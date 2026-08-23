namespace Blokemon.Game.Tests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open ConformanceCensus

[<CLIMutable>]
type ConformanceCardIdentities =
    { Collectibles: string array
      Kits: string array
      BasicVim: string array }

[<CLIMutable>]
type ConformanceProgramIdentity =
    { OwnerId: string
      MechanicalId: string
      Kind: string
      Trigger: string
      RecursiveInstructions: int
      Disposition: string }

[<CLIMutable>]
type ConformanceTriggerDisposition =
    { OwnerId: string
      MechanicalId: string
      Trigger: string }

[<CLIMutable>]
type ConformanceStructuralDisposition =
    { OwnerId: string
      MechanicalId: string
      RecursiveInstructions: int
      Opcodes: string array
      Conditions: string array }

[<CLIMutable>]
type ConformanceTotals =
    { Collectibles: int
      Kits: int
      BasicVim: int
      Cards: int
      ProgramBearingCards: int
      Programs: int
      RecursiveInstructions: int
      DeclaredAndUsedOpcodes: int
      DeclaredAndUsedConditions: int
      NonActivatedTriggers: int
      RecursiveNontrivialPrograms: int
      ExecutableNontrivialPrograms: int
      StructuralNontrivialPrograms: int
      BigHitters: int }

[<CLIMutable>]
type ConformanceAuthorityFacts =
    { ManifestVersion: string
      CardIdentities: ConformanceCardIdentities
      ProgramIdentities: ConformanceProgramIdentity array
      UsedOpcodes: string array
      UsedConditions: string array
      NonActivatedTriggers: ConformanceTriggerDisposition array
      StructuralPrograms: ConformanceStructuralDisposition array
      Totals: ConformanceTotals }

[<CLIMutable>]
type ConformanceCompositionHash =
    { MechanicalId: string; Sha256: string }

[<CLIMutable>]
type ConformanceFixtureData =
    { Authority: ConformanceAuthorityFacts
      CompositionHashes: ConformanceCompositionHash array }

module internal ConformanceFixture =

    [<Literal>]
    let OutputEnvironmentVariable = "CONFORMANCE_FIXTURE_OUTPUT"

    let private jsonOptions =
        let options = JsonSerializerOptions(JsonSerializerDefaults.Web)
        options.PropertyNameCaseInsensitive <- false
        options.UnmappedMemberHandling <- JsonUnmappedMemberHandling.Disallow
        options.WriteIndented <- true
        options

    let private programKind =
        function
        | ProgramKind.PartyTrick -> "PartyTrick"
        | ProgramKind.Attack -> "Attack"
        | ProgramKind.HouseRule -> "HouseRule"

    let private triggerName (row: ProgramRow) =
        match row.Trigger with
        | ValueSome trigger -> string trigger
        | ValueNone -> "None"

    let private instructionCount (row: ProgramRow) = instructions row.Program |> Seq.length

    let private programDisposition (row: ProgramRow) count =
        if count <= 1 then
            "Trivial"
        elif declarativeKitStructuralProgramIds.Contains row.MechanicalId then
            "StructuralNontrivial"
        else
            "ExecutableNontrivial"

    let authorityFacts () =
        let cardIdentities =
            { Collectibles = MatchScenario.Authority.Collectibles |> Array.map _.Id |> Array.sort
              Kits = MatchScenario.Authority.Kits |> Array.map _.Id |> Array.sort
              BasicVim = MatchScenario.Authority.BasicVim |> Array.map _.Id |> Array.sort }

        let programIdentities =
            programRows
            |> Array.map (fun row ->
                let count = instructionCount row

                { OwnerId = row.OwnerId
                  MechanicalId = row.MechanicalId
                  Kind = programKind row.Kind
                  Trigger = triggerName row
                  RecursiveInstructions = count
                  Disposition = programDisposition row count })

        let triggerDispositions =
            nonActivatedTriggers
            |> Array.map (fun row ->
                { OwnerId = row.OwnerId
                  MechanicalId = row.MechanicalId
                  Trigger = triggerName row })

        let structuralDispositions =
            structuralNontrivialProgramExclusions
            |> Array.map (fun (row, count) ->
                let flattened = instructions row.Program |> Seq.toArray

                { OwnerId = row.OwnerId
                  MechanicalId = row.MechanicalId
                  RecursiveInstructions = count
                  Opcodes = flattened |> Array.map (fun instruction -> string instruction.Opcode)
                  Conditions =
                    flattened
                    |> Array.collect _.Predicates
                    |> Array.map (fun predicate -> string predicate.Condition) })

        { ManifestVersion = MatchScenario.Authority.ManifestVersion
          CardIdentities = cardIdentities
          ProgramIdentities = programIdentities
          UsedOpcodes = usedOpcodes |> Set.toArray |> Array.sortBy int |> Array.map string
          UsedConditions = usedConditions |> Set.toArray |> Array.sortBy int |> Array.map string
          NonActivatedTriggers = triggerDispositions
          StructuralPrograms = structuralDispositions
          Totals =
            { Collectibles = cardIdentities.Collectibles.Length
              Kits = cardIdentities.Kits.Length
              BasicVim = cardIdentities.BasicVim.Length
              Cards =
                cardIdentities.Collectibles.Length
                + cardIdentities.Kits.Length
                + cardIdentities.BasicVim.Length
              ProgramBearingCards = totals.ProgramBearingCards
              Programs = totals.Programs
              RecursiveInstructions = totals.RecursiveInstructions
              DeclaredAndUsedOpcodes = totals.DeclaredAndUsedOpcodes
              DeclaredAndUsedConditions = totals.DeclaredAndUsedConditions
              NonActivatedTriggers = totals.NonActivatedTriggers
              RecursiveNontrivialPrograms = totals.RecursiveNontrivialPrograms
              ExecutableNontrivialPrograms = executableNontrivialPrograms.Length
              StructuralNontrivialPrograms = structuralNontrivialProgramExclusions.Length
              BigHitters = totals.BigHitters } }

    let derive (compositionHashes: ConformanceCompositionHash array) =
        { Authority = authorityFacts ()
          CompositionHashes = compositionHashes }

    let loadFrom (path: string) : ConformanceFixtureData =
        let fixture =
            JsonSerializer.Deserialize<ConformanceFixtureData>(File.ReadAllText path, jsonOptions)
            |> box

        if isNull fixture then
            raise (JsonException($"Could not deserialize conformance fixture {path}."))

        unbox<ConformanceFixtureData> fixture

    let load () =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "conformance.json")
        |> loadFrom

    let write (path: string) (fixture: ConformanceFixtureData) =
        match Path.GetDirectoryName path with
        | null
        | "" -> ()
        | parent -> Directory.CreateDirectory parent |> ignore

        let json = JsonSerializer.Serialize(fixture, jsonOptions)
        File.WriteAllText(path, json + "\n")

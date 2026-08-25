namespace Blokemon.Game.Tests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open Blokemon.Core.SetDesign
open ConformanceCensus

type ConformanceStructuralDisposition =
    { OwnerId: string
      MechanicalId: string
      RecursiveInstructions: int
      Disposition: string }

type ConformanceCompositionHash =
    { MechanicalId: string; Sha256: string }

type ConformanceFixtureData =
    { Authority: BlokemonRuntimeManifest
      StructuralDisposition: ConformanceStructuralDisposition array
      CompositionHashes: ConformanceCompositionHash array }

module internal ConformanceFixture =

    [<Literal>]
    let OutputEnvironmentVariable = "CONFORMANCE_FIXTURE_OUTPUT"

    let private jsonOptions =
        let options = JsonSerializerOptions(JsonSerializerDefaults.Web)
        options.PropertyNameCaseInsensitive <- false
        options.RespectRequiredConstructorParameters <- true
        options.UnmappedMemberHandling <- JsonUnmappedMemberHandling.Disallow
        options.WriteIndented <- true
        options

    let rec private canonicalize (node: JsonNode) =
        match node with
        | :? JsonObject as source ->
            let canonical = JsonObject()

            source
            |> Seq.sortBy _.Key
            |> Seq.iter (fun property ->
                match property.Value with
                | null -> canonical[property.Key] <- null
                | value -> canonical[property.Key] <- canonicalize value)

            canonical :> JsonNode
        | :? JsonArray as source ->
            let canonical = JsonArray()

            source
            |> Seq.iter (fun item ->
                match item with
                | null -> canonical.Add null
                | value -> canonical.Add(canonicalize value))

            canonical :> JsonNode
        | _ -> node.DeepClone()

    let private parseNode (json: string) =
        match JsonNode.Parse json with
        | null -> raise (JsonException("The conformance projection was empty."))
        | node -> node

    let private disposition (row: ProgramRow) count =
        if count <= 1 then
            "Trivial"
        elif declarativeKitStructuralProgramIds.Contains row.MechanicalId then
            "StructuralNontrivial"
        else
            "ExecutableNontrivial"

    let private structuralDisposition () =
        programRows
        |> Array.map (fun row ->
            let count = instructions row.Program |> Seq.length

            { OwnerId = row.OwnerId
              MechanicalId = row.MechanicalId
              RecursiveInstructions = count
              Disposition = disposition row count })

    let derive (compositionHashes: ConformanceCompositionHash array) =
        { Authority = MatchScenario.Authority
          StructuralDisposition = structuralDisposition ()
          CompositionHashes = compositionHashes }

    let private envelopeNode fixture =
        let envelope = JsonObject()

        envelope["authority"] <- fixture.Authority |> BlokemonSetJson.Serialize |> parseNode

        envelope["structuralDisposition"] <-
            JsonSerializer.Serialize(fixture.StructuralDisposition, jsonOptions)
            |> parseNode

        envelope["compositionHashes"] <-
            JsonSerializer.Serialize(fixture.CompositionHashes, jsonOptions) |> parseNode

        canonicalize envelope

    let render fixture =
        envelopeNode fixture |> _.ToJsonString(jsonOptions) |> (fun json -> json + "\n")

    let private deserializeArray<'element> (root: JsonElement) (property: string) =
        let value =
            JsonSerializer.Deserialize<'element array>(
                root.GetProperty(property).GetRawText(),
                jsonOptions
            )

        match value with
        | null -> raise (JsonException($"The conformance fixture has no {property}."))
        | values -> values

    let parse (json: string) =
        use document = JsonDocument.Parse json

        if document.RootElement.ValueKind <> JsonValueKind.Object then
            raise (JsonException("The conformance fixture must be a JSON object."))

        let root = document.RootElement

        let propertyNames = root.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq

        let requiredPropertyNames =
            set [ "authority"; "compositionHashes"; "structuralDisposition" ]

        if propertyNames <> requiredPropertyNames then
            raise (JsonException("The conformance fixture envelope does not match its schema."))

        let authority =
            root.GetProperty("authority").GetRawText() |> BlokemonSetJson.RuntimeManifest

        { Authority = authority
          StructuralDisposition =
            deserializeArray<ConformanceStructuralDisposition> root "structuralDisposition"
          CompositionHashes = deserializeArray<ConformanceCompositionHash> root "compositionHashes" }

    let loadFrom (path: string) = File.ReadAllText path |> parse

    let fixturePath () =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "conformance.json")

    let load () = fixturePath () |> loadFrom

    let write (path: string) fixture =
        if not (Path.IsPathFullyQualified path) then
            raise (ArgumentException("The conformance output path must be absolute.", nameof path))

        match Path.GetDirectoryName path with
        | null
        | "" -> ()
        | parent -> Directory.CreateDirectory parent |> ignore

        File.WriteAllText(path, render fixture)

    let toNode fixture = envelopeNode fixture

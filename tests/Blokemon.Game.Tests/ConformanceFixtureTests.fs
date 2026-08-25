namespace Blokemon.Game.Tests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open FsUnit
open TUnit.Core

module private ConformanceFixtureTestCases =

    let currentFixture = lazy (currentCompositionHashes () |> ConformanceFixture.derive)

    let private requiredNode (description: string) (node: JsonNode | null) : JsonNode =
        match node with
        | null -> failwith $"The fixture has no {description}."
        | value -> value

    let property (name: string) (node: JsonNode) = node[name] |> requiredNode name

    let item (index: int) (node: JsonNode) =
        node[index] |> requiredNode $"item {index}"

    let objects (node: JsonNode) =
        let rec descendants (current: JsonNode) =
            seq {
                match current with
                | :? JsonObject as value ->
                    yield value

                    for property in value do
                        match property.Value with
                        | null -> ()
                        | child -> yield! descendants (requiredNode property.Key child)
                | :? JsonArray as value ->
                    for index, item in value |> Seq.indexed do
                        match item with
                        | null -> ()
                        | child -> yield! descendants (requiredNode $"item {index}" child)
                | _ -> ()
            }

        descendants node

    let fixtureNode () =
        ConformanceFixture.load () |> ConformanceFixture.toNode

    let currentNode () =
        currentFixture.Value |> ConformanceFixture.toNode

    let shouldNotMatchCurrent (mutate: JsonNode -> unit) =
        let mutated = fixtureNode().DeepClone()
        mutate mutated
        JsonNode.DeepEquals(mutated, currentNode ()) |> should be False

    let integer (propertyName: string) (value: JsonObject) =
        let current = value[propertyName] |> requiredNode propertyName |> _.GetValue<int>()
        value[propertyName] <- JsonValue.Create(current + 1)

    let changedString (propertyName: string) (replacement: string) (value: JsonObject) =
        let current =
            value[propertyName] |> requiredNode propertyName |> _.GetValue<string>()

        if StringComparer.Ordinal.Equals(current, replacement) then
            failwith $"The replacement for {propertyName} did not change its fixture value."

        value[propertyName] <- JsonValue.Create replacement

    let firstAuthorityArray (name: string) (root: JsonNode) =
        root |> property "authority" |> property name |> _.AsArray()

    let firstAuthorityObject (name: string) (root: JsonNode) =
        firstAuthorityArray name root |> item 0 |> _.AsObject()

    let firstObjectWith (propertyName: string) (root: JsonNode) =
        objects root |> Seq.find (fun value -> value.ContainsKey propertyName)

    let nestedInstruction (branch: string) (root: JsonNode) =
        objects root
        |> Seq.find (fun value ->
            match value[branch] with
            | :? JsonArray as instructions -> instructions.Count > 0
            | _ -> false)
        |> fun instruction -> instruction[branch] |> requiredNode branch
        |> item 0
        |> _.AsObject()

    let private decodePointerSegment (segment: string) =
        segment
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal)

    let private pointerSegments (pointer: string) =
        pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map decodePointerSegment

    let private childAtSegment (segment: string) (node: JsonNode) =
        match node with
        | :? JsonObject as value -> value[segment] |> requiredNode segment
        | :? JsonArray as value -> value[int segment] |> requiredNode $"item {segment}"
        | _ -> failwith $"The pointer segment {segment} did not name a container."

    let private changedValue (node: JsonNode) =
        use document = JsonDocument.Parse(node.ToJsonString())

        let valueNode value =
            JsonSerializer.Serialize value |> JsonNode.Parse |> requiredNode "mutated value"

        match document.RootElement.ValueKind with
        | JsonValueKind.String -> valueNode (node.GetValue<string>() + "-mutation")
        | JsonValueKind.Number -> valueNode (node.GetValue<int>() + 1)
        | JsonValueKind.True
        | JsonValueKind.False -> valueNode (not (node.GetValue<bool>()))
        | kind -> failwith $"The selected obligation leaf had unsupported kind {kind}."

    let mutatePointer (pointer: string) (root: JsonNode) =
        let segments = pointerSegments pointer

        let parent =
            segments[.. segments.Length - 2]
            |> Array.fold (fun current segment -> childAtSegment segment current) root

        let leaf = segments[segments.Length - 1]

        match parent with
        | :? JsonObject as value -> value[leaf] <- value[leaf] |> requiredNode leaf |> changedValue
        | :? JsonArray as value ->
            let index = int leaf
            value[index] <- value[index] |> requiredNode $"item {index}" |> changedValue
        | _ -> failwith $"The pointer {pointer} did not name a replaceable leaf."

    let asElement (node: JsonNode) =
        use document = JsonDocument.Parse(node.ToJsonString())
        document.RootElement.Clone()


type ConformanceFixtureTests() =

    [<Test>]
    member _.``the checked-in fixture should match every current canonical projection field``() =
        let expected = ConformanceFixture.load ()
        let actual = ConformanceFixtureTestCases.currentFixture.Value

        actual |> should equal expected

        File.ReadAllText(ConformanceFixture.fixturePath ())
        |> should equal (ConformanceFixture.render actual)

    [<Test>]
    member _.``the checked-in fixture should reconcile every base rule leaf with the obligation ledger``
        ()
        =
        let fixtureRules =
            ConformanceFixtureTestCases.fixtureNode ()
            |> ConformanceFixtureTestCases.property "authority"
            |> ConformanceFixtureTestCases.property "baseRules"
            |> ConformanceFixtureTestCases.asElement

        let rows = BaseRuleObligations.rows ()

        let result =
            BaseRuleObligations.reconcile
                (BaseRuleObligations.leafPointers "" fixtureRules)
                (rows |> Seq.map _.Pointer)

        result.Missing |> should be Empty
        result.Unmatched |> should be Empty
        result.Duplicated |> should be Empty

    [<Test>]
    member _.``a collectible scalar mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            root
            |> ConformanceFixtureTestCases.firstAuthorityObject "collectibles"
            |> ConformanceFixtureTestCases.integer "stayingPower")

    [<Test>]
    member _.``a kit scalar mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            root
            |> ConformanceFixtureTestCases.firstAuthorityObject "kits"
            |> ConformanceFixtureTestCases.integer "stackCopyLimit")

    [<Test>]
    member _.``a basic vim scalar mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            root
            |> ConformanceFixtureTestCases.firstAuthorityObject "basicVim"
            |> ConformanceFixtureTestCases.integer "stackCopyLimit")

    [<Test>]
    member _.``a mechanical display label mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            let mappings =
                ConformanceFixtureTestCases.firstAuthorityArray "approvedMechanicalDisplayMap" root

            let first = mappings |> ConformanceFixtureTestCases.item 0 |> _.AsObject()

            let replacement =
                mappings
                |> ConformanceFixtureTestCases.item 1
                |> ConformanceFixtureTestCases.property "approvedLabel"
                |> _.GetValue<string>()

            first |> ConformanceFixtureTestCases.changedString "approvedLabel" replacement)

    [<Test>]
    member _.``a program scalar mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            root
            |> ConformanceFixtureTestCases.firstObjectWith "printedDamage"
            |> ConformanceFixtureTestCases.integer "printedDamage")

    [<Test>]
    member _.``a program trigger mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            let programs =
                ConformanceFixtureTestCases.objects root
                |> Seq.filter (fun value -> value.ContainsKey "trigger")
                |> Seq.toArray

            let first = programs[0]

            let current =
                first |> ConformanceFixtureTestCases.property "trigger" |> _.GetValue<string>()

            let replacement =
                programs
                |> Array.map (fun value ->
                    value
                    |> ConformanceFixtureTestCases.property "trigger"
                    |> _.GetValue<string>())
                |> Array.find (fun value -> not (StringComparer.Ordinal.Equals(current, value)))

            first |> ConformanceFixtureTestCases.changedString "trigger" replacement)

    [<Test>]
    member _.``an instruction operand mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            root
            |> ConformanceFixtureTestCases.firstObjectWith "opcode"
            |> ConformanceFixtureTestCases.integer "amount")

    [<Test>]
    member _.``a nested then branch mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            root
            |> ConformanceFixtureTestCases.nestedInstruction "then"
            |> ConformanceFixtureTestCases.integer "amount")

    [<Test>]
    member _.``a nested otherwise branch mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            root
            |> ConformanceFixtureTestCases.nestedInstruction "otherwise"
            |> ConformanceFixtureTestCases.integer "amount")

    [<Test>]
    member _.``a base rule ledger leaf mutation should not match the current projection``() =
        let pointer = BaseRuleObligations.rows () |> Array.head |> _.Pointer

        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            root
            |> ConformanceFixtureTestCases.property "authority"
            |> ConformanceFixtureTestCases.property "baseRules"
            |> ConformanceFixtureTestCases.mutatePointer pointer)

    [<Test>]
    member _.``a structural disposition mutation should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            let disposition =
                root
                |> ConformanceFixtureTestCases.property "structuralDisposition"
                |> ConformanceFixtureTestCases.item 0
                |> _.AsObject()

            disposition
            |> ConformanceFixtureTestCases.changedString
                "disposition"
                (disposition
                 |> ConformanceFixtureTestCases.property "disposition"
                 |> _.GetValue<string>()
                 |> fun value -> value + "-mutation"))

    [<Test>]
    member _.``an authority field omission should fail schema loading``() =
        let omitted = ConformanceFixtureTestCases.fixtureNode ()

        omitted
        |> ConformanceFixtureTestCases.firstAuthorityObject "collectibles"
        |> _.Remove("stayingPower")
        |> should be True

        (fun () -> omitted.ToJsonString() |> ConformanceFixture.parse |> ignore)
        |> should throw typeof<JsonException>

    [<Test>]
    member _.``an authority schema addition should fail schema loading``() =
        let added = ConformanceFixtureTestCases.fixtureNode ()

        added
        |> ConformanceFixtureTestCases.firstAuthorityObject "collectibles"
        |> fun card -> card["schemaGrowth"] <- JsonValue.Create 1

        (fun () -> added.ToJsonString() |> ConformanceFixture.parse |> ignore)
        |> should throw typeof<JsonException>

    [<Test>]
    member _.``a new program array item should not match the current projection``() =
        ConformanceFixtureTestCases.shouldNotMatchCurrent (fun root ->
            let program =
                root
                |> ConformanceFixtureTestCases.objects
                |> Seq.pick (fun value ->
                    match value["program"] with
                    | :? JsonArray as instructions when instructions.Count > 0 ->
                        Some instructions
                    | _ -> None)

            program |> ConformanceFixtureTestCases.item 0 |> _.DeepClone() |> program.Add)

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

        let fixture = ConformanceFixtureTestCases.currentFixture.Value

        ConformanceFixture.write output fixture
        File.Exists output |> should be True
        ConformanceFixture.loadFrom output |> should equal fixture
        File.ReadAllText output |> should equal (ConformanceFixture.render fixture)

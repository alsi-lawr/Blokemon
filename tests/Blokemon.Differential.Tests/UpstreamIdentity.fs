namespace Blokemon.Differential.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes

[<RequireQualifiedAccess>]
module UpstreamIdentity =

    let private fail message = raise (JsonException message)

    let private required context (node: JsonNode | null) =
        match node with
        | null -> fail $"The upstream identity projection has no {context}."
        | value -> value

    let private objectNode context expected (node: JsonNode | null) =
        let value = required context node

        let object =
            match value with
            | :? JsonObject as object -> object
            | _ -> fail $"The upstream identity projection {context} is not an object."

        let actual = object |> Seq.map (_.Key) |> Set.ofSeq
        let missing = Set.difference expected actual
        let unknown = Set.difference actual expected

        if not missing.IsEmpty || not unknown.IsEmpty then
            fail
                $"The upstream identity projection {context} schema drifted (missing={String.Join(',', missing)}; unknown={String.Join(',', unknown)})."

        object

    let private arrayNode context (node: JsonNode | null) =
        match required context node with
        | :? JsonArray as array -> array
        | _ -> fail $"The upstream identity projection {context} is not an array."

    let private text context (node: JsonNode | null) =
        let value = required context node

        try
            let result = value.GetValue<string>()

            if String.IsNullOrWhiteSpace result then
                fail $"The upstream identity projection {context} is blank."

            result
        with :? InvalidOperationException ->
            fail $"The upstream identity projection {context} is not text."

    let rec private canonical (node: JsonNode) : JsonNode =
        match node with
        | :? JsonObject as source ->
            let output = JsonObject()

            source
            |> Seq.sortBy (_.Key)
            |> Seq.iter (fun property ->
                output[property.Key] <- property.Value |> required property.Key |> canonical)

            output
        | :? JsonArray as source ->
            let output = JsonArray()

            source
            |> Seq.iter (fun item -> output.Add(item |> required "array item" |> canonical))

            output
        | value -> value.DeepClone()

    let private canonicalProperty context (name: string) (source: JsonObject) =
        source[name] |> required $"{context}.{name}" |> canonical

    let private sortedStrings context (source: JsonObject) (name: string) =
        let output = JsonArray()

        source[name]
        |> arrayNode $"{context}.{name}"
        |> Seq.map (fun item -> text $"{context}.{name}[]" item)
        |> Seq.sort
        |> Seq.iter (fun value -> output.Add value)

        output

    let private obligation index (node: JsonNode | null) =
        let context = $"obligations[{index}]"

        let source =
            objectNode
                context
                (Set
                    [ "id"
                      "programKey"
                      "covers"
                      "reviewedProgram"
                      "initialState"
                      "actions"
                      "randomInput"
                      "expectedChoices"
                      "legalActionResult"
                      "canonicalState"
                      "orderedEvents" ])
                node

        let output = JsonObject()
        output["id"] <- JsonValue.Create(text $"{context}.id" source["id"])
        output["programKey"] <- JsonValue.Create(text $"{context}.programKey" source["programKey"])
        output["reviewedProgram"] <- canonicalProperty context "reviewedProgram" source
        output["covers"] <- sortedStrings context source "covers"
        output["initialState"] <- canonicalProperty context "initialState" source
        output["actions"] <- canonicalProperty context "actions" source
        output["randomInput"] <- canonicalProperty context "randomInput" source

        for name in [ "expectedChoices"; "legalActionResult"; "canonicalState"; "orderedEvents" ] do
            output[name] <- canonicalProperty context name source

        output

    let private structural index (node: JsonNode | null) =
        let context = $"structuralRationales[{index}]"
        let source = objectNode context (Set [ "key"; "programKey"; "rationale" ]) node
        let output = JsonObject()

        for name in [ "key"; "programKey"; "rationale" ] do
            output[name] <- JsonValue.Create(text $"{context}.{name}" source[name])

        output

    let private mutation index (node: JsonNode | null) =
        let context = $"mutations[{index}]"

        let source =
            objectNode
                context
                (Set
                    [ "id"
                      "pointer"
                      "namedObligation"
                      "scenarioObligation"
                      "operation"
                      "expectedFailurePaths" ])
                node

        let output = JsonObject()

        for name in [ "id"; "pointer"; "namedObligation"; "scenarioObligation"; "operation" ] do
            output[name] <- JsonValue.Create(text $"{context}.{name}" source[name])

        output["expectedFailurePaths"] <- sortedStrings context source "expectedFailurePaths"
        output

    let private nonMutableOperand index (node: JsonNode | null) =
        let context = $"nonMutableOperands[{index}]"

        let source =
            objectNode context (Set [ "pointer"; "programKey"; "namedItem"; "rationale" ]) node

        let output = JsonObject()

        for name in [ "pointer"; "programKey"; "namedItem"; "rationale" ] do
            output[name] <- JsonValue.Create(text $"{context}.{name}" source[name])

        output

    let private sortedObjects key (values: JsonObject seq) =
        let output = JsonArray()
        values |> Seq.sortBy key |> Seq.iter (fun value -> output.Add value)
        output

    let project path =
        let root =
            JsonNode.Parse(File.ReadAllText path)
            |> objectNode
                "root"
                (Set
                    [ "schemaVersion"
                      "obligations"
                      "structuralRationales"
                      "mutations"
                      "nonMutableOperands" ])

        let schemaVersion = root["schemaVersion"] |> required "root.schemaVersion"

        if schemaVersion.GetValue<int>() <> 2 then
            fail "The upstream identity projection source schema is unsupported."

        let output = JsonObject()
        output["schema"] <- "blokemon-upstream-obligation-identities-3"
        output["sourceSchemaVersion"] <- 2

        output["obligations"] <-
            root["obligations"]
            |> arrayNode "root.obligations"
            |> Seq.mapi obligation
            |> sortedObjects (fun value -> text "obligation.id" value["id"])

        output["structuralRationales"] <-
            root["structuralRationales"]
            |> arrayNode "root.structuralRationales"
            |> Seq.mapi structural
            |> sortedObjects (fun value -> text "structural.key" value["key"])

        output["mutations"] <-
            root["mutations"]
            |> arrayNode "root.mutations"
            |> Seq.mapi mutation
            |> sortedObjects (fun value -> text "mutation.id" value["id"])

        output["nonMutableOperands"] <-
            root["nonMutableOperands"]
            |> arrayNode "root.nonMutableOperands"
            |> Seq.mapi nonMutableOperand
            |> sortedObjects (fun value ->
                text "nonMutable.pointer" value["pointer"],
                text "nonMutable.programKey" value["programKey"],
                text "nonMutable.namedItem" value["namedItem"])

        output

    let bytes (projection: JsonNode) =
        Encoding.UTF8.GetBytes(projection.ToJsonString() + "\n")

    let validate upstreamPath checkedInPath =
        let current = project upstreamPath |> bytes
        let checkedIn = File.ReadAllBytes checkedInPath

        if current <> checkedIn then
            fail
                "The upstream obligation identity projection drifted; reviewed reconciliation is required."

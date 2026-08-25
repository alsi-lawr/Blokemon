namespace Blokemon.Game.Tests

open System
open System.IO
open System.Text.Json
open Blokemon.Core.SetDesign
open FsUnit
open TUnit.Core

module private BaseRuleObligations =

    type Row =
        { Pointer: string
          Kind: string
          Obligation: string
          Evidence: string
          Mutation: string }

    type Reconciliation =
        { Missing: string array
          Unmatched: string array
          Duplicated: string array }

    let private requiredString (row: JsonElement) (property: string) =
        match row.GetProperty(property).GetString() with
        | null -> failwith $"The obligation row has no {property}."
        | value -> value

    let rows () =
        let path =
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "base-rule-obligations.json")

        use document = JsonDocument.Parse(File.ReadAllText path)

        document.RootElement.GetProperty("obligations").EnumerateArray()
        |> Seq.map (fun row ->
            { Pointer = requiredString row "pointer"
              Kind = requiredString row "kind"
              Obligation = requiredString row "obligation"
              Evidence = requiredString row "evidence"
              Mutation = requiredString row "mutation" })
        |> Seq.toArray

    let private escapeSegment (value: string) =
        value
            .Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal)

    let rec leafPointers (pointer: string) (element: JsonElement) =
        seq {
            match element.ValueKind with
            | JsonValueKind.Object ->
                let properties = element.EnumerateObject() |> Seq.toArray

                if properties.Length = 0 then
                    yield pointer
                else
                    for property in properties do
                        yield!
                            leafPointers $"{pointer}/{escapeSegment property.Name}" property.Value
            | JsonValueKind.Array ->
                let values = element.EnumerateArray() |> Seq.toArray

                if values.Length = 0 then
                    yield pointer
                else
                    for index in 0 .. values.Length - 1 do
                        yield! leafPointers $"{pointer}/{index}" values[index]
            | _ -> yield pointer
        }

    let canonicalBaseRules () =
        use canonical =
            BlokemonSetJson.Serialize MatchScenario.Authority |> JsonDocument.Parse

        canonical.RootElement.GetProperty("baseRules").Clone()

    let reconcile (derived: string seq) (ledger: string seq) =
        let derived = derived |> Seq.toArray
        let ledger = ledger |> Seq.toArray
        let derivedSet = Set.ofArray derived
        let ledgerSet = Set.ofArray ledger

        { Missing = Set.difference derivedSet ledgerSet |> Set.toArray
          Unmatched = Set.difference ledgerSet derivedSet |> Set.toArray
          Duplicated =
            ledger
            |> Array.countBy id
            |> Array.choose (fun (pointer, count) -> if count = 1 then None else Some pointer) }


type BaseRuleObligationTests() =

    [<Test>]
    member _.``the obligation ledger should classify every canonical base rule leaf exactly once``
        ()
        =
        let rows = BaseRuleObligations.rows ()

        rows
        |> Array.forall (fun row ->
            Set.contains row.Kind (set [ "behavior"; "validation"; "reused" ])
            && not (String.IsNullOrWhiteSpace row.Obligation)
            && not (String.IsNullOrWhiteSpace row.Evidence)
            && not (String.IsNullOrWhiteSpace row.Mutation))
        |> should be True

        let result =
            BaseRuleObligations.reconcile
                (BaseRuleObligations.leafPointers "" (BaseRuleObligations.canonicalBaseRules ()))
                (rows |> Seq.map _.Pointer)

        result.Missing |> should be Empty
        result.Unmatched |> should be Empty
        result.Duplicated |> should be Empty

        let behaviorRows = rows |> Array.filter (fun row -> row.Kind = "behavior")

        behaviorRows
        |> Array.forall (fun row ->
            row.Evidence.Contains("BaseRuleBehaviorTests", StringComparison.Ordinal))
        |> should be True

        behaviorRows
        |> Array.map _.Evidence
        |> Array.distinct
        |> Array.length
        |> should equal behaviorRows.Length

    [<Test>]
    member _.``schema additions omissions and duplicate obligations should fail reconciliation``() =
        let rows = BaseRuleObligations.rows ()
        let pointers = rows |> Array.map _.Pointer
        let canonicalBaseRules = BaseRuleObligations.canonicalBaseRules ()
        let canonicalJson = canonicalBaseRules.GetRawText()

        let addedJson =
            canonicalJson.Substring(0, canonicalJson.Length - 1)
            + ",\"newDefault\":0,\"newNull\":null,\"newArray\":[0]}"

        use added = JsonDocument.Parse addedJson

        let addition =
            BaseRuleObligations.reconcile
                (BaseRuleObligations.leafPointers "" added.RootElement)
                pointers

        addition.Missing |> should equal [| "/newArray/0"; "/newDefault"; "/newNull" |]

        let canonical = BaseRuleObligations.leafPointers "" canonicalBaseRules

        let omission = BaseRuleObligations.reconcile canonical (pointers |> Array.skip 1)

        omission.Missing |> should equal [| pointers[0] |]

        let duplicate =
            BaseRuleObligations.reconcile canonical (Array.append pointers [| pointers[0] |])

        duplicate.Duplicated |> should equal [| pointers[0] |]

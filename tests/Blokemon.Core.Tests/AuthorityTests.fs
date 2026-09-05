namespace Blokemon.Core.Tests

open System
open System.IO
open System.Linq
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.Core.PublicContent
open Blokemon.Core.SetDesign
open FsUnit
open TUnit.Core

module private Authorities =

    let read () =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json"))

    let mechanics = lazy (BlokemonSetJson.RuntimeManifest(read ()))

    let document () =
        match JsonNode.Parse(read ()) with
        | null -> failwith "The mechanical authority did not parse as JSON."
        | parsed -> parsed.AsObject()

    let deserialize (document: JsonNode) =
        BlokemonSetJson.RuntimeManifest(document.ToJsonString())

    let validationCodes manifest =
        BlokemonSetValidator.ValidateRuntime(manifest).Issues |> Seq.map _.Code

    let readPublic () =
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Authorities", "public-content.json")
        )

    let publicDocument () =
        match JsonNode.Parse(readPublic ()) with
        | null -> failwith "The public content authority did not parse as JSON."
        | parsed -> parsed.AsObject()

    let node (value: JsonNode | null) =
        match value with
        | null -> failwith "The public content authority lacks a node the test relies on."
        | present -> present

    let firstTrainerEffect (document: JsonNode) =
        let trainers = node document["trainers"]
        let trainer = node trainers.[0]
        let effects = node trainer["effects"]
        node effects.[0]

    let publicCodes (document: JsonNode) =
        let manifest = BlokemonPublicContentJson.Manifest(document.ToJsonString())

        BlokemonPublicContentValidator.ValidateDocument manifest mechanics.Value
        |> _.Issues
        |> Seq.map _.Code
        |> Seq.toList

type AuthorityTests() =

    [<Test>]
    [<Arguments(BlokemonMechanicalType.Grass, BlokemonApprovedMechanicalLabel.Blazed)>]
    [<Arguments(BlokemonMechanicalType.Fire, BlokemonApprovedMechanicalLabel.Curry)>]
    [<Arguments(BlokemonMechanicalType.Water, BlokemonApprovedMechanicalLabel.Sober)>]
    [<Arguments(BlokemonMechanicalType.Lightning, BlokemonApprovedMechanicalLabel.Beer)>]
    [<Arguments(BlokemonMechanicalType.Psychic, BlokemonApprovedMechanicalLabel.Geeked)>]
    [<Arguments(BlokemonMechanicalType.Fighting, BlokemonApprovedMechanicalLabel.Lairy)>]
    [<Arguments(BlokemonMechanicalType.Darkness, BlokemonApprovedMechanicalLabel.Dodgy)>]
    [<Arguments(BlokemonMechanicalType.Colorless, BlokemonApprovedMechanicalLabel.Local)>]
    [<Arguments(BlokemonMechanicalType.Dragon, BlokemonApprovedMechanicalLabel.Legend)>]
    [<Arguments(BlokemonMechanicalType.Metal, BlokemonApprovedMechanicalLabel.Roadie)>]
    member _.``a mechanical type should resolve to its approved player-facing label``
        (mechanicalType: BlokemonMechanicalType, expected: BlokemonApprovedMechanicalLabel)
        =
        BlokemonMechanicalDisplay.ApprovedLabel Authorities.mechanics.Value mechanicalType
        |> should equal expected

    [<Test>]
    member _.``runtime validation should reject a duplicated mechanical display mapping``() =
        let mappings = Array.copy Authorities.mechanics.Value.ApprovedMechanicalDisplayMap
        mappings[mappings.Length - 1] <- mappings[0]

        { Authorities.mechanics.Value with
            ApprovedMechanicalDisplayMap = mappings }
        |> Authorities.validationCodes
        |> should contain "runtime.mechanical-display-map"

    [<Test>]
    member _.``runtime validation should reject an incomplete mechanical display mapping``() =
        { Authorities.mechanics.Value with
            ApprovedMechanicalDisplayMap =
                Authorities.mechanics.Value.ApprovedMechanicalDisplayMap |> Array.skip 1 }
        |> Authorities.validationCodes
        |> should contain "runtime.mechanical-display-map"

    [<Test>]
    member _.``an unmapped mechanical type should fail with the requested type``() =
        let incomplete =
            { Authorities.mechanics.Value with
                ApprovedMechanicalDisplayMap =
                    Authorities.mechanics.Value.ApprovedMechanicalDisplayMap
                    |> Array.filter (fun mapping ->
                        mapping.MechanicalType <> BlokemonMechanicalType.Grass) }

        let failure =
            try
                BlokemonMechanicalDisplay.ApprovedLabel incomplete BlokemonMechanicalType.Grass
                |> ignore

                failwith "The incomplete display map was accepted."
            with :? InvalidDataException as invalid ->
                invalid

        failure.Message |> should contain "Grass"

    // Advanced Rulebook Version 1, pp. 2, 6-7, 20-24: Deck, setup, win, and
    // Sudden Death rules are one immutable load-time contract, not tunable house rules.
    [<Test>]
    member _.``the pinned 1999 shared rule authority should pass strict validation``() =
        let authority = Authorities.mechanics.Value

        BlokemonSetValidator.ValidateRuntime(authority).IsValid |> should be True

        authority.BaseRules.RulesVersion
        |> should equal "wotc-advanced-rulebook-v1-1999-candidate.1"

    // Advanced Rulebook Version 1, pp. 14-15, 20, 23: Basic Energy alone is exempt
    // from the four-card limit; Double Colorless supplies two Colorless Energy units.
    [<Test>]
    member _.``the Energy pool should contain six Basic Energy and exact Double Colorless``() =
        let energy = Authorities.mechanics.Value.BasicVim
        let basics = energy |> Array.filter _.IsBasic

        let doubleColorless =
            energy |> Array.filter (fun card -> not card.IsBasic) |> Array.exactlyOne

        basics.Length |> should equal 6

        basics
        |> Array.forall (fun card -> card.Provides = [| card.MechanicalType |])
        |> should be True

        doubleColorless.Id |> should equal "VIM-DODGY"

        doubleColorless.Provides
        |> should equal [| BlokemonMechanicalType.Colorless; BlokemonMechanicalType.Colorless |]

        doubleColorless.StackCopyLimit |> should equal 4

    [<Test>]
    member _.``runtime authority should reject unknown root fields``() =
        let document = Authorities.document ()
        document["unsupported"] <- JsonValue.Create(true)

        (fun () -> Authorities.deserialize document |> ignore)
        |> should throw typeof<JsonException>

    [<Test>]
    member _.``the published public content should pass validation``() =
        Authorities.publicCodes (Authorities.publicDocument ()) |> should be Empty

    [<Test>]
    [<Arguments("Pokémon")>]
    [<Arguments("Pokemon")>]
    [<Arguments("Pokédex")>]
    [<Arguments("Poké Ball")>]
    member _.``public content should reject the source game's vocabulary in a Trainer's copy``
        (word: string)
        =
        let document = Authorities.publicDocument ()
        let effect = Authorities.firstTrainerEffect document
        effect["effectText"] <- JsonValue.Create($"Choose 1 of your {word} and heal it.")

        Authorities.publicCodes document |> should contain "text.source-vocabulary"

    [<Test>]
    [<Arguments("Discard 1 Fire Energy card attached to this Blokemon.")>]
    [<Arguments("Attach it to 1 of your Water Blokemon.")>]
    [<Arguments("This attack's type is still Colorless.")>]
    member _.``public content should reject the source game's type names in mechanics copy``
        (text: string)
        =
        let document = Authorities.publicDocument ()
        let effect = Authorities.firstTrainerEffect document
        effect["effectText"] <- JsonValue.Create(text)

        Authorities.publicCodes document |> should contain "text.source-type"

    [<Test>]
    member _.``public content should reject the source game's type names in an attack's name``() =
        let document = Authorities.publicDocument ()
        let effect = Authorities.firstTrainerEffect document
        effect["name"] <- JsonValue.Create("Water Gun")

        Authorities.publicCodes document |> should contain "text.source-type"

    [<Test>]
    member _.``public content should keep ordinary English that shares a type name's letters``() =
        let document = Authorities.publicDocument ()
        let effect = Authorities.firstTrainerEffect document

        effect["effectText"] <-
            JsonValue.Create("Pour a glass of water for the Defending Blokemon.")

        Authorities.publicCodes document |> should not' (contain "text.source-type")

    [<Test>]
    member _.``public content should reject the source game's vocabulary in a Trainer's effect name``
        ()
        =
        let document = Authorities.publicDocument ()
        let effect = Authorities.firstTrainerEffect document
        effect["name"] <- JsonValue.Create("Professor's Pokédex")

        Authorities.publicCodes document |> should contain "text.source-vocabulary"

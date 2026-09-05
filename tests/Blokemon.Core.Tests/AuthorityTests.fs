namespace Blokemon.Core.Tests

open System
open System.IO
open System.Linq
open System.Text.Json
open System.Text.Json.Nodes
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

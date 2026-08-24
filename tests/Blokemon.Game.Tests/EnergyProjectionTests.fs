namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open System.IO
open Blokemon.App.Catalogue
open Blokemon.App.MatchViewProjection
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

type EnergyProjectionTests() =

    let catalogue =
        lazy
            (Path.Combine(AppContext.BaseDirectory, "content", "catalogue.json")
             |> File.ReadAllText
             |> BlokemonCatalogue.FromBootstrapJson)

    [<Test>]
    member _.``a rendered attack should use approved labels for every part of its cost``() =
        let state = MatchScenario.BattleState "BLK-001" "BLK-004" [] 31UL

        let attack =
            attacks catalogue.Value state MatchScenario.FirstPlayer Seq.empty
            |> Array.exactlyOne

        attack.EnergyCost |> should equal [| "Blazed"; "Local" |]

    [<Test>]
    member _.``match choice data should keep command values mechanical and project every rendered type``
        ()
        =
        let mechanicalTypes =
            [| BlokemonMechanicalType.Grass
               BlokemonMechanicalType.Fire
               BlokemonMechanicalType.Water
               BlokemonMechanicalType.Lightning
               BlokemonMechanicalType.Psychic
               BlokemonMechanicalType.Fighting
               BlokemonMechanicalType.Darkness
               BlokemonMechanicalType.Colorless
               BlokemonMechanicalType.Dragon
               BlokemonMechanicalType.Metal |]

        let state = MatchScenario.BattleState "BLK-001" "BLK-004" [] 37UL

        let requirement =
            { ChoiceRequirement.create
                  (EffectChoiceId "mechanical-type")
                  ChoiceRequirementKind.MechanicalType
                  MatchScenario.FirstPlayer
                  1
                  1
                  ImmutableArray<_>.Empty
                  (ImmutableArray.CreateRange mechanicalTypes)
                  ImmutableArray<_>.Empty
                  ValueNone with
                EligibleCardTypes =
                    ImmutableArray.Create(
                        { Card = CardInstanceId "attacker"
                          Types =
                            ImmutableArray.Create(
                                BlokemonMechanicalType.Colorless,
                                BlokemonMechanicalType.Lightning
                            ) }
                    ) }

        let view =
            requirementView
                catalogue.Value
                state
                MatchScenario.FirstPlayer
                "Local Player"
                requirement

        view.EligibleMechanicalTypes
        |> Array.map (fun option -> option.Value, option.Label)
        |> should
            equal
            [| "Grass", "Blazed"
               "Fire", "Curry"
               "Water", "Sober"
               "Lightning", "Beer"
               "Psychic", "Geeked"
               "Fighting", "Lairy"
               "Darkness", "Dodgy"
               "Colorless", "Local"
               "Dragon", "Legend"
               "Metal", "Roadie" |]

        view.EligibleCardTypes
        |> Array.exactlyOne
        |> _.MechanicalTypes
        |> should equal [| "Local"; "Beer" |]

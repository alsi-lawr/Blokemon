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

    let read (name: string) =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Authorities", name))

    let mechanics = lazy (BlokemonSetJson.RuntimeManifest(read "mechanics.json"))

    let publicContent =
        lazy (BlokemonPublicContentJson.Manifest(read "public-content.json"))

type AuthorityTests() =

    [<Test>]
    member _.``current authorities should pass owned validation``() =
        BlokemonSetValidator.ValidateRuntime(Authorities.mechanics.Value).IsValid
        |> should be True

        let publicValidation =
            BlokemonPublicContentValidator.ValidateDocument
                Authorities.publicContent.Value
                Authorities.mechanics.Value

        publicValidation.IsValid |> should be True

    [<Test>]
    member _.``eleven card sampling should be deterministic and preserve pack composition``() =
        let manifest = Authorities.mechanics.Value
        let first = BlokemonSeededRandom(0xB10CE188UL)
        let replay = BlokemonSeededRandom(0xB10CE188UL)

        let cards = manifest.Collectibles |> Array.map (fun card -> card.Id, card) |> dict

        for _ in 1..256 do
            let pack = BlokemonPackSampler.SampleEleven manifest first
            let repeated = BlokemonPackSampler.SampleEleven manifest replay

            pack.SequenceEqual(repeated) |> should be True
            pack.Distinct(StringComparer.Ordinal).Count() |> should equal 11

            let bucketCount bucket =
                pack |> Seq.filter (fun id -> cards[id].ProductBucket = bucket) |> Seq.length

            bucketCount BlokemonProductBucket.Rare |> should equal 1
            bucketCount BlokemonProductBucket.Uncommon |> should equal 3
            bucketCount BlokemonProductBucket.Common |> should equal 7

        first.ConsumptionIndex |> should equal replay.ConsumptionIndex

    [<Test>]
    member _.``runtime validation should reject a changed roadie affinity``() =
        let manifest = Authorities.mechanics.Value
        let roadie = manifest.Collectibles.Single(fun card -> card.Id = "BLK-035")

        let changed =
            { manifest with
                Collectibles =
                    [| yield!
                           manifest.Collectibles |> Array.filter (fun card -> card.Id <> "BLK-035")
                       yield { roadie with SoftSpots = Array.empty } |] }

        let result = BlokemonSetValidator.ValidateRuntime(changed)

        result.IsValid |> should be False

        result.Issues
        |> Array.exists (fun issue -> issue.Code = "runtime.roadie-soft-spots")
        |> should be True

    [<Test>]
    member _.``runtime authority should reject unknown fields``() =
        let document =
            match JsonNode.Parse(Authorities.read "mechanics.json") with
            | null -> failwith "The mechanical authority did not parse as JSON."
            | node -> node.AsObject()

        document["unsupported"] <- JsonValue.Create(true)

        (fun () -> BlokemonSetJson.RuntimeManifest(document.ToJsonString()) |> ignore)
        |> should throw typeof<JsonException>

namespace Blokemon.CardGen.Tests

open System
open System.IO
open Blokemon.CardGen.Authority
open Blokemon.CardGen.Domain
open Blokemon.Core.SetDesign
open FsUnit
open TUnit.Core

module private PrintedSet =

    let private content = Path.Combine(AppContext.BaseDirectory, "content")

    let private authority name =
        Path.Combine(content, "authorities", name)

    let mechanics =
        lazy (BlokemonSetJson.RuntimeManifest(File.ReadAllText(authority "mechanics.json")))

    let cards =
        lazy
            (SetAuthority.Load
                (authority "public-content.json")
                (authority "mechanics.json")
                (authority "printing.json")
                (Path.Combine(content, "art")))

    /// The rarity a card's colophon prints.
    let printedRarity (card: Card) =
        card.Regions
        |> Seq.pick (fun region ->
            match region with
            | CardRegion.Colophon(rarity = rarity) -> Some rarity
            | _ -> None)

type TrainerFaceTests() =

    // BLOKEMON-D-044 gives every Trainer a product bucket; the face prints that bucket as its
    // rarity mark, as a Blokemon's face prints its own.
    [<Test>]
    [<Arguments(BlokemonProductBucket.Rare, Rarity.Rare)>]
    [<Arguments(BlokemonProductBucket.Uncommon, Rarity.Uncommon)>]
    [<Arguments(BlokemonProductBucket.Common, Rarity.Common)>]
    member _.``a Trainer face should print its product bucket as its rarity``
        (bucket: BlokemonProductBucket, expected: Rarity)
        =
        let kit =
            PrintedSet.mechanics.Value.Kits
            |> Array.find (fun kit -> kit.ProductBucket = bucket)

        let face =
            PrintedSet.cards.Value.Trainers |> Seq.find (fun card -> card.Id.Value = kit.Id)

        PrintedSet.printedRarity face |> should equal (Some expected)

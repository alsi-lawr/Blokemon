namespace Blokemon.Differential.Tests

open System
open System.IO
open Blokemon.ReferenceModel

type FoundationTraceSpec =
    { Id: string
      Seed: uint64
      FirstCards: string array
      SecondCards: string array }

[<RequireQualifiedAccess>]
module Checkout =

    let repositoryRoot () =
        let rec find (directory: DirectoryInfo) =
            if File.Exists(Path.Combine(directory.FullName, "Blokemon.slnx")) then
                directory.FullName
            else
                match directory.Parent |> Option.ofObj with
                | Some parent -> find parent
                | None ->
                    raise (DirectoryNotFoundException("Could not locate the Blokemon checkout."))

        find (DirectoryInfo AppContext.BaseDirectory)

    let rawAuthorityPath root =
        Path.Combine(root, "content", "authorities", "mechanics.json")

    let starterDeckPath root =
        Path.Combine(root, "content", "authorities", "starter-decks.json")

    let obligationPath root =
        Path.Combine(
            root,
            "tests",
            "Blokemon.Game.Tests",
            "Fixtures",
            "conformance-obligations.json"
        )

[<RequireQualifiedAccess>]
module FoundationTraces =

    let starter root id seed =
        let decks = ReferenceAuthority.loadStarterDecks (Checkout.starterDeckPath root)
        let deck = decks |> Array.find (fun candidate -> candidate.Id = id)
        let cards = ReferenceAuthority.expandStarterDeck deck

        { Id = $"starter:{id}"
          Seed = seed
          FirstCards = cards
          SecondCards = cards }

    let mulligan seed =
        let first = Array.append (Array.create 4 "BLK-001") (Array.create 56 "VIM-BLAZED")
        let second = Array.append (Array.create 4 "BLK-004") (Array.create 56 "VIM-CURRY")

        { Id = "constructed:mulligan-bonus"
          Seed = seed
          FirstCards = first
          SecondCards = second }

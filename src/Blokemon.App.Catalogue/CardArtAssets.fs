namespace Blokemon.App.Catalogue

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Blokemon.App.Contracts

/// The card illustrations a browser needs, ordered by how soon it is likely to need them.
module CardArtAssets =

    // The C# original used a [GeneratedRegex] source generator, which has no F# equivalent; the
    // pattern is identical and the instance is built once.
    let private artReference = Regex("src=\"(/art/[^\"]+)\"")

    let private collect (html: string) (ordered: List<string>) (taken: HashSet<string>) =
        for reference in artReference.Matches html do
            let url = reference.Groups[1].Value

            if taken.Add url then
                ordered.Add url

    let private likeliest (catalogue: BlokemonCatalogue) (state: ApplicationView) =
        seq {
            let known = Dictionary<string, CardView>(StringComparer.Ordinal)

            for card in catalogue.Cards do
                known.Add(card.Id, card)

            for card in state.Cards do
                if card.OwnedQuantity > 0 then
                    yield card

            for deck in state.Decks do
                for entry in deck.Entries do
                    match known.TryGetValue entry.CardId with
                    | true, card -> yield card
                    | _ -> ()

            for starter in state.StarterDecks do
                yield starter.Leader

                for entry in starter.Entries do
                    match known.TryGetValue entry.CardId with
                    | true, card -> yield card
                    | _ -> ()
        }

    /// Orders every illustration the catalogue references for background warming.
    let WarmingOrder
        (catalogue: BlokemonCatalogue, state: ApplicationView | null)
        : IReadOnlyList<string> =
        ArgumentNullException.ThrowIfNull(catalogue, nameof catalogue)

        let ordered = List<string>()
        let taken = HashSet<string>(StringComparer.Ordinal)
        collect catalogue.ReverseFaceHtml ordered taken

        match state with
        | null -> ()
        | loaded ->
            for card in likeliest catalogue loaded do
                collect card.FaceHtml ordered taken

        for card in
            catalogue.Cards
            |> Seq.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id)) do
            collect card.FaceHtml ordered taken

        ordered

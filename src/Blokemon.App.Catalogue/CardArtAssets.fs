namespace Blokemon.App.Catalogue

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Blokemon.App.Contracts

/// One illustration as the warmer asks the browser for it.
type ArtWarming =
    {
        /// What a browser is given when it will not choose between widths.
        Source: string

        /// Every width the same picture is delivered at, written as a srcset, so that the browser
        /// may apply its own rule - the density of the screen included - and fetch the one file
        /// the page will go on to use. Empty for vector artwork, which is one file at every size.
        Candidates: string
    }

/// The card illustrations a browser needs, ordered by how soon it is likely to need them.
module CardArtAssets =

    // The C# original used a [GeneratedRegex] source generator, which has no F# equivalent; the
    // instances are built once.
    //
    // A card names every width its illustration is delivered at and leaves the choice to the
    // browser. Warming does the same rather than picking a width of its own: the right one
    // depends on how dense the player's screen is, which is not known here, and a width the
    // browser then declines to use would cost more than not warming at all.
    let private imageTag = Regex("<img[^>]*>", RegexOptions.Compiled)
    let private source = Regex("src=\"(?<url>/art/[^\"]+)\"", RegexOptions.Compiled)
    let private candidates = Regex("srcset=\"(?<set>[^\"]+)\"", RegexOptions.Compiled)

    let private collect (html: string) (ordered: List<ArtWarming>) (taken: HashSet<string>) =
        for tag in imageTag.Matches html do
            let found = source.Match tag.Value

            if found.Success then
                let url = found.Groups["url"].Value

                if taken.Add url then
                    ordered.Add
                        { Source = url
                          Candidates =
                            match candidates.Match tag.Value with
                            | hit when hit.Success -> hit.Groups["set"].Value
                            | _ -> "" }

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
        : IReadOnlyList<ArtWarming> =
        ArgumentNullException.ThrowIfNull(catalogue, nameof catalogue)

        let ordered = List<ArtWarming>()
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

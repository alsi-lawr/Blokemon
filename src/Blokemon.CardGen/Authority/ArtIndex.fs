namespace Blokemon.CardGen.Authority

open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Text.RegularExpressions
open Blokemon.CardGen.Domain

module private ArtAsset =

    let card = Regex(@"(?<id>(?:BLK|KIT)-\d+)-", RegexOptions.Compiled)

    let symbol = Regex(@"(?<key>energy-[a-z]+|card-back)\.svg$", RegexOptions.Compiled)

/// A lookup of illustrations by card id and symbol key.
type ArtIndex =
    private
        {
            /// The illustration file names by the card they are bound to.
            ByCardId: ImmutableDictionary<string, string>

            /// The illustration file names by the symbol they draw.
            BySymbolKey: ImmutableDictionary<string, string>
        }

    /// The illustration bound to a card.
    member this.For(cardId: string, altText: string) =
        match this.ByCardId.TryGetValue cardId with
        | true, file -> { FileName = file; AltText = altText }
        | _ -> raise (InvalidDataException $"No illustration for {cardId}")

    /// The illustration bound to a symbol.
    member this.ForSymbol(symbolKey: string, altText: string) =
        match this.BySymbolKey.TryGetValue symbolKey with
        | true, file -> { FileName = file; AltText = altText }
        | _ -> raise (InvalidDataException $"No illustration for symbol {symbolKey}")

    /// Indexes the illustrations in a directory.
    static member Scan(directory: string) =
        let files =
            Directory.EnumerateFiles(directory, "*.svg")
            |> Seq.choose (fun path -> Path.GetFileName path |> Option.ofObj)
            |> List.ofSeq

        let index (pattern: Regex) (group: string) =
            files
            |> Seq.map (fun file -> file, pattern.Match file)
            |> Seq.filter (fun (_, hit) -> hit.Success)
            |> Seq.map (fun (file, hit) -> KeyValuePair(hit.Groups[group].Value, file))
            |> ImmutableDictionary.CreateRange

        { ByCardId = index ArtAsset.card "id"
          BySymbolKey = index ArtAsset.symbol "key" }

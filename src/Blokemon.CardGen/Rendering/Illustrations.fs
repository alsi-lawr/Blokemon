namespace Blokemon.CardGen.Rendering

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open Blokemon.CardGen.Domain

/// The part an illustration plays on a card face.
[<RequireQualifiedAccess>]
type internal IllustrationRole =
    /// The card's own illustration.
    | Primary

    /// The thumbnail in the evolution burst.
    | PreviousStage

/// A trusted source of rendered card illustrations.
[<RequireQualifiedAccess>]
type IllustrationRendering =
    private
    /// Illustrations that travel inside the rendered card, by file name.
    | Inline of encoded: ImmutableDictionary<string, string>

    /// The illustration names the same-origin art directory may be asked for.
    | SameOrigin of known: ImmutableHashSet<string>

/// A trusted source of rendered card illustrations.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module IllustrationRendering =

    let private fileNames (directory: string) =
        Directory.EnumerateFiles(directory, "*.svg")
        |> Seq.choose (fun path -> Path.GetFileName path |> Option.ofObj)

    let private attribute role =
        match role with
        | IllustrationRole.Primary -> ""
        | IllustrationRole.PreviousStage -> " class=\"previous-art\""

    let private esc (value: string) =
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal)

    /// Loads illustrations that travel inside the rendered card.
    let embedded (directory: string) =
        Directory.EnumerateFiles(directory, "*.svg")
        |> Seq.map (fun path ->
            KeyValuePair(
                Path.GetFileName path |> nonNull,
                Convert.ToBase64String(File.ReadAllBytes path)
            ))
        |> fun encoded -> ImmutableDictionary.CreateRange(StringComparer.Ordinal, encoded)
        |> IllustrationRendering.Inline

    /// Loads illustrations referenced from the same-origin art directory.
    let referenced (directory: string) =
        fileNames directory
        |> fun known -> ImmutableHashSet.CreateRange(StringComparer.Ordinal, known)
        |> IllustrationRendering.SameOrigin

    let internal image (artwork: Artwork) role rendering =
        match rendering with
        | IllustrationRendering.Inline encoded ->
            match encoded.TryGetValue artwork.FileName with
            | true, data ->
                $"""<img{attribute role} src="data:image/svg+xml;base64,{data}" alt="{esc artwork.AltText}"/>"""
            | _ -> raise (InvalidDataException $"No illustration for {artwork.FileName}")
        | IllustrationRendering.SameOrigin known ->
            if known.Contains artwork.FileName then
                $"""<img{attribute role} src="/art/{esc artwork.FileName}" alt="{esc artwork.AltText}"/>"""
            else
                raise (InvalidDataException $"No illustration for {artwork.FileName}")

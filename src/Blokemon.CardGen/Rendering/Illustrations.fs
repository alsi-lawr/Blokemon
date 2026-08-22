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

/// One illustration as a browser receives it.
type Delivered =
    {
        /// The file the served art directory holds it under.
        FileName: string

        /// A base64 WebP a couple of hundred bytes wide, carried inside the card so that the space
        /// the illustration will fill is never an empty rectangle while it is on the way. Vector
        /// artwork arrives whole and has nothing to stand in for it.
        Placeholder: string option
    }

/// A trusted source of rendered card illustrations.
[<RequireQualifiedAccess>]
type IllustrationRendering =
    private
    /// Illustrations that travel inside the rendered card, by file name.
    | Inline of encoded: ImmutableDictionary<string, string>

    /// What the same-origin art directory may be asked for, by the stem of the approved
    /// illustration it was derived from.
    | SameOrigin of delivered: ImmutableDictionary<string, Delivered>

/// A trusted source of rendered card illustrations.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module IllustrationRendering =

    /// What marks a file in the delivered directory as a placeholder rather than an illustration.
    let private placeholderSuffix = ".lqip.webp"

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
    ///
    /// The directory this reads is the delivered one, not the approved one: what a browser is sent
    /// is the same picture re-encoded, and the approved artwork it was derived from stays where it
    /// is as the source of record. A card is still bound to its approved file name, so the two are
    /// matched by the stem they share.
    let referenced (directory: string) =
        let files =
            Directory.EnumerateFiles directory
            |> Seq.choose (fun path -> Path.GetFileName path |> Option.ofObj)
            |> List.ofSeq

        let isPlaceholder (file: string) =
            file.EndsWith(placeholderSuffix, StringComparison.Ordinal)

        let placeholders =
            files
            |> Seq.filter isPlaceholder
            |> Seq.map (fun file ->
                file[.. file.Length - placeholderSuffix.Length - 1],
                Convert.ToBase64String(File.ReadAllBytes(Path.Combine(directory, file))))
            |> readOnlyDict

        files
        |> Seq.filter (isPlaceholder >> not)
        |> Seq.map (fun file ->
            let stem = Path.GetFileNameWithoutExtension file |> nonNull

            KeyValuePair(
                stem,
                { FileName = file
                  Placeholder =
                    match placeholders.TryGetValue stem with
                    | true, encoded -> Some encoded
                    | _ -> None }
            ))
        |> fun delivered -> ImmutableDictionary.CreateRange(StringComparer.Ordinal, delivered)
        |> IllustrationRendering.SameOrigin

    /// The placeholder rides on the card's own illustration and nowhere else. The evolution
    /// thumbnail is drawn over a gradient the card design puts behind it, and a background of its
    /// own would take that gradient off the card.
    let private standIn role (delivered: Delivered) =
        match role, delivered.Placeholder with
        | IllustrationRole.Primary, Some encoded ->
            sprintf " style=\"background-image:url(data:image/webp;base64,%s)\"" encoded
        | _ -> ""

    let internal image (artwork: Artwork) role rendering =
        match rendering with
        | IllustrationRendering.Inline encoded ->
            match encoded.TryGetValue artwork.FileName with
            | true, data ->
                $"""<img{attribute role} src="data:image/svg+xml;base64,{data}" alt="{esc artwork.AltText}"/>"""
            | _ -> raise (InvalidDataException $"No illustration for {artwork.FileName}")
        | IllustrationRendering.SameOrigin delivered ->
            let stem = Path.GetFileNameWithoutExtension artwork.FileName |> nonNull

            match delivered.TryGetValue stem with
            | true, art ->
                $"""<img{attribute role}{standIn role art} src="/art/{esc art.FileName}" alt="{esc artwork.AltText}"/>"""
            | _ -> raise (InvalidDataException $"No illustration for {artwork.FileName}")

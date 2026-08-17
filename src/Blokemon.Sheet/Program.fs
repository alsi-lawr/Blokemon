module Blokemon.Sheet.Program

open System
open System.IO
open System.Text
open Blokemon.CardGen.Authority
open Blokemon.CardGen.Domain
open Blokemon.CardGen.Rendering
open Blokemon.PackGen.Catalogue
open Blokemon.PackGen.Domain
open Blokemon.PackGen.Rendering

let private faces (fonts: string) =
    [ "GilliusADF-Regular.otf", "Gillius ADF", 400, "normal"
      "GilliusADF-Italic.otf", "Gillius ADF", 400, "italic"
      "GilliusADF-Bold.otf", "Gillius ADF", 700, "normal"
      "GilliusADF-BoldItalic.otf", "Gillius ADF", 700, "italic"
      "Jost-700-Bold.otf", "Jost", 700, "normal"
      "Jost-900-Black.otf", "Jost", 900, "normal" ]
    |> List.map (fun (file, family, weight, style) ->
        let data = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(fonts, file)))

        $"@font-face{{font-family:'{family}';font-style:{style};font-weight:{weight};font-display:block;src:url(data:font/otf;base64,{data}) format('opentype')}}")
    |> String.concat ""

// The page supplies what every object shares, which is what the emitted objects assume. Written
// as logical lines because an F# template would carry its own indentation into the page.
let private shell fonts (stylesheet: string) =
    let page =
        [ """<!doctype html><meta charset="utf-8"><title>Blokemon sheet</title>"""
          "<style>FACES"
          "STYLESHEET"
          "body{margin:0;background:#d8d2c6;font:15px system-ui,sans-serif;color:#1b1f1a}"
          "h2{margin:38px 18px 12px;font-size:20px;letter-spacing:.02em}"
          ".row{display:flex;flex-wrap:wrap;gap:14px;padding:0 18px;align-items:flex-end}"
          ".slot svg{display:block;width:230px;height:auto}"
          """.slot svg[width="24"]{width:64px;background:#faf7ef;padding:8px;border-radius:8px}"""
          "</style>" ]
        |> String.concat "\n"

    page
        .Replace("FACES", faces fonts, StringComparison.Ordinal)
        .Replace("STYLESHEET", stylesheet, StringComparison.Ordinal)

let private section (page: StringBuilder) (title: string) (objects: string seq) =
    let slots = objects |> Seq.map (fun svg -> $"<div class=\"slot\">{svg}</div>")

    page
        .Append($"<h2>{title}</h2><div class=\"row\">")
        .AppendJoin(String.Empty, slots)
        .Append("</div>")
    |> ignore

let private draw (contentDirectory: string) (output: string) =
    let content = Path.GetFullPath contentDirectory
    let authorities = Path.Combine(content, "authorities")
    let art = Path.Combine(content, "art")

    let set =
        SetAuthority.Load
            (Path.Combine(authorities, "public-content.json"))
            (Path.Combine(authorities, "mechanics.json"))
            (Path.Combine(authorities, "printing.json"))
            art

    let cards = CardDocument.Load art
    let page = StringBuilder()

    page.Append(shell (Path.Combine(content, "fonts")) cards.Stylesheet) |> ignore

    section page "Collectibles" (set.Blokemon |> Seq.map (fun card -> cards.Build card))
    section page "Support" (set.Supports |> Seq.map (fun card -> cards.Build card))
    section page "Basic Energy" (set.Energy |> Seq.map (fun card -> cards.Build card))
    section page "Reverse" [ cards.Build set.Reverse ]
    section page "Type glyphs" (Enum.GetValues<BlokemonType>() |> Seq.map TypeGlyphs.glyph)

    for stock in Enum.GetValues<PackStock>() do
        let profile = PackProfile.Blokemon stock

        section
            page
            $"Packaging &middot; {stock}"
            (PackCatalogue.All |> Seq.map (fun pack -> PackArt.Draw pack profile))

    File.WriteAllText(output, page.ToString())
    let size = FileInfo(output).Length / 1024L
    Console.WriteLine $"""{output} ({size.ToString("N0")} KB)"""
    0

[<EntryPoint>]
let main args =
    match args with
    | [| content; output |] -> draw content output
    | _ ->
        Console.Error.WriteLine "usage: Blokemon.Sheet <content-directory> <output.html>"
        1

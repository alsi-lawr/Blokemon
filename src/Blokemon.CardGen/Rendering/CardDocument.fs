namespace Blokemon.CardGen.Rendering

open System
open System.IO
open Blokemon.CardGen.Domain

module private CardDelivery =

    [<Literal>]
    let Xhtml = "http://www.w3.org/1999/xhtml"

    let design () =
        Path.Combine(AppContext.BaseDirectory, "Content", "blokemon-card.css")

    let esc (value: string) =
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)

    let label (card: Card) =
        let vitality =
            card.Regions
            |> Seq.tryPick (function
                | CardRegion.Vitality(points = points; printedType = printedType) ->
                    Some(points, printedType)
                | _ -> None)

        match vitality with
        | Some(Some points, printedType) ->
            $"{card.DisplayName}, {points} HP, {printedType} Blokemon"
        | _ -> card.DisplayName

/// The standalone document one card is delivered as.
type CardDocument =
    private
        {
            /// The stylesheet every card this printer builds is printed under.
            Design: string

            /// The illustrations the printer renders.
            Art: IllustrationRendering
        }

    /// The stylesheet every card this printer builds is printed under.
    member this.Stylesheet = this.Design

    /// Prints the complete reusable card object without an outer SVG.
    member this.BuildMarkup(card: Card) =
        let sprite = TypeGlyphs.sprite ()
        let face = CardRenderer.render card this.Art

        $"""<div xmlns="{CardDelivery.Xhtml}" class="blokemon-card-scale">{sprite}{face}</div>"""

    /// Prints a card as its own document.
    member this.Build(card: Card) =
        // The face is the approved card markup under the approved stylesheet, carried inside an
        // SVG viewport. The design travels with the card because the design is the card; the
        // typefaces do not, because the page a card is embedded into already provides them.
        // The stylesheet is emitted bare rather than in a CDATA section: it holds no ampersand
        // or angle bracket, and CDATA would become a bogus comment if a card were dropped into
        // an HTML page rather than parsed as XML.
        let label = CardDelivery.esc (CardDelivery.label card)
        let id = CardDelivery.esc card.Id.Value

        // Written as logical lines: an indented F# template would carry its own indentation into
        // the delivered document.
        [ $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 750 1050" width="750" height="1050" role="img" aria-label="{label}" data-card-id="{id}" data-generated-by="Blokemon.CardGen">"""
          $"<title>{label}</title>"
          """<foreignObject x="0" y="0" width="750" height="1050">"""
          this.BuildMarkup card
          "</foreignObject>"
          "</svg>"
          "" ]
        |> String.concat "\n"

    /// Assembles the printer for a content directory.
    static member Load(content: string) =
        { Design = File.ReadAllText(CardDelivery.design ())
          Art = IllustrationRendering.embedded content }

    /// Assembles an inline-HTML printer for a content directory.
    static member LoadReferenced(content: string) =
        { Design = File.ReadAllText(CardDelivery.design ())
          Art = IllustrationRendering.referenced content }

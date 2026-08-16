namespace Blokemon.PackGen.Rendering

open System
open System.Text.RegularExpressions
open Blokemon.PackGen.Domain

/// One printed object, ready to be delivered as a standalone document.
type Drawing =
    {
        /// The left edge of the drawn extent.
        X: float

        /// The top edge of the drawn extent.
        Y: float

        /// The drawn width.
        Width: float

        /// The drawn height.
        Height: float

        /// The accessible name of the object.
        Title: string

        /// The definitions the artwork refers to.
        Defs: string

        /// The artwork.
        Art: string

        /// The stylesheet the artwork needs, empty when it needs none.
        Style: string

        /// The token every identity in this document is qualified by.
        Scope: string
    }

/// The standalone document one printed object is delivered as.
module Document =

    let private identity = Regex(@"id=""([\w-]+)""", RegexOptions.Compiled)

    let private reference = Regex(@"url\(#([\w-]+)\)", RegexOptions.Compiled)

    // Identities are global to the page a fragment is embedded in, so each document carries its
    // own token on every definition, reference, class and animation it declares.
    let private qualify (scope: string) (document: string) =
        let identified =
            identity.Replace(document, (fun hit -> $"id=\"{scope}-{hit.Groups[1].Value}\""))

        let referenced =
            reference.Replace(identified, (fun hit -> $"url(#{scope}-{hit.Groups[1].Value})"))

        referenced
            .Replace("class=\"glint\"", $"class=\"{scope}-glint\"")
            .Replace(".glint{", $".{scope}-glint{{")
            .Replace(".glint,", $".{scope}-glint,")
            .Replace("animation:glint", $"animation:{scope}-glint")
            .Replace("@keyframes glint", $"@keyframes {scope}-glint")

    /// Wraps drawn artwork as a document.
    let wrap (drawing: Drawing) =
        let sheet =
            if String.IsNullOrEmpty drawing.Style then
                ""
            else
                $"<style>{drawing.Style}</style>"

        [ $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{Svg.n drawing.X} {Svg.n drawing.Y} {Svg.n drawing.Width} {Svg.n drawing.Height}" width="{Svg.n drawing.Width}" height="{Svg.n drawing.Height}" role="img" aria-label="{Svg.esc drawing.Title}" data-generated-by="Blokemon.PackGen">"""
          $"<title>{Svg.esc drawing.Title}</title>"
          $"<defs>{drawing.Defs}</defs>{sheet}"
          drawing.Art
          "</svg>"
          "" ]
        |> String.concat "\n"
        |> qualify drawing.Scope

    /// The stylesheet driving a travelling glint.
    let glint (travel: float) (delay: GlintDelay) =
        // Written as a template rather than interpolated: CSS closes nested blocks with runs of
        // braces that an interpolated string would read as holes.
        let template =
            [ ".glint{animation:glint 5.5s linear infinite;animation-delay:DELAY}"
              "@keyframes glint{0%{transform:translate(-TRAVELpx,0);animation-timing-function:cubic-bezier(.35,0,.25,1)}13%{transform:translate(TRAVELpx,0)}100%{transform:translate(TRAVELpx,0)}}"
              "@media(prefers-reduced-motion:reduce){.glint{animation:none}}" ]
            |> String.concat "\n"

        template.Replace("DELAY", delay.ToCssValue()).Replace("TRAVEL", Svg.n travel)

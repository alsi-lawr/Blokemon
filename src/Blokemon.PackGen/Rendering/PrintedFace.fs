namespace Blokemon.PackGen.Rendering

open Blokemon.PackGen.Domain

/// The proportions of one printed face.
type FaceMetrics =
    {
        /// The face width.
        Width: float

        /// The face height.
        Height: float

        /// The top of the wordmark.
        WordmarkTop: float

        /// The wordmark size.
        WordmarkSize: float

        /// The wordmark outline width.
        WordmarkStroke: float

        /// The top of the pack name.
        SetnameTop: float

        /// The pack name size.
        SetnameSize: float

        /// The pack name tracking as a fraction of its size.
        SetnameTracking: float

        /// The height of the foot above the face bottom.
        FootBottom: float

        /// The inset of the foot from the face sides.
        FootPad: float

        /// The contents line size.
        CountSize: float

        /// The declaration size.
        LegalSize: float

        /// The width the declaration wraps within.
        LegalWidth: float

        /// The barcode width.
        BarWidth: float

        /// The barcode height.
        BarHeight: float
    }

/// The proportions of one printed face.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FaceMetrics =

    /// The proportions of a booster wrapper face.
    let booster =
        { Width = 460.0
          Height = 750.0
          WordmarkTop = 120.0
          WordmarkSize = 46.0
          WordmarkStroke = 7.0
          SetnameTop = 188.0
          SetnameSize = 15.0
          SetnameTracking = 0.3
          FootBottom = 52.0
          FootPad = 24.0
          CountSize = 18.0
          LegalSize = 8.0
          LegalWidth = 150.0
          BarWidth = 84.0
          BarHeight = 32.0 }

    /// The proportions of a channel-pack wrapper face.
    let small =
        { Width = 340.0
          Height = 530.0
          WordmarkTop = 92.0
          WordmarkSize = 30.0
          WordmarkStroke = 5.0
          SetnameTop = 142.0
          SetnameSize = 11.0
          SetnameTracking = 0.22
          FootBottom = 44.0
          FootPad = 14.0
          CountSize = 13.0
          LegalSize = 6.5
          LegalWidth = 104.0
          BarWidth = 58.0
          BarHeight = 23.0 }

    /// The proportions of a carton front.
    let cartonFront =
        { Width = 360.0
          Height = 500.0
          WordmarkTop = 96.0
          WordmarkSize = 38.0
          WordmarkStroke = 6.0
          SetnameTop = 154.0
          SetnameSize = 13.0
          SetnameTracking = 0.3
          FootBottom = 34.0
          FootPad = 18.0
          CountSize = 15.0
          LegalSize = 7.0
          LegalWidth = 120.0
          BarWidth = 64.0
          BarHeight = 26.0 }

/// The furniture printed on a face.
module PrintedFace =

    let private display = "'Arial Black','Helvetica Neue',Impact,sans-serif"

    let private text = "system-ui,'Segoe UI',sans-serif"

    /// The definitions the printed furniture refers to.
    let defs =
        [ """<filter id="wm-cast" x="-20%" y="-20%" width="140%" height="160%"><feDropShadow dx="0" dy="4" stdDeviation="0" flood-color="#080c1e" flood-opacity="0.4"/></filter>"""
          """<filter id="count-cast" x="-30%" y="-40%" width="160%" height="200%"><feDropShadow dx="0" dy="2" stdDeviation="2" flood-color="#000000" flood-opacity="0.55"/></filter>"""
          """<pattern id="barcode" width="13" height="1" patternUnits="userSpaceOnUse"><rect width="13" height="1" fill="#ffffff"/><rect x="0" width="2" height="1" fill="#111111"/><rect x="4" width="1" height="1" fill="#111111"/><rect x="9" width="2" height="1" fill="#111111"/></pattern>""" ]
        |> String.concat "\n"

    let private barcode (face: FaceMetrics) (bottom: float) =
        let x = face.Width - face.FootPad - face.BarWidth
        let y = bottom - face.BarHeight
        let inner = 3.0

        $"""<g><rect x="{Svg.n x}" y="{Svg.n y}" width="{Svg.n face.BarWidth}" height="{Svg.n face.BarHeight}" rx="2" fill="#ffffff"/><rect x="{Svg.n (x + inner)}" y="{Svg.n (y + inner)}" width="{Svg.n (face.BarWidth - inner * 2.0)}" height="{Svg.n (face.BarHeight - inner * 2.0)}" fill="url(#barcode)" preserveAspectRatio="none"/></g>"""

    let private foot (pack: Pack) (profile: PackProfile) (palette: Palette) (face: FaceMetrics) =
        // The foot is one flex row aligned to its own bottom edge, so the barcode, the contents
        // line and the two declaration lines all sit on the same baseline rather than on a grid.
        let bottom = face.Height - face.FootBottom
        let line = face.LegalSize * 1.3
        let legalTop = bottom - line * 2.0
        let left = face.FootPad

        let declaration = pack.Contents.Declaration.ToUpperInvariant()
        let copyright = pack.Contents.Copyright(Svg.esc profile.Noun).ToUpperInvariant()

        [ $"""<text x="{Svg.n left}" y="{Svg.n (legalTop - face.CountSize * 0.2)}" font-family="{text}" font-size="{Svg.n face.CountSize}" font-weight="900" letter-spacing="{Svg.n (face.CountSize * 0.09)}" fill="{palette.Print}" filter="url(#count-cast)">{Svg.esc (pack.Contents.Count.ToUpperInvariant())}</text>"""
          $"""<g font-family="{text}" font-size="{Svg.n face.LegalSize}" font-weight="600" letter-spacing="{Svg.n (face.LegalSize * 0.03)}" fill="{palette.Print}" opacity="0.7">"""
          $"""<text x="{Svg.n left}" y="{Svg.n (legalTop + line * 0.78)}">{declaration}</text>"""
          $"""<text x="{Svg.n left}" y="{Svg.n (legalTop + line * 1.78)}">{copyright}</text>"""
          "</g>"
          barcode face bottom ]
        |> String.concat "\n"

    /// Prints the wordmark, the pack name and the foot onto a face.
    let print (pack: Pack) (profile: PackProfile) (palette: Palette) (face: FaceMetrics) =
        let centre = face.Width / 2.0
        let name = Svg.esc ((profile.Name pack.Key).ToUpperInvariant())
        let noun = Svg.esc (profile.Noun.ToUpperInvariant())

        [ $"""<text x="{Svg.n centre}" y="{Svg.n (face.WordmarkTop + face.WordmarkSize * 0.78)}" text-anchor="middle" font-family="{display}" font-size="{Svg.n face.WordmarkSize}" fill="{palette.WordmarkFill}" stroke="{palette.WordmarkLine}" stroke-width="{Svg.n face.WordmarkStroke}" stroke-linejoin="round" paint-order="stroke fill" filter="url(#wm-cast)">{noun}</text>"""
          $"""<text x="{Svg.n centre}" y="{Svg.n (face.SetnameTop + face.SetnameSize * 0.78)}" text-anchor="middle" font-family="{text}" font-size="{Svg.n face.SetnameSize}" font-weight="900" letter-spacing="{Svg.n (face.SetnameSize * face.SetnameTracking)}" fill="{palette.Print}">{name}</text>"""
          foot pack profile palette face ]
        |> String.concat "\n"

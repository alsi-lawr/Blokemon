namespace Blokemon.PackGen.Rendering

open System
open System.Globalization

/// A gradient stop.
type Stop =
    {
        /// The stop position as a fraction of the gradient line.
        Offset: float

        /// The stop colour.
        Colour: string

        /// The stop opacity.
        Alpha: float
    }

/// Gradient stops.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Stop =

    /// A fully opaque stop.
    let solid offset colour =
        { Offset = offset
          Colour = colour
          Alpha = 1.0 }

    /// A stop at a declared opacity.
    let faded offset colour alpha =
        { Offset = offset
          Colour = colour
          Alpha = alpha }

/// Primitives shared by every printed element.
module Svg =

    /// Formats a number for a coordinate or a length.
    let n (value: float) =
        Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture)

    /// Escapes text for markup.
    let esc (value: string) =
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;")

    /// Defines a linear gradient across a box at a CSS gradient angle, measured clockwise from
    /// upwards.
    let linear (id: string) (angle: float) (width: float) (height: float) (stops: Stop list) =
        // CSS measures the gradient line clockwise from upwards and sizes it so the box corners
        // land exactly on its ends, which is why the length is a sum of projections rather than
        // the box diagonal.
        let radians = angle * Math.PI / 180.0
        let dx, dy = sin radians, -cos radians
        let length = (abs (width * dx) + abs (height * dy)) / 2.0
        let cx, cy = width / 2.0, height / 2.0

        let marks =
            stops
            |> List.map (fun stop ->
                $"""<stop offset="{n stop.Offset}" stop-color="{stop.Colour}" stop-opacity="{n stop.Alpha}"/>""")
            |> String.concat ""

        $"""<linearGradient id="{id}" gradientUnits="userSpaceOnUse" x1="{n (cx - dx * length)}" y1="{n (cy - dy * length)}" x2="{n (cx + dx * length)}" y2="{n (cy + dy * length)}">{marks}</linearGradient>"""

    /// Defines a stripe pattern running across a gradient at a CSS gradient angle.
    let stripes
        (id: string)
        (angle: float)
        (period: float)
        (thickness: float)
        (colour: string)
        (alpha: float)
        =
        // The stripes lie perpendicular to the gradient line, so a tile of vertical bars is
        // turned by the angle the gradient makes with the horizontal.
        let turn = angle - 90.0

        $"""<pattern id="{id}" width="{n period}" height="{n period}" patternUnits="userSpaceOnUse" patternTransform="rotate({n turn})"><rect width="{n thickness}" height="{n period}" fill="{colour}" fill-opacity="{n alpha}"/></pattern>"""

    /// A rectangle filled by a definition, carrying attributes appended to the element.
    let rectWith
        (x: float)
        (y: float)
        (width: float)
        (height: float)
        (fill: string)
        (extra: string)
        =
        $"""<rect x="{n x}" y="{n y}" width="{n width}" height="{n height}" fill="{fill}"{extra}/>"""

    /// A rectangle filled by a definition.
    let rect x y width height fill = rectWith x y width height fill ""

    /// A reference to a definition.
    let url (id: string) = $"url(#{id})"

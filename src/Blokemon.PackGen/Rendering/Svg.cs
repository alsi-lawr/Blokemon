using System.Globalization;

namespace Blokemon.PackGen.Rendering;

/// <summary>A gradient stop.</summary>
/// <param name="Offset">The stop position as a fraction of the gradient line.</param>
/// <param name="Colour">The stop colour.</param>
/// <param name="Alpha">The stop opacity.</param>
public readonly record struct Stop(double Offset, string Colour, double Alpha = 1d);

/// <summary>Primitives shared by every printed element.</summary>
public static class Svg
{
    /// <summary>Formats a number for a coordinate or a length.</summary>
    /// <param name="value">The number to format.</param>
    /// <returns>The formatted number.</returns>
    public static string N(double value) =>
        Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Escapes text for markup.</summary>
    /// <param name="value">The text to escape.</param>
    /// <returns>The escaped text.</returns>
    public static string Esc(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    /// <summary>Defines a linear gradient across a box at a CSS gradient angle.</summary>
    /// <param name="id">The gradient identity.</param>
    /// <param name="angle">The CSS angle in degrees, measured clockwise from upwards.</param>
    /// <param name="width">The box width.</param>
    /// <param name="height">The box height.</param>
    /// <param name="stops">The stops along the gradient line.</param>
    /// <returns>The gradient definition.</returns>
    public static string Linear(
        string id,
        double angle,
        double width,
        double height,
        params Stop[] stops
    )
    {
        // CSS measures the gradient line clockwise from upwards and sizes it so the box corners
        // land exactly on its ends, which is why the length is a sum of projections rather than
        // the box diagonal.
        var radians = angle * Math.PI / 180d;
        var (dx, dy) = (Math.Sin(radians), -Math.Cos(radians));
        var length = (Math.Abs(width * dx) + Math.Abs(height * dy)) / 2d;
        var (cx, cy) = (width / 2d, height / 2d);

        var marks = string.Concat(
            stops.Select(stop =>
                $"""<stop offset="{N(stop.Offset)}" stop-color="{stop.Colour}" stop-opacity="{N(stop.Alpha)}"/>"""
            )
        );

        return $"""<linearGradient id="{id}" gradientUnits="userSpaceOnUse" x1="{N(cx - (dx * length))}" y1="{N(cy - (dy * length))}" x2="{N(cx + (dx * length))}" y2="{N(cy + (dy * length))}">{marks}</linearGradient>""";
    }

    /// <summary>Defines a stripe pattern running at a CSS gradient angle.</summary>
    /// <param name="id">The pattern identity.</param>
    /// <param name="angle">The CSS angle of the gradient the stripes run across.</param>
    /// <param name="period">The distance between stripes.</param>
    /// <param name="thickness">The stripe thickness.</param>
    /// <param name="colour">The stripe colour.</param>
    /// <param name="alpha">The stripe opacity.</param>
    /// <returns>The pattern definition.</returns>
    public static string Stripes(
        string id,
        double angle,
        double period,
        double thickness,
        string colour,
        double alpha
    )
    {
        // The stripes lie perpendicular to the gradient line, so a tile of vertical bars is
        // turned by the angle the gradient makes with the horizontal.
        var turn = angle - 90d;

        return $"""<pattern id="{id}" width="{N(period)}" height="{N(period)}" patternUnits="userSpaceOnUse" patternTransform="rotate({N(turn)})"><rect width="{N(thickness)}" height="{N(period)}" fill="{colour}" fill-opacity="{N(alpha)}"/></pattern>""";
    }

    /// <summary>A rectangle filled by a definition.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="fill">The fill.</param>
    /// <param name="extra">Attributes appended to the element.</param>
    /// <returns>The rectangle.</returns>
    public static string Rect(
        double x,
        double y,
        double width,
        double height,
        string fill,
        string extra = ""
    ) =>
        $"""<rect x="{N(x)}" y="{N(y)}" width="{N(width)}" height="{N(height)}" fill="{fill}"{extra}/>""";

    /// <summary>A reference to a definition.</summary>
    /// <param name="id">The definition identity.</param>
    /// <returns>The reference.</returns>
    public static string Url(string id) => $"url(#{id})";
}

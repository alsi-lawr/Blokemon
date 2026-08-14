using Blokemon.PackGen.Domain;

namespace Blokemon.PackGen.Rendering;

/// <summary>The proportions of one printed face.</summary>
/// <param name="Width">The face width.</param>
/// <param name="Height">The face height.</param>
/// <param name="WordmarkTop">The top of the wordmark.</param>
/// <param name="WordmarkSize">The wordmark size.</param>
/// <param name="WordmarkStroke">The wordmark outline width.</param>
/// <param name="SetnameTop">The top of the pack name.</param>
/// <param name="SetnameSize">The pack name size.</param>
/// <param name="SetnameTracking">The pack name tracking as a fraction of its size.</param>
/// <param name="FootBottom">The height of the foot above the face bottom.</param>
/// <param name="FootPad">The inset of the foot from the face sides.</param>
/// <param name="CountSize">The contents line size.</param>
/// <param name="LegalSize">The declaration size.</param>
/// <param name="LegalWidth">The width the declaration wraps within.</param>
/// <param name="BarWidth">The barcode width.</param>
/// <param name="BarHeight">The barcode height.</param>
public sealed record FaceMetrics(
    double Width,
    double Height,
    double WordmarkTop,
    double WordmarkSize,
    double WordmarkStroke,
    double SetnameTop,
    double SetnameSize,
    double SetnameTracking,
    double FootBottom,
    double FootPad,
    double CountSize,
    double LegalSize,
    double LegalWidth,
    double BarWidth,
    double BarHeight
)
{
    /// <summary>The proportions of a booster wrapper face.</summary>
    public static FaceMetrics Booster { get; } =
        new(460d, 750d, 120d, 46d, 7d, 188d, 15d, 0.3d, 52d, 24d, 18d, 8d, 150d, 84d, 32d);

    /// <summary>The proportions of a channel-pack wrapper face.</summary>
    public static FaceMetrics Small { get; } =
        new(340d, 530d, 92d, 30d, 5d, 142d, 11d, 0.22d, 44d, 14d, 13d, 6.5d, 104d, 58d, 23d);

    /// <summary>The proportions of a carton front.</summary>
    public static FaceMetrics CartonFront { get; } =
        new(360d, 500d, 96d, 38d, 6d, 154d, 13d, 0.3d, 34d, 18d, 15d, 7d, 120d, 64d, 26d);
}

/// <summary>The furniture printed on a face.</summary>
public static class PrintedFace
{
    private const string _display = "'Arial Black','Helvetica Neue',Impact,sans-serif";
    private const string _text = "system-ui,'Segoe UI',sans-serif";

    /// <summary>The definitions the printed furniture refers to.</summary>
    /// <returns>The definitions.</returns>
    public static string Defs() =>
        """
            <filter id="wm-cast" x="-20%" y="-20%" width="140%" height="160%"><feDropShadow dx="0" dy="4" stdDeviation="0" flood-color="#080c1e" flood-opacity="0.4"/></filter>
            <filter id="count-cast" x="-30%" y="-40%" width="160%" height="200%"><feDropShadow dx="0" dy="2" stdDeviation="2" flood-color="#000000" flood-opacity="0.55"/></filter>
            <pattern id="barcode" width="13" height="1" patternUnits="userSpaceOnUse"><rect width="13" height="1" fill="#ffffff"/><rect x="0" width="2" height="1" fill="#111111"/><rect x="4" width="1" height="1" fill="#111111"/><rect x="9" width="2" height="1" fill="#111111"/></pattern>
            """;

    /// <summary>Prints the wordmark, the pack name and the foot onto a face.</summary>
    /// <param name="pack">The pack being printed.</param>
    /// <param name="profile">The profile printing it.</param>
    /// <param name="palette">The material palette.</param>
    /// <param name="face">The face proportions.</param>
    /// <returns>The furniture markup.</returns>
    public static string Print(Pack pack, PackProfile profile, Palette palette, FaceMetrics face)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(face);

        var centre = face.Width / 2d;
        var name = Svg.Esc(profile.Name(pack.Key).ToUpperInvariant());

        return $"""
            <text x="{Svg.N(centre)}" y="{Svg.N(
                face.WordmarkTop + (face.WordmarkSize * 0.78d)
            )}" text-anchor="middle" font-family="{_display}" font-size="{Svg.N(
                face.WordmarkSize
            )}" fill="{palette.WordmarkFill}" stroke="{palette.WordmarkLine}" stroke-width="{Svg.N(
                face.WordmarkStroke
            )}" stroke-linejoin="round" paint-order="stroke fill" filter="url(#wm-cast)">{Svg.Esc(
                profile.Noun.ToUpperInvariant()
            )}</text>
            <text x="{Svg.N(centre)}" y="{Svg.N(
                face.SetnameTop + (face.SetnameSize * 0.78d)
            )}" text-anchor="middle" font-family="{_text}" font-size="{Svg.N(
                face.SetnameSize
            )}" font-weight="900" letter-spacing="{Svg.N(
                face.SetnameSize * face.SetnameTracking
            )}" fill="{palette.Print}">{name}</text>
            {Foot(pack, profile, palette, face)}
            """;
    }

    private static string Foot(Pack pack, PackProfile profile, Palette palette, FaceMetrics face)
    {
        // The foot is one flex row aligned to its own bottom edge, so the barcode, the contents
        // line and the two declaration lines all sit on the same baseline rather than on a grid.
        var bottom = face.Height - face.FootBottom;
        var line = face.LegalSize * 1.3d;
        var legalTop = bottom - (line * 2d);
        var left = face.FootPad;

        var declaration = pack.Contents.Declaration.ToUpperInvariant();
        var copyright = pack.Contents.Copyright(Svg.Esc(profile.Noun)).ToUpperInvariant();

        return $"""
            <text x="{Svg.N(left)}" y="{Svg.N(
                legalTop - (face.CountSize * 0.2d)
            )}" font-family="{_text}" font-size="{Svg.N(
                face.CountSize
            )}" font-weight="900" letter-spacing="{Svg.N(
                face.CountSize * 0.09d
            )}" fill="{palette.Print}" filter="url(#count-cast)">{Svg.Esc(
                pack.Contents.Count.ToUpperInvariant()
            )}</text>
            <g font-family="{_text}" font-size="{Svg.N(
                face.LegalSize
            )}" font-weight="600" letter-spacing="{Svg.N(
                face.LegalSize * 0.03d
            )}" fill="{palette.Print}" opacity="0.7">
            <text x="{Svg.N(left)}" y="{Svg.N(legalTop + (line * 0.78d))}">{declaration}</text>
            <text x="{Svg.N(left)}" y="{Svg.N(legalTop + (line * 1.78d))}">{copyright}</text>
            </g>
            {Barcode(face, bottom)}
            """;
    }

    private static string Barcode(FaceMetrics face, double bottom)
    {
        var x = face.Width - face.FootPad - face.BarWidth;
        var y = bottom - face.BarHeight;
        var inner = 3d;

        return $"""
            <g><rect x="{Svg.N(x)}" y="{Svg.N(y)}" width="{Svg.N(face.BarWidth)}" height="{Svg.N(
                face.BarHeight
            )}" rx="2" fill="#ffffff"/><rect x="{Svg.N(x + inner)}" y="{Svg.N(
                y + inner
            )}" width="{Svg.N(face.BarWidth - (inner * 2d))}" height="{Svg.N(
                face.BarHeight - (inner * 2d)
            )}" fill="url(#barcode)" preserveAspectRatio="none"/></g>
            """;
    }
}

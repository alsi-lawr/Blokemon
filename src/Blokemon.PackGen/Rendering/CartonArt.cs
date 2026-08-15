using Blokemon.PackGen.Domain;

namespace Blokemon.PackGen.Rendering;

/// <summary>A rigid carton drawn as a printed object.</summary>
public static class CartonArt
{
    private const double _width = 360d;
    private const double _height = 500d;
    private const double _depth = 118d;
    private const double _stage = 640d;
    private const double _pitch = -14d;
    private const double _yaw = -27d;
    private const double _gauge = 7d;
    private const double _seat = 14d;

    /// <summary>Draws a carton.</summary>
    /// <param name="pack">The pack to draw.</param>
    /// <param name="profile">The profile printing it.</param>
    /// <returns>The document.</returns>
    public static string Draw(Pack pack, PackProfile profile)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(profile);

        var palette = Palette.For(pack.MaterialUnder(profile.Stock));

        // A carton is opaque, so only the faces turned towards the viewer are drawn, and those
        // are painted far to near so the shared folds meet without seams.
        var art = string.Concat(
            Cast(),
            string.Concat(
                Faces()
                    .Where(face => Rotate(face.Normal).Z > 0d)
                    .OrderBy(face => Rotate(face.Centre).Z)
                    .Select(face => Face(face, pack, profile, palette))
            )
        );

        return Document.Wrap(
            0d,
            0d,
            _stage,
            _stage,
            $"{profile.Noun} {profile.Name(pack.Key)} carton",
            Defs(palette),
            art,
            string.Empty,
            $"{pack.Key.Slug()}-{profile.Stock.ToString().ToLowerInvariant()}"
        );
    }

    /// <summary>Draws the open inner tray the carton's lid slides off.</summary>
    /// <param name="pack">The pack whose tray is drawn.</param>
    /// <param name="profile">The profile printing it.</param>
    /// <returns>The document.</returns>
    public static string DrawTray(Pack pack, PackProfile profile)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(profile);

        var palette = Palette.For(pack.MaterialUnder(profile.Stock));

        var art = string.Concat(
            Cast(),
            string.Concat(
                TrayFaces()
                    .Where(face => Rotate(face.Normal).Z > 0d)
                    .OrderBy(face => Rotate(face.Centre).Z)
                    .Select(face => TrayFace(face, palette))
            )
        );

        return Document.Wrap(
            0d,
            0d,
            _stage,
            _stage,
            $"{profile.Noun} {profile.Name(pack.Key)} inner tray",
            string.Concat(StockDefs(TrayFaces(), palette), SurfaceDefs(palette)),
            art,
            string.Empty,
            $"{pack.Key.Slug()}-tray-{profile.Stock.ToString().ToLowerInvariant()}"
        );
    }

    private static string Defs(Palette palette) =>
        string.Concat(StockDefs(Faces(), palette), SurfaceDefs(palette), PrintedFace.Defs());

    private static string StockDefs(Panel[] faces, Palette palette) =>
        string.Concat(
            faces.Select(face =>
                Svg.Linear(
                    $"stock-{face.Name}",
                    palette.StockAngle,
                    face.Across,
                    face.Down,
                    palette.Stock
                )
            )
        );

    private static string SurfaceDefs(Palette palette) =>
        string.Concat(
            """<linearGradient id="carton-form" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity="0.07"/><stop offset="0.3" stop-color="#000000" stop-opacity="0"/><stop offset="1" stop-color="#000000" stop-opacity="0.16"/></linearGradient>""",
            """<radialGradient id="ground"><stop offset="0" stop-color="#0a0802" stop-opacity="0.55"/><stop offset="0.68" stop-color="#0a0802" stop-opacity="0"/></radialGradient>""",
            """<filter id="soften"><feGaussianBlur stdDeviation="7"/></filter>""",
            palette.Fibre
                ? string.Concat(
                    Svg.Stripes("fibre-warp", 72d, 4d, 1d, "#5a3c14", 0.14d),
                    Svg.Stripes("fibre-weft", -64d, 6d, 1d, "#5a3c14", 0.10d)
                )
                : string.Empty
        );

    private static string Face(Panel panel, Pack pack, PackProfile profile, Palette palette)
    {
        var content = string.Concat(
            Svg.Rect(0d, 0d, panel.Across, panel.Down, Svg.Url($"stock-{panel.Name}")),
            Grain(panel, palette),
            Svg.Rect(0d, 0d, panel.Across, panel.Down, Svg.Url("carton-form")),
            Furniture(panel, pack, profile, palette),
            Shade(panel),
            Core(panel)
        );

        return $"""<g transform="matrix({Matrix(panel)})">{content}</g>""";
    }

    private static string TrayFace(Panel panel, Palette palette)
    {
        var content = string.Concat(
            Svg.Rect(0d, 0d, panel.Across, panel.Down, Svg.Url($"stock-{panel.Name}")),
            Grain(panel, palette),
            Svg.Rect(0d, 0d, panel.Across, panel.Down, Svg.Url("carton-form")),
            Shade(panel),
            Rim(panel)
        );

        return $"""<g transform="matrix({Matrix(panel)})">{content}</g>""";
    }

    private static string Matrix(Panel panel)
    {
        var origin = Project(panel.At(0d, 0d));
        var across = Project(panel.At(panel.Across, 0d));
        var down = Project(panel.At(0d, panel.Down));

        return string.Join(
            " ",
            Svg.N((across.X - origin.X) / panel.Across),
            Svg.N((across.Y - origin.Y) / panel.Across),
            Svg.N((down.X - origin.X) / panel.Down),
            Svg.N((down.Y - origin.Y) / panel.Down),
            Svg.N(origin.X),
            Svg.N(origin.Y)
        );
    }

    private static string Grain(Panel panel, Palette palette) =>
        palette.Fibre
            ? $"""<g style="mix-blend-mode:multiply" opacity="0.5">{Svg.Rect(0d, 0d, panel.Across, panel.Down, Svg.Url("fibre-warp"))}{Svg.Rect(0d, 0d, panel.Across, panel.Down, Svg.Url("fibre-weft"))}</g>"""
            : string.Empty;

    private static string Shade(Panel panel) =>
        panel.Shade is { } shade
            ? Svg.Rect(
                0d,
                0d,
                panel.Across,
                panel.Down,
                shade,
                $" fill-opacity=\"{Svg.N(panel.ShadeAlpha)}\""
            )
            : string.Empty;

    private static string Furniture(Panel panel, Pack pack, PackProfile profile, Palette palette) =>
        panel.Name switch
        {
            "front" => PrintedFace.Print(pack, profile, palette, FaceMetrics.CartonFront),
            "side" => Spine(panel, pack, profile, palette),
            _ => string.Empty,
        };

    private static string Spine(Panel panel, Pack pack, PackProfile profile, Palette palette) =>
        $"""<text transform="translate({Svg.N(panel.Across / 2d)} {Svg.N(panel.Down / 2d)}) rotate(90)" y="6.3" text-anchor="middle" font-family="system-ui,'Segoe UI',sans-serif" font-size="18" font-weight="900" letter-spacing="5.76" fill="{palette.Print}" opacity="0.9">{Svg.Esc($"{profile.Noun} · {profile.Name(pack.Key)}".ToUpperInvariant())}</text>""";

    // Every fold shows the pale board core along its edge, which is what tells a carton from a
    // flat panel once the sheen is gone.
    private static string Core(Panel panel) =>
        panel.Name switch
        {
            "front" =>
                $"""{Svg.Rect(panel.Across - 1d, 0d, 1d, panel.Down, "#ffffff", " fill-opacity=\"0.34\"")}{Svg.Rect(0d, 0d, panel.Across, 1d, "#ffffff", " fill-opacity=\"0.3\"")}""",
            "side" => Svg.Rect(0d, 0d, 1d, panel.Down, "#ffffff", " fill-opacity=\"0.18\""),
            _ => string.Empty,
        };

    // The cut board core runs around the whole opening, which is what reads as an open tray
    // rather than a shorter closed box.
    private static string Rim(Panel panel) =>
        panel.Name switch
        {
            "tray-front" =>
                $"""{Svg.Rect(0d, 0d, panel.Across, 1.4d, "#ffffff", " fill-opacity=\"0.34\"")}{Svg.Rect(panel.Across - 1d, 0d, 1d, panel.Down, "#ffffff", " fill-opacity=\"0.3\"")}""",
            "tray-side" =>
                $"""{Svg.Rect(0d, 0d, panel.Across, 1.4d, "#ffffff", " fill-opacity=\"0.26\"")}{Svg.Rect(0d, 0d, 1d, panel.Down, "#ffffff", " fill-opacity=\"0.18\"")}""",
            "tray-back" => Svg.Rect(0d, 0d, panel.Across, 1.4d, "#fffcf0", " fill-opacity=\"0.2\""),
            "tray-left" => Svg.Rect(0d, 0d, panel.Across, 1.4d, "#fffcf0", " fill-opacity=\"0.16\""),
            _ => string.Empty,
        };

    private static string Cast() =>
        """<g transform="translate(325 516) skewX(-27) scale(1 0.48) translate(-325 -516)" filter="url(#soften)"><ellipse cx="325" cy="516" rx="215" ry="46" fill="url(#ground)"/></g>""";

    private static Panel[] Faces() =>
        [
            new Panel(
                "front",
                _width,
                _height,
                (0d, 0d, 1d),
                (u, v) => (u - 180d, v - 250d, 59d),
                null,
                0d
            ),
            new Panel(
                "side",
                _depth,
                _height,
                (1d, 0d, 0d),
                (u, v) => (180d, v - 250d, 59d - u),
                "#060a04",
                0.46d
            ),
            new Panel(
                "top",
                _width,
                _depth,
                (0d, -1d, 0d),
                (u, v) => (u - 180d, -250d, v - 59d),
                "#fffcf0",
                0.17d
            ),
            new Panel(
                "bottom",
                _width,
                _depth,
                (0d, 1d, 0d),
                (u, v) => (u - 180d, 250d, 59d - v),
                "#040602",
                0.72d
            ),
        ];

    // The tray keeps the carton's rig, so the lid art and the tray art line up exactly when one
    // is drawn over the other. It is a board gauge smaller on every side, seats below the lid's
    // top, and shows its shaded interior through the open mouth.
    private static Panel[] TrayFaces()
    {
        var width = _width - (2d * _gauge);
        var depth = _depth - (2d * _gauge);
        var down = _height - _seat;
        var rim = (_height / 2d) - down;
        var innerWidth = width - (2d * _gauge);
        var innerDepth = depth - (2d * _gauge);
        var floor = (_height / 2d) - _gauge;
        var cavity = floor - rim;

        return
        [
            new Panel(
                "tray-front",
                width,
                down,
                (0d, 0d, 1d),
                (u, v) => (u - (width / 2d), v + rim, depth / 2d),
                null,
                0d
            ),
            new Panel(
                "tray-side",
                depth,
                down,
                (1d, 0d, 0d),
                (u, v) => (width / 2d, v + rim, (depth / 2d) - u),
                "#060a04",
                0.46d
            ),
            new Panel(
                "tray-left",
                innerDepth,
                cavity,
                (1d, 0d, 0d),
                (u, v) => (-(innerWidth / 2d), v + rim, (innerDepth / 2d) - u),
                "#030c07",
                0.58d
            ),
            new Panel(
                "tray-back",
                innerWidth,
                cavity,
                (0d, 0d, 1d),
                (u, v) => (u - (innerWidth / 2d), v + rim, -(innerDepth / 2d)),
                "#04100a",
                0.5d
            ),
            new Panel(
                "tray-floor",
                innerWidth,
                innerDepth,
                (0d, -1d, 0d),
                (u, v) => (u - (innerWidth / 2d), floor, (innerDepth / 2d) - v),
                "#020805",
                0.66d
            ),
        ];
    }

    // The rig turns the box and the projection is orthographic, which keeps every face an exact
    // parallelogram so its printing maps onto it without distortion.
    private static (double X, double Y, double Z) Rotate((double X, double Y, double Z) point)
    {
        var (cy, sy) = (Cos(_yaw), Sin(_yaw));
        var (x1, z1) = ((point.X * cy) + (point.Z * sy), (point.Z * cy) - (point.X * sy));
        var (cx, sx) = (Cos(_pitch), Sin(_pitch));

        return (x1, (point.Y * cx) - (z1 * sx), (point.Y * sx) + (z1 * cx));
    }

    private static (double X, double Y) Project((double X, double Y, double Z) point)
    {
        var turned = Rotate(point);
        return (turned.X + (_stage / 2d), turned.Y + (_stage / 2d));
    }

    private static double Cos(double degrees) => Math.Cos(degrees * Math.PI / 180d);

    private static double Sin(double degrees) => Math.Sin(degrees * Math.PI / 180d);

    private sealed record Panel(
        string Name,
        double Across,
        double Down,
        (double X, double Y, double Z) Normal,
        Func<double, double, (double X, double Y, double Z)> At,
        string? Shade,
        double ShadeAlpha
    )
    {
        public (double X, double Y, double Z) Centre => At(Across / 2d, Down / 2d);
    }
}

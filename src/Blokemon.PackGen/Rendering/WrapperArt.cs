using Blokemon.PackGen.Domain;

namespace Blokemon.PackGen.Rendering;

/// <summary>A flexible wrapper drawn as a printed object.</summary>
public static class WrapperArt
{
    private const double _sealShadow = 24d;
    private const double _crimp = 14d;
    private const double _creaseInset = 0.06d;
    private const double _glintInset = 0.25d;

    /// <summary>Draws a wrapper.</summary>
    /// <param name="pack">The pack to draw.</param>
    /// <param name="profile">The profile printing it.</param>
    /// <param name="wrapper">The wrapper construction.</param>
    /// <returns>The document.</returns>
    public static string Draw(Pack pack, PackProfile profile, PackFormat.Wrapper wrapper)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(wrapper);

        var booster = wrapper.Size is WrapperSize.Booster;
        var face = booster ? FaceMetrics.Booster : FaceMetrics.Small;
        var palette = Palette.For(pack.MaterialUnder(profile.Stock));
        var height = booster ? 830d : 600d;
        var (bodyTop, sealHeight) = booster ? (40d, 44d) : (34d, 34d);
        var groundDrop = booster ? 24d : 18d;
        var top = wrapper.Resealable ? -34d : -8d;

        var art = string.Concat(
            Ground(face.Width, height, groundDrop),
            $"""<g clip-path="url(#body)" transform="translate(0 {Svg.N(bodyTop)})">""",
            Svg.Rect(0d, 0d, face.Width, face.Height, Svg.Url("stock")),
            Svg.Rect(0d, 0d, face.Width, face.Height, Svg.Url("form-across")),
            Svg.Rect(0d, 0d, face.Width, face.Height, Svg.Url("form-down")),
            Creases(face, palette),
            Grain(face, palette),
            Specular(face, palette),
            PrintedFace.Print(pack, profile, palette, face),
            Zip(wrapper, face),
            "</g>",
            SealShadows(face.Width, height, sealHeight),
            Seal(face.Width, 0d, sealHeight, flip: false),
            Seal(face.Width, height - sealHeight, sealHeight, flip: true),
            Tab(wrapper, booster)
        );

        return Document.Wrap(
            0d,
            top,
            face.Width,
            height + groundDrop - top,
            $"{profile.Noun} {profile.Name(pack.Key)} wrapper",
            Defs(face, palette, sealHeight),
            art,
            palette.Specular > 0d
                ? Document.Glint(face.Width * (1d + (_glintInset * 2d)) * 1.45d, pack.Glint)
                : string.Empty,
            $"{pack.Key.Slug()}-{profile.Stock.ToString().ToLowerInvariant()}"
        );
    }

    private static string Defs(FaceMetrics face, Palette palette, double sealHeight)
    {
        var (w, h) = (face.Width, face.Height);
        var (cw, ch) = Overflow(face, _creaseInset);
        var (sw, sh) = Overflow(face, _glintInset);

        return string.Concat(
            $"""<clipPath id="body"><rect width="{Svg.N(w)}" height="{Svg.N(h)}" rx="3"/></clipPath>""",
            Svg.Linear("stock", palette.StockAngle, w, h, palette.Stock),
            Svg.Linear(
                "form-across",
                90d,
                w,
                h,
                new Stop(0d, "#000000", 0.46d),
                new Stop(0.07d, "#000000", 0.16d),
                new Stop(0.24d, "#ffffff", 0.10d),
                new Stop(0.44d, "#ffffff", 0.16d),
                new Stop(0.62d, "#ffffff", 0.06d),
                new Stop(0.88d, "#000000", 0.20d),
                new Stop(1d, "#000000", 0.5d)
            ),
            Svg.Linear(
                "form-down",
                180d,
                w,
                h,
                new Stop(0d, "#000000", 0.26d),
                new Stop(0.12d, "#000000", 0d),
                new Stop(0.84d, "#000000", 0d),
                new Stop(1d, "#000000", 0.32d)
            ),
            string.Concat(
                Creases()
                    .Index()
                    .Select(entry =>
                        Svg.Linear(
                            $"crease-{entry.Index}",
                            entry.Item.Angle,
                            cw,
                            ch,
                            new Stop(entry.Item.From, "#ffffff", 0d),
                            new Stop(entry.Item.Peak, "#ffffff", entry.Item.Light),
                            new Stop(entry.Item.Trough, "#000000", entry.Item.Dark),
                            new Stop(entry.Item.To, "#000000", 0d)
                        )
                    )
            ),
            Svg.Linear(
                "glint-core",
                102d,
                sw,
                sh,
                new Stop(0.28d, "#ffffff", 0d),
                new Stop(0.42d, "#ffffff", 0.10d),
                new Stop(0.5d, "#ffffff", 0.38d),
                new Stop(0.58d, "#ffffff", 0.10d),
                new Stop(0.72d, "#ffffff", 0d)
            ),
            Svg.Linear(
                "glint-trail",
                102d,
                sw,
                sh,
                new Stop(0.64d, "#ffffff", 0d),
                new Stop(0.73d, "#ffffff", 0.13d),
                new Stop(0.82d, "#ffffff", 0d)
            ),
            Svg.Linear(
                "seal",
                180d,
                w,
                sealHeight,
                new Stop(0d, palette.SealTop),
                new Stop(1d, palette.SealBottom)
            ),
            """<pattern id="rib" width="7" height="1" patternUnits="userSpaceOnUse"><rect width="2" height="1" fill="#000000" fill-opacity="0.34"/><rect x="2" width="2" height="1" fill="#ffffff" fill-opacity="0.2"/><rect x="4" width="3" height="1" fill="#000000" fill-opacity="0.1"/></pattern>""",
            Svg.Linear(
                "seal-cast-top",
                180d,
                w,
                _sealShadow,
                new Stop(0d, "#000000", 0.48d),
                new Stop(1d, "#000000", 0d)
            ),
            Svg.Linear(
                "seal-cast-bot",
                0d,
                w,
                _sealShadow,
                new Stop(0d, "#000000", 0.48d),
                new Stop(1d, "#000000", 0d)
            ),
            """<radialGradient id="ground"><stop offset="0" stop-color="#0a0802" stop-opacity="0.5"/><stop offset="0.7" stop-color="#0a0802" stop-opacity="0"/></radialGradient>""",
            Grain(palette),
            PrintedFace.Defs()
        );
    }

    // The six creases are authored rather than spread evenly, which would read as corduroy. Each
    // is a lit edge followed immediately by its own shadow, which is what a fold in film does.
    private static Crease[] Creases() =>
        [
            new Crease(97d, 0.18d, 0.21d, 0.23d, 0.27d, 0.5d, 0.35d),
            new Crease(84d, 0.44d, 0.47d, 0.49d, 0.53d, 0.34d, 0.28d),
            new Crease(103d, 0.66d, 0.69d, 0.71d, 0.75d, 0.4d, 0.3d),
            new Crease(75d, 0.08d, 0.10d, 0.12d, 0.15d, 0.22d, 0.2d),
            new Crease(160d, 0.30d, 0.33d, 0.35d, 0.39d, 0.2d, 0.18d),
            new Crease(168d, 0.72d, 0.75d, 0.77d, 0.81d, 0.24d, 0.2d),
        ];

    private static (double Width, double Height) Overflow(FaceMetrics face, double inset) =>
        (face.Width * (1d + (inset * 2d)), face.Height * (1d + (inset * 2d)));

    private static string Creases(FaceMetrics face, Palette palette)
    {
        var (w, h) = Overflow(face, _creaseInset);
        var layers = string.Concat(
            Creases()
                .Index()
                .Select(entry => Svg.Rect(0d, 0d, w, h, Svg.Url($"crease-{entry.Index}")))
        );

        return Overflowing("mix-blend-mode:overlay", palette.Crinkle, face, _creaseInset, layers);
    }

    private static string Specular(FaceMetrics face, Palette palette)
    {
        if (palette.Specular is 0d)
        {
            return string.Empty;
        }

        var (w, h) = Overflow(face, _glintInset);
        var sweep = string.Concat(
            Svg.Rect(0d, 0d, w, h, Svg.Url("glint-core")),
            Svg.Rect(0d, 0d, w, h, Svg.Url("glint-trail"))
        );

        return Overflowing(
            "mix-blend-mode:screen",
            palette.Specular,
            face,
            _glintInset,
            sweep,
            "glint"
        );
    }

    // Gradients are declared in user space from the origin, and the animated class owns the CSS
    // transform, so an overflowing layer is drawn at the origin inside its own offset child.
    private static string Overflowing(
        string blend,
        double opacity,
        FaceMetrics face,
        double inset,
        string layers,
        string? animated = null
    )
    {
        var mark = animated is null ? string.Empty : $" class=\"{animated}\"";
        var (x, y) = (-face.Width * inset, -face.Height * inset);

        return $"""<g{mark} style="{blend}" opacity="{Svg.N(opacity)}"><g transform="translate({Svg.N(x)} {Svg.N(y)})">{layers}</g></g>""";
    }

    private static string Grain(Palette palette) =>
        palette.Fibre
            ? string.Concat(
                Svg.Stripes("fibre-warp", 72d, 4d, 1d, "#5a3c14", 0.14d),
                Svg.Stripes("fibre-weft", -64d, 6d, 1d, "#5a3c14", 0.10d)
            )
            : string.Empty;

    private static string Grain(FaceMetrics face, Palette palette) =>
        palette.Fibre
            ? $"""<g style="mix-blend-mode:multiply" opacity="0.5">{Svg.Rect(0d, 0d, face.Width, face.Height, Svg.Url("fibre-warp"))}{Svg.Rect(0d, 0d, face.Width, face.Height, Svg.Url("fibre-weft"))}</g>"""
            : string.Empty;

    private static string Zip(PackFormat.Wrapper wrapper, FaceMetrics face) =>
        wrapper.Resealable
            ? $"""<g><rect x="0" y="56" width="{Svg.N(face.Width)}" height="15" fill="url(#rib)"/><rect x="0" y="56" width="{Svg.N(face.Width)}" height="1" fill="#ffffff" fill-opacity="0.5"/></g>"""
            : string.Empty;

    private static string SealShadows(double width, double height, double sealHeight) =>
        string.Concat(
            Svg.Rect(0d, sealHeight, width, _sealShadow, Svg.Url("seal-cast-top")),
            Svg.Rect(
                0d,
                height - sealHeight - _sealShadow,
                width,
                _sealShadow,
                Svg.Url("seal-cast-bot")
            )
        );

    // The crimp is cut into the outer edge, so the top seal is scalloped along its top and the
    // bottom seal is the same shape turned over.
    private static string Seal(double width, double y, double height, bool flip)
    {
        var steps = (int)Math.Ceiling(width / _crimp);
        var scallops = string.Concat(
            Enumerable
                .Range(0, steps)
                .Select(step => Svg.N(Math.Min(_crimp, width - (step * _crimp))))
                .Select(span => $"a7 3 0 0 0 {span} 0")
        );

        var path = $"M0 0{scallops}V{Svg.N(height)}H0Z";
        var turn = flip
            ? $"translate(0 {Svg.N(y + height)}) scale(1 -1)"
            : $"translate(0 {Svg.N(y)})";

        return $"""<g transform="{turn}"><path d="{path}" fill="url(#seal)"/><path d="{path}" fill="url(#rib)"/></g>""";
    }

    private static string Tab(PackFormat.Wrapper wrapper, bool booster)
    {
        if (!wrapper.Resealable)
        {
            return string.Empty;
        }

        var (x, w) = booster ? (145d, 170d) : (95d, 150d);
        var hole = x + (w / 2d) - 17d;

        return $"""
            <mask id="hang"><rect x="{Svg.N(x)}" y="-30" width="{Svg.N(
                w
            )}" height="52" fill="#ffffff"/><rect x="{Svg.N(
                hole
            )}" y="-14" width="34" height="17" rx="8.5" fill="#000000"/></mask>
            <g mask="url(#hang)"><path d="M{Svg.N(x)} 22V-22a8 8 0 0 1 8-8h{Svg.N(
                w - 16d
            )}a8 8 0 0 1 8 8v44Z" fill="url(#seal)"/><rect x="{Svg.N(x)}" y="-30" width="{Svg.N(
                w
            )}" height="10" rx="8" fill="#ffffff" fill-opacity="0.26"/></g>
            """;
    }

    private static string Ground(double width, double height, double drop) =>
        $"""<ellipse cx="{Svg.N(width / 2d)}" cy="{Svg.N(height + drop - 18d)}" rx="{Svg.N(width * 0.44d)}" ry="18" fill="url(#ground)"/>""";

    private readonly record struct Crease(
        double Angle,
        double From,
        double Peak,
        double Trough,
        double To,
        double Light,
        double Dark
    );
}

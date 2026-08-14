using Blokemon.PackGen.Domain;

namespace Blokemon.PackGen.Rendering;

/// <summary>The printed colours and surface response of a material.</summary>
/// <param name="Stock">The stops of the stock's own gradient.</param>
/// <param name="StockAngle">The CSS angle the stock gradient runs at.</param>
/// <param name="SealTop">The lit edge of a crimped seal.</param>
/// <param name="SealBottom">The shaded edge of a crimped seal.</param>
/// <param name="Print">The colour printed text takes.</param>
/// <param name="WordmarkFill">The wordmark fill.</param>
/// <param name="WordmarkLine">The wordmark outline.</param>
/// <param name="Specular">The strength of the travelling glint.</param>
/// <param name="Crinkle">The strength of the crease overlay.</param>
/// <param name="Fibre">Whether the surface shows a board grain.</param>
public sealed record Palette(
    Stop[] Stock,
    double StockAngle,
    string SealTop,
    string SealBottom,
    string Print,
    string WordmarkFill,
    string WordmarkLine,
    double Specular,
    double Crinkle,
    bool Fibre
)
{
    /// <summary>The palette a material prints with.</summary>
    /// <param name="material">The material to print.</param>
    /// <returns>The palette.</returns>
    public static Palette For(PackMaterial material) =>
        material switch
        {
            PackMaterial.Gloss => new Palette(
                [
                    new Stop(0d, "#0a2e1f"),
                    new Stop(0.26d, "#1e6b49"),
                    new Stop(0.46d, "#0f4531"),
                    new Stop(0.62d, "#2c7f57"),
                    new Stop(1d, "#0a2e1f"),
                ],
                94d,
                "#1b5a3e",
                "#0a2e1f",
                "#ffffff",
                "#ffe14f",
                "#0b3122",
                0.5d,
                0.7d,
                Fibre: false
            ),
            PackMaterial.Gold => new Palette(
                [
                    new Stop(0d, "#6b4a0c"),
                    new Stop(0.26d, "#d8ad3c"),
                    new Stop(0.46d, "#8a6414"),
                    new Stop(0.62d, "#f0cc63"),
                    new Stop(1d, "#6b4a0c"),
                ],
                94d,
                "#a87e1c",
                "#5c3f08",
                "#2a1c02",
                "#fff6d2",
                "#5c3f08",
                0.5d,
                0.7d,
                Fibre: false
            ),
            // Board takes no specular at all and will not hold a fold the way film does, so the
            // crease overlay drops with it rather than being tuned independently.
            PackMaterial.Kraft => new Palette(
                [new Stop(0d, "#bd9360"), new Stop(0.55d, "#ac804c"), new Stop(1d, "#916838")],
                180d,
                "#ac804c",
                "#7d5628",
                "#33200a",
                "#f7e7bd",
                "#5f3f16",
                0d,
                0.2d,
                Fibre: true
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(material)),
        };
}

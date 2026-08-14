using System.Text.RegularExpressions;
using Blokemon.PackGen.Domain;

namespace Blokemon.PackGen.Rendering;

/// <summary>The standalone document one printed object is delivered as.</summary>
public static partial class Document
{
    /// <summary>Wraps drawn artwork as a document.</summary>
    /// <param name="x">The left edge of the drawn extent.</param>
    /// <param name="y">The top edge of the drawn extent.</param>
    /// <param name="width">The drawn width.</param>
    /// <param name="height">The drawn height.</param>
    /// <param name="title">The accessible name of the object.</param>
    /// <param name="defs">The definitions the artwork refers to.</param>
    /// <param name="art">The artwork.</param>
    /// <param name="style">The stylesheet the artwork needs, empty when it needs none.</param>
    /// <param name="scope">The token every identity in this document is qualified by.</param>
    /// <returns>The document.</returns>
    public static string Wrap(
        double x,
        double y,
        double width,
        double height,
        string title,
        string defs,
        string art,
        string style,
        string scope
    )
    {
        var sheet = string.IsNullOrEmpty(style) ? string.Empty : $"<style>{style}</style>";

        var document = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="{Svg.N(x)} {Svg.N(y)} {Svg.N(
                width
            )} {Svg.N(height)}" width="{Svg.N(width)}" height="{Svg.N(
                height
            )}" role="img" aria-label="{Svg.Esc(title)}" data-generated-by="Blokemon.PackGen">
            <title>{Svg.Esc(title)}</title>
            <defs>{defs}</defs>{sheet}
            {art}
            </svg>

            """;

        return Qualify(document, scope);
    }

    // Identities are global to the page a fragment is embedded in, so each document carries its
    // own token on every definition, reference, class and animation it declares.
    private static string Qualify(string document, string scope)
    {
        document = Identity().Replace(document, match => $"id=\"{scope}-{match.Groups[1].Value}\"");
        document = Reference().Replace(document, match => $"url(#{scope}-{match.Groups[1].Value})");
        document = document.Replace(
            "class=\"glint\"",
            $"class=\"{scope}-glint\"",
            StringComparison.Ordinal
        );
        document = document.Replace(".glint{", $".{scope}-glint{{", StringComparison.Ordinal);
        document = document.Replace(".glint,", $".{scope}-glint,", StringComparison.Ordinal);
        document = document.Replace(
            "animation:glint",
            $"animation:{scope}-glint",
            StringComparison.Ordinal
        );
        return document.Replace(
            "@keyframes glint",
            $"@keyframes {scope}-glint",
            StringComparison.Ordinal
        );
    }

    [GeneratedRegex(@"id=""([\w-]+)""")]
    private static partial Regex Identity();

    [GeneratedRegex(@"url\(#([\w-]+)\)")]
    private static partial Regex Reference();

    /// <summary>The stylesheet driving a travelling glint.</summary>
    /// <param name="travel">The distance the glint crosses from rest to rest.</param>
    /// <param name="delay">The offset into the shared cycle.</param>
    /// <returns>The stylesheet.</returns>
    public static string Glint(double travel, GlintDelay delay) =>
        // Written as a template rather than interpolated: CSS closes nested blocks with runs of
        // braces that a raw interpolated string would read as holes.
        """
            .glint{animation:glint 5.5s linear infinite;animation-delay:DELAY}
            @keyframes glint{0%{transform:translate(-TRAVELpx,0);animation-timing-function:cubic-bezier(.35,0,.25,1)}13%{transform:translate(TRAVELpx,0)}100%{transform:translate(TRAVELpx,0)}}
            @media(prefers-reduced-motion:reduce){.glint{animation:none}}
            """.Replace("DELAY", delay.ToCssValue(), StringComparison.Ordinal).Replace(
            "TRAVEL",
            Svg.N(travel),
            StringComparison.Ordinal
        );
}

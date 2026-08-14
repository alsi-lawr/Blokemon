using Blokemon.CardGen.Domain;

namespace Blokemon.CardGen.Rendering;

/// <summary>The standalone document one card is delivered as.</summary>
public sealed class CardDocument
{
    private const string _xhtml = "http://www.w3.org/1999/xhtml";
    private readonly Illustrations _art;

    private CardDocument(string stylesheet, Illustrations art)
    {
        Stylesheet = stylesheet;
        _art = art;
    }

    /// <summary>The stylesheet every card this printer builds is printed under.</summary>
    public string Stylesheet { get; }

    /// <summary>Assembles the printer for a content directory.</summary>
    /// <param name="content">The directory holding the illustrations.</param>
    /// <returns>The printer.</returns>
    public static CardDocument Load(string content)
    {
        var design = Path.Combine(AppContext.BaseDirectory, "Content", "blokemon-card.css");

        return new CardDocument(File.ReadAllText(design), Illustrations.Load(content));
    }

    /// <summary>Prints a card as its own document.</summary>
    /// <param name="card">The card to print.</param>
    /// <returns>The document.</returns>
    public string Build(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        // The face is the approved card markup under the approved stylesheet, carried inside an
        // SVG viewport. The design travels with the card because the design is the card; the
        // typefaces do not, because the page a card is embedded into already provides them.
        // The stylesheet is emitted bare rather than in a CDATA section: it holds no ampersand
        // or angle bracket, and CDATA would become a bogus comment if a card were dropped into
        // an HTML page rather than parsed as XML.
        var label = Label(card);

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 778 1080" width="778" height="1080" role="img" aria-label="{Esc(
                label
            )}" data-card-id="{Esc(card.Id.Value)}" data-generated-by="Blokemon.CardGen">
            <title>{Esc(label)}</title>
            <foreignObject x="0" y="0" width="778" height="1080">
            <div xmlns="{_xhtml}" class="blokemon-card-scale">{TypeGlyphs.Sprite()}{CardRenderer.Render(
                card,
                _art
            )}</div>
            </foreignObject>
            </svg>

            """;
    }

    private static string Label(ICard card) =>
        card.Regions.OfType<CardRegion.Vitality>().FirstOrDefault()
            is { Points: { } points } vitality
            ? $"{card.DisplayName}, {points} HP, {vitality.Type} Blokemon"
            : card.DisplayName;

    private static string Esc(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}

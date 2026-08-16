using System.Text.RegularExpressions;
using Blokemon.Web.Client.Api;

namespace Blokemon.Web.Content;

/// <summary>The card illustrations a browser needs, ordered by how soon it is likely to need them.</summary>
public static partial class CardArtAssets
{
    /// <summary>Orders every illustration the catalogue references for background warming.</summary>
    /// <param name="catalogue">The card catalogue.</param>
    /// <param name="state">The loaded player state, when the application has one.</param>
    /// <returns>The distinct illustration urls, the ones most likely to be seen first.</returns>
    public static IReadOnlyList<string> WarmingOrder(
        BlokemonCatalogue catalogue,
        ApplicationView? state
    )
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var ordered = new List<string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        Collect(catalogue.ReverseFaceHtml, ordered, taken);
        foreach (var card in Likeliest(catalogue, state))
        {
            Collect(card.FaceHtml, ordered, taken);
        }
        foreach (
            var card in catalogue.Cards.OrderBy(static card => card.Id, StringComparer.Ordinal)
        )
        {
            Collect(card.FaceHtml, ordered, taken);
        }
        return ordered;
    }

    private static IEnumerable<CardView> Likeliest(
        BlokemonCatalogue catalogue,
        ApplicationView? state
    )
    {
        if (state is null)
        {
            yield break;
        }

        var known = catalogue.Cards.ToDictionary(static card => card.Id, StringComparer.Ordinal);
        foreach (var card in state.Cards.Where(static card => card.OwnedQuantity > 0))
        {
            yield return card;
        }
        foreach (var entry in state.Decks.SelectMany(static deck => deck.Entries))
        {
            if (known.TryGetValue(entry.CardId, out var card))
            {
                yield return card;
            }
        }
        foreach (var starter in state.StarterDecks)
        {
            yield return starter.Leader;
            foreach (var entry in starter.Entries)
            {
                if (known.TryGetValue(entry.CardId, out var card))
                {
                    yield return card;
                }
            }
        }
    }

    private static void Collect(string html, List<string> ordered, HashSet<string> taken)
    {
        foreach (Match reference in ArtReference().Matches(html))
        {
            var url = reference.Groups[1].Value;
            if (taken.Add(url))
            {
                ordered.Add(url);
            }
        }
    }

    [GeneratedRegex("src=\"(/art/[^\"]+)\"")]
    private static partial Regex ArtReference();
}

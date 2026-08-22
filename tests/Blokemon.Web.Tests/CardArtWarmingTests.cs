using System.Text.RegularExpressions;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Web.Content;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed partial class CardArtWarmingTests
{
    [Test]
    public void WarmingOrder_CoversEveryShippedIllustrationOnceBehindTheCardBack()
    {
        var catalogue = Catalogue();

        var order = CardArtAssets.WarmingOrder(catalogue, null);

        order.ShouldAllBe(static art => art.Source.StartsWith("/art/", StringComparison.Ordinal));
        order
            .Select(static art => art.Source)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(order.Count);
        order[0].Source.ShouldBe("/art/card-back.svg");

        // Against what is delivered rather than what was approved: a browser is sent the WebP
        // derived from the approved artwork, and the placeholders beside it are not sent at all
        // because every card already carries its own inside it.
        order
            .Select(static art => art.Source["/art/".Length..])
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(Delivered().Select(static widths => widths[^1]))
            .ShouldBeTrue();
    }

    [Test]
    public void WarmingOrder_LetsTheBrowserChooseBetweenTheDeliveredWidths()
    {
        var catalogue = Catalogue();

        var order = CardArtAssets.WarmingOrder(catalogue, null);

        // Vector artwork is one file at every size, so there is nothing to choose between.
        order
            .Where(static art => art.Source.EndsWith(".svg", StringComparison.Ordinal))
            .ShouldAllBe(static art => art.Candidates.Length == 0);

        var chooseable = order
            .Where(static art => !art.Source.EndsWith(".svg", StringComparison.Ordinal))
            .ToArray();

        chooseable.ShouldNotBeEmpty();
        foreach (var art in chooseable)
        {
            var offered = Candidate()
                .Matches(art.Candidates)
                .Select(static candidate => candidate.Groups["url"].Value)
                .ToArray();

            offered.Length.ShouldBeGreaterThan(1);
            offered.ShouldContain(art.Source);
        }
    }

    [Test]
    public void WarmingOrder_LeadsWithTheCardsThePlayerAlreadyHas()
    {
        var catalogue = Catalogue();
        var cards = catalogue
            .Cards.Where(static card => Illustrations(card).Length == 1)
            .OrderBy(static card => card.Id, StringComparer.Ordinal)
            .ToArray();
        var owned = cards[^1];
        var inDeck = cards[^2];
        var starterLeader = cards[^3];
        var unseen = cards[0];
        var state = new ApplicationView(
            null,
            [owned with { OwnedQuantity = 2 }],
            [new(Guid.NewGuid(), "League deck", 1, [new(inDeck.Id, 4)], true, [], [])],
            [
                new(
                    "starter",
                    "Starter",
                    "Beer",
                    "Lead",
                    "A starter deck.",
                    starterLeader,
                    [],
                    1,
                    0,
                    0,
                    false
                ),
            ],
            catalogue.PackPresentation,
            null,
            null,
            null
        );

        var order = Position(CardArtAssets.WarmingOrder(catalogue, state));

        order[Illustrations(owned)[0]].ShouldBeLessThan(order[Illustrations(inDeck)[0]]);
        order[Illustrations(inDeck)[0]].ShouldBeLessThan(order[Illustrations(starterLeader)[0]]);
        order[Illustrations(starterLeader)[0]].ShouldBeLessThan(order[Illustrations(unseen)[0]]);
    }

    private static BlokemonCatalogue Catalogue() =>
        BlokemonCatalogueBuilder.Load(Path.Combine(AppContext.BaseDirectory, "content"));

    /// <summary>Every delivered illustration, as its widths narrowest first.</summary>
    private static IEnumerable<string[]> Delivered() =>
        Directory
            .EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "content", "art-web"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(static file => !file.EndsWith(".lqip.webp", StringComparison.Ordinal))
            .GroupBy(static file =>
                Variant().Match(file) is { Success: true } sized
                    ? sized.Groups["stem"].Value
                    : Path.GetFileNameWithoutExtension(file)
            )
            .Select(static widths =>
                widths
                    .OrderBy(static file =>
                        Variant().Match(file) is { Success: true } sized
                            ? int.Parse(sized.Groups["width"].Value)
                            : 0
                    )
                    .ToArray()
            );

    private static Dictionary<string, int> Position(IReadOnlyList<ArtWarming> order) =>
        order
            .Select(static (art, index) => (art.Source, index))
            .ToDictionary(
                static entry => entry.Source,
                static entry => entry.index,
                StringComparer.Ordinal
            );

    private static string[] Illustrations(CardView card) =>
        ArtReference()
            .Matches(card.FaceHtml)
            .Select(static reference => reference.Groups[1].Value)
            .ToArray();

    [GeneratedRegex("src=\"(/art/[^\"]+)\"")]
    private static partial Regex ArtReference();

    [GeneratedRegex(@"^(?<stem>.+)-(?<width>\d+)\.webp$")]
    private static partial Regex Variant();

    [GeneratedRegex(@"(?<url>/art/[^\s,]+)\s+\d+w")]
    private static partial Regex Candidate();
}

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

        order.ShouldAllBe(static url => url.StartsWith("/art/", StringComparison.Ordinal));
        order.Distinct(StringComparer.Ordinal).Count().ShouldBe(order.Count);
        order[0].ShouldBe("/art/card-back.svg");
        order
            .Select(static url => url["/art/".Length..])
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(
                Directory
                    .EnumerateFiles(
                        Path.Combine(AppContext.BaseDirectory, "content", "art"),
                        "*.svg"
                    )
                    .Select(Path.GetFileName)
                    .OfType<string>()
            )
            .ShouldBeTrue();
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

    private static Dictionary<string, int> Position(IReadOnlyList<string> order) =>
        order
            .Select(static (url, index) => (url, index))
            .ToDictionary(
                static entry => entry.url,
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
}

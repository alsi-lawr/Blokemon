using System.Text.Json.Nodes;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Core.SetDesign;
using Blokemon.Web.Content;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class EnergyProjectionTests
{
    private static readonly IReadOnlyDictionary<string, string> _basicEnergy = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["VIM-BLAZED"] = "Blazed",
        ["VIM-CURRY"] = "Curry",
        ["VIM-SOBER"] = "Sober",
        ["VIM-BEER"] = "Beer",
        ["VIM-GEEKED"] = "Geeked",
        ["VIM-LAIRY"] = "Lairy",
    };

    private static readonly string[] _mechanicalNames =
    [
        "Grass",
        "Fire",
        "Water",
        "Lightning",
        "Psychic",
        "Fighting",
        "Darkness",
        "Colorless",
        "Dragon",
        "Metal",
    ];

    [Test]
    public void CatalogueCards_ProjectBasicAndDoubleColorlessEnergySemantics()
    {
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var cards = catalogue.Cards.ToDictionary(static card => card.Id, StringComparer.Ordinal);

        foreach (var expected in _basicEnergy)
        {
            var card = cards[expected.Key];
            card.Kind.ShouldBe(CardKindView.Energy);
            card.Type.ShouldBe(expected.Value);
            card.Detail.ShouldBe("Basic Energy");
            card.Rules.ShouldHaveSingleItem().Name.ShouldBe("Basic Energy");
        }

        var doubleColorless = cards["VIM-DODGY"];
        doubleColorless.Kind.ShouldBe(CardKindView.Energy);
        doubleColorless.Type.ShouldBe("Local");
        doubleColorless.Detail.ShouldBe("Special Energy");
        var doubleColorlessRule = doubleColorless.Rules.ShouldHaveSingleItem();
        doubleColorlessRule.Name.ShouldBe("Special Energy");
        doubleColorlessRule.Text.ShouldNotBeNull();
        doubleColorlessRule.Text!.ShouldContain("provides 2 Local Energy");
        doubleColorlessRule.Text.ShouldContain("does not count as Basic Energy");

        cards["VIM-BEER"].Name.ShouldBe("Dutch Courage");

        var projectedMechanicalFields = catalogue
            .Cards.SelectMany(card =>
                new[] { card.Type, card.Detail }.Concat(
                    card.Rules.SelectMany(static rule => rule.EnergyCost)
                )
            )
            .ToArray();

        projectedMechanicalFields.ShouldNotContain(value =>
            _mechanicalNames.Contains(value, StringComparer.Ordinal)
        );
        projectedMechanicalFields.ShouldNotContain("Basic Vim");
    }

    [Test]
    public void CatalogueCardProjection_ExcludesItsEmbeddedRawMechanicsJson()
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "content");
        var document = JsonNode
            .Parse(BlokemonCatalogueBuilder.Load(contentRoot).ToBootstrapJson())!
            .AsObject();

        var rawMechanics = document["mechanicsJson"]!.GetValue<string>();
        var rawMechanicalTypes = JsonNode.Parse(rawMechanics)!["approvedMechanicalDisplayMap"]!
            .AsArray()
            .Select(mapping => mapping!["mechanicalType"]!.GetValue<string>());
        rawMechanicalTypes.ShouldContain("Lightning");

        var cards = document["cards"]!.AsArray();
        var projectedMechanicalFields = cards.SelectMany(card =>
        {
            var projected = card!.AsObject();
            return new[]
            {
                projected["type"]!.GetValue<string>(),
                projected["detail"]!.GetValue<string>(),
            }.Concat(
                projected["rules"]!
                    .AsArray()
                    .SelectMany(rule =>
                        rule!["energyCost"]!
                            .AsArray()
                            .Select(static cost => cost!.GetValue<string>())
                    )
            );
        });

        projectedMechanicalFields.ShouldNotContain(value =>
            _mechanicalNames.Contains(value, StringComparer.Ordinal) || value == "Basic Vim"
        );
    }

    [Test]
    public void DoubleColorlessEnergy_DoesNotSatisfyAStarterBasicEnergySlot()
    {
        var authorityRoot = Path.Combine(AppContext.BaseDirectory, "content", "authorities");
        var mechanics = BlokemonSetJson.RuntimeManifest(
            File.ReadAllText(Path.Combine(authorityRoot, "mechanics.json"))
        );
        var starters = JsonNode
            .Parse(File.ReadAllText(Path.Combine(authorityRoot, "starter-decks.json")))!
            .AsObject();
        var entries = starters["decks"]![0]!["entries"]!.AsArray();
        var basicEnergy = entries.Single(entry =>
            entry!["cardId"]!.GetValue<string>() == "VIM-BLAZED"
        )!;
        basicEnergy["quantity"] = 14;
        entries.Add(new JsonObject { ["cardId"] = "VIM-DODGY", ["quantity"] = 1 });

        var failure = Should.Throw<InvalidDataException>(() =>
            StarterDeckCatalogue.LoadJson(starters.ToJsonString(), mechanics)
        );

        failure.Message.ShouldContain("15 Basic Energy");
    }
}

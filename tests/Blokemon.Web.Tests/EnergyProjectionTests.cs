using System.Text;
using System.Text.Json.Nodes;
using Blokemon.App.Contracts;
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
        ["VIM-DODGY"] = "Dodgy",
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
    public void CatalogueCards_ProjectApprovedBasicEnergyMetadataAndRuleCosts()
    {
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var cards = catalogue.Cards.ToDictionary(static card => card.Id, StringComparer.Ordinal);

        foreach (var expected in _basicEnergy)
        {
            var card = cards[expected.Key];
            card.Kind.ShouldBe(CardKindView.BasicVim);
            card.Type.ShouldBe(expected.Value);
            card.Detail.ShouldBe("Basic Energy");
            card.Rules.ShouldHaveSingleItem().Name.ShouldBe("Basic Energy");
        }

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
    public void GeneratedCatalogue_ExactlyMatchesFreshBuilderOutput()
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "content");
        var generated = Encoding.UTF8.GetBytes(
            BlokemonCatalogueBuilder.Load(contentRoot).ToBootstrapJson()
        );
        var committed = File.ReadAllBytes(Path.Combine(contentRoot, "catalogue.json"));

        generated.ShouldBe(committed);
    }

    [Test]
    public void GeneratedCatalogue_CardProjectionExcludesItsEmbeddedRawMechanicsJson()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "content", "catalogue.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

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
}
